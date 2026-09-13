using System.IO;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace LuKnight.Services;

public sealed record SpeechTranscriptionResult(
    string Text);

public interface ISpeechToTextService
{
    bool IsModelReady { get; }

    Task<SpeechTranscriptionResult> TranscribeAsync(
        VoiceCaptureResult capture,
        CancellationToken cancellationToken = default);
}

public sealed class LocalWhisperSpeechToTextService
    : ISpeechToTextService
{
    private readonly SemaphoreSlim _gate =
        new(1, 1);

    private readonly string _modelPath;

    public LocalWhisperSpeechToTextService(
        string? modelPath = null)
    {
        _modelPath =
            modelPath ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "LuKnight",
                "Models",
                "ggml-base.bin");
    }

    public bool IsModelReady =>
        File.Exists(_modelPath);

    public async Task<SpeechTranscriptionResult>
        TranscribeAsync(
            VoiceCaptureResult capture,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (capture.WavData.Length == 0)
        {
            throw new InvalidOperationException(
                "Rekaman suara kosong.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureModelAsync(
                cancellationToken).ConfigureAwait(false);

            using WhisperFactory factory =
                WhisperFactory.FromPath(
                    _modelPath);

            using WhisperProcessor processor =
                factory
                    .CreateBuilder()
                    .WithLanguage("auto")
                    .Build();

            using var audioStream =
                new MemoryStream(
                    capture.WavData,
                    writable: false);

            var transcript =
                new StringBuilder();

            await foreach (
                var segment in processor.ProcessAsync(
                    audioStream,
                    cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(
                        segment.Text))
                {
                    transcript.Append(
                        segment.Text);
                }
            }

            string text =
                transcript
                    .ToString()
                    .Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    "Tidak ada ucapan yang dapat dikenali.");
            }

            return new SpeechTranscriptionResult(
                text);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureModelAsync(
        CancellationToken cancellationToken)
    {
        if (File.Exists(_modelPath))
            return;

        string? directory =
            Path.GetDirectoryName(
                _modelPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidOperationException(
                "Lokasi model STT tidak valid.");
        }

        Directory.CreateDirectory(
            directory);

        string temporaryPath =
            _modelPath + ".download";

        try
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }

            using Stream modelStream =
                await WhisperGgmlDownloader
                    .Default
                    .GetGgmlModelAsync(
                        GgmlType.Base);

            await using (var destination =
                new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
            {
                await modelStream.CopyToAsync(
                    destination,
                    cancellationToken);

                await destination.FlushAsync(
                    cancellationToken);
            }

            File.Move(
                temporaryPath,
                _modelPath,
                overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
            }

            throw;
        }
    }
}
