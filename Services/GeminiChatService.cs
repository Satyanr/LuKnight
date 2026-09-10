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

namespace LuKnight.Services;

public sealed class GeminiChatService : IChatService
{
    public const string ModelName = "gemini-3.8-flash";

    private const int MaxHistoryTurns = 20;

    private const string SystemInstruction =
        "Kamu adalah Lu-Knight, asisten desktop kecil yang ramah, ringkas, dan membantu. " +
        "Gunakan Bahasa Indonesia secara default kecuali pengguna meminta bahasa lain. " +
        "Saat ini kamu hanya memiliki kemampuan percakapan. Jangan mengaku sudah membuka aplikasi, " +
        "mengubah file, atau menjalankan tindakan di PC kecuali aplikasi benar-benar memberikan hasil tindakan tersebut.";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly List<ChatTurn> _history = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string DisplayName => $"Gemini • {ModelName}";

    public async Task<string> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Pesan tidak boleh kosong.", nameof(message));

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            string apiKey = GetApiKey();
            string trimmedMessage = message.Trim();

            var requestHistory = new List<ChatTurn>(_history)
            {
                new("user", trimmedMessage)
            };

            var payload = new
            {
                system_instruction = new
                {
                    parts = new[]
                    {
                        new { text = SystemInstruction }
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
                $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

#if DEBUG
            Debug.WriteLine(
                $"Gemini request: model={ModelName}, historyTurns={requestHistory.Count}, apiKeyLength={apiKey.Length}");
#endif

            HttpResponseMessage response;

            try
            {
                response = await HttpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Gemini tidak merespons dalam 30 detik. Periksa koneksi internet lalu coba lagi.",
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

                _history.Add(new ChatTurn("user", trimmedMessage));
                _history.Add(new ChatTurn("model", reply));
                TrimHistory();

                return reply;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static string GetApiKey()
    {
        string? apiKey =
            Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "GEMINI_API_KEY tidak ditemukan. Tutup lalu buka kembali VS Code/terminal setelah menjalankan setx.");
        }

        return apiKey.Trim();
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

    private static Exception CreateApiException(
        HttpStatusCode statusCode,
        string responseBody)
    {
        string apiMessage = ReadApiErrorMessage(responseBody);

        string message = statusCode switch
        {
            HttpStatusCode.BadRequest =>
                $"Permintaan ke Gemini ditolak (400). {apiMessage}",

            HttpStatusCode.Unauthorized =>
                "API key Gemini tidak valid atau tidak diterima (401). Periksa GEMINI_API_KEY.",

            HttpStatusCode.Forbidden =>
                $"Akses Gemini ditolak (403). Periksa izin API key/project. {apiMessage}",

            HttpStatusCode.NotFound =>
                $"Model Gemini tidak ditemukan (404). Model yang digunakan: {ModelName}. {apiMessage}",

            HttpStatusCode.TooManyRequests =>
                "Batas permintaan Gemini tercapai (429). Tunggu sebentar lalu coba lagi.",

            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout =>
                $"Layanan Gemini sedang bermasalah ({(int)statusCode}). Coba lagi nanti.",

            _ =>
                $"Gemini API error {(int)statusCode}. {apiMessage}"
        };

        return new InvalidOperationException(message);
    }

    private static string ReadApiErrorMessage(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                error.TryGetProperty("message", out JsonElement messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Abaikan dan gunakan fallback singkat di bawah.
        }

        string compact = responseBody.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (compact.Length > 300)
            compact = compact[..300] + "…";

        return compact;
    }

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
