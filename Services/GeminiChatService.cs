using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LuKnight.Models;

namespace LuKnight.Services;

public sealed class GeminiChatService : IChatService
{
    public const string ModelName = "gemini-3.8-flash";

    private const int MaxHistoryTurns = 20;

    private const string FallbackIdentityInstruction =
        """
        Kamu adalah Lu-Knight,
        asisten desktop kecil yang ramah,
        ringkas, dan membantu.
        """;

    private const string CapabilityBoundaryInstruction =
        """
        Jangan mengaku sudah membuka aplikasi,
        melihat layar, mengubah file,
        menjalankan command, atau melakukan
        tindakan di PC kecuali aplikasi benar-benar
        memberikan hasil tindakan tersebut.
        """;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly List<ChatTurn> _history = new();
    private readonly Func<string?> _key;
    private readonly HttpClient _client;
    private ChatSettings _options;
    public GeminiChatService(Func<string?>? key = null, ChatSettings? options = null, HttpClient? client = null)
    { _key = key ?? (() => Environment.GetEnvironmentVariable("GEMINI_API_KEY")); _options = options ?? new(); _client = client ?? HttpClient; }
    public void Configure(ChatSettings options) { _options = options; if (!options.RememberConversation) _history.Clear(); }
    public void ClearConversation() => _history.Clear();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string DisplayName => $"Gemini • {_options.Model}";

    public Task<string> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(message, assistantInstruction: null, cancellationToken);

    public async Task<string> SendMessageAsync(
        string message,
        string? assistantInstruction,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Pesan tidak boleh kosong.", nameof(message));

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            string apiKey = _key()?.Trim() ?? "";
            if (apiKey.Length == 0) throw new InvalidOperationException("API key belum dikonfigurasi.");
            string trimmedMessage = message.Trim();

            var requestHistory = new List<ChatTurn>(_options.RememberConversation ? _history : [])
            {
                new("user", trimmedMessage)
            };

            var payload = new
            {
                system_instruction = new
                {
                    parts = new[]
                    {
                        new { text = BuildInstruction(_options, assistantInstruction) }
                    }
                            },

                            contents = requestHistory
                    .Select(turn => new
                    {
                        role = turn.Role,
                        parts = new[]
                        {
                            new { text = turn.Text }
                        }
                    })
                .ToArray(),

                generationConfig = new
                {
                    thinkingConfig = new
                    {
                        thinkingLevel = "low"
                    }
                }
            };

            string json = JsonSerializer.Serialize(payload);
            string url =
                $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

#if DEBUG
            Debug.WriteLine(
                $"Gemini request: model={_options.Model}, historyTurns={requestHistory.Count}, apiKeyLength={apiKey.Length}");
#endif

            HttpResponseMessage response;

            try
            {
                response = await _client.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Gemini tidak merespons dalam 60 detik. Periksa koneksi internet lalu coba lagi.",
                    ex);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    "Tidak dapat terhubung ke Gemini. Periksa koneksi internet atau firewall.",
                    ex);
            }

            using (response)
            {
                string responseBody =
                    await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateApiException(response.StatusCode, responseBody);
                }

                string reply = ExtractReply(responseBody);

                if (_options.RememberConversation)
                {
                    _history.Add(new ChatTurn("user", trimmedMessage));
                    _history.Add(new ChatTurn("model", reply));
                    TrimHistory();
                }

                return reply;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public static string BuildInstruction(ChatSettings options, string? assistantInstruction = null)
    {
        string identity = string.IsNullOrWhiteSpace(assistantInstruction)
            ? FallbackIdentityInstruction : assistantInstruction.Trim();
        string language = options.Language switch
        {
            ChatLanguage.Indonesia => "Jawab dalam Bahasa Indonesia.",
            ChatLanguage.English => "Reply in English.",
            _ => "Gunakan bahasa yang sama dengan pesan terbaru pengguna secara natural."
        };
        string length = options.ResponseLength switch
        {
            ResponseLength.Short => "Jawaban harus sangat ringkas, maksimal sekitar dua kalimat pendek kecuali dibutuhkan format khusus.",
            ResponseLength.Detailed => "Berikan penjelasan menyeluruh dengan detail atau contoh berguna ketika relevan.",
            _ => "Jawab secara ringkas tetapi lengkap."
        };
        string style = options.Style switch
        {
            ResponseStyle.Professional => "Gunakan gaya profesional, jelas, dan tenang.",
            ResponseStyle.Playful => "Gunakan gaya hangat dan sedikit playful ketika sesuai konteks.",
            _ => "Gunakan gaya ramah dan membantu."
        };
        return $"""
            {identity}

            {CapabilityBoundaryInstruction}

            Preferensi respons saat ini:
            - {language}
            - {length}
            - {style}
            """;
    }

    private static string ExtractReply(string responseBody)
    {
        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("candidates", out JsonElement candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            string blockReason = TryReadBlockReason(root);

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(blockReason)
                    ? "Gemini tidak mengembalikan kandidat jawaban."
                    : $"Gemini memblokir respons: {blockReason}");
        }

        JsonElement candidate = candidates[0];
        var textBuilder = new StringBuilder();

        if (candidate.TryGetProperty("content", out JsonElement content) &&
            content.TryGetProperty("parts", out JsonElement parts) &&
            parts.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement part in parts.EnumerateArray())
            {
                bool isThought =
                    part.TryGetProperty("thought", out JsonElement thoughtElement) &&
                    thoughtElement.ValueKind == JsonValueKind.True;

                if (isThought)
                    continue;

                if (part.TryGetProperty("text", out JsonElement textElement) &&
                    textElement.ValueKind == JsonValueKind.String)
                {
                    string? text = textElement.GetString();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (textBuilder.Length > 0)
                            textBuilder.AppendLine();

                        textBuilder.Append(text.Trim());
                    }
                }
            }
        }

        if (textBuilder.Length > 0)
            return textBuilder.ToString();

        string finishReason =
            candidate.TryGetProperty("finishReason", out JsonElement finishElement) &&
            finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString() ?? "tidak diketahui"
                : "tidak diketahui";

        throw new InvalidOperationException(
            $"Gemini tidak memberikan teks jawaban. Finish reason: {finishReason}.");
    }

    private static Exception CreateApiException(HttpStatusCode statusCode, string responseBody) => new InvalidOperationException(statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API key atau izin Gemini ditolak. Periksa key dan project.",
        HttpStatusCode.NotFound => "Model Gemini tidak tersedia. Periksa nama model.",
        HttpStatusCode.TooManyRequests => "Kuota Gemini tercapai. Coba kembali nanti.",
        _ => $"Permintaan Gemini gagal (HTTP {(int)statusCode})."
    });

    private static string TryReadBlockReason(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out JsonElement promptFeedback) &&
            promptFeedback.TryGetProperty("blockReason", out JsonElement blockReason) &&
            blockReason.ValueKind == JsonValueKind.String)
        {
            return blockReason.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private void TrimHistory()
    {
        if (_history.Count <= MaxHistoryTurns)
            return;

        int removeCount = _history.Count - MaxHistoryTurns;

        // History selalu ditambahkan berpasangan user/model.
        if (removeCount % 2 != 0)
            removeCount++;

        removeCount = Math.Min(removeCount, _history.Count);
        _history.RemoveRange(0, removeCount);
    }

    private sealed record ChatTurn(string Role, string Text);
}
