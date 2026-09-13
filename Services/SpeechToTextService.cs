using System.Buffers.Binary;
using NAudio.Wave;
using System.IO;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace LuKnight.Services;

public sealed record SpeechTranscriptionResult(
    string Text);

public sealed class NoSpeechDetectedException
    : Exception
{
    public NoSpeechDetectedException()
        : base("Tidak terdengar ucapan yang cukup jelas.")
    {
    }
}

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
    private const uint GgmlMagic = 0x67676D6C;
    private const long MinimumModelBytes = 1024 * 1024;

    private const double MinimumAudioRms = 0.001;
    private const float MinimumAudioPeak = 0.008f;
    private static readonly TimeSpan MinimumSpeechDuration =
        TimeSpan.FromMilliseconds(250);

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
        IsModelFileUsable(_modelPath);

    public async Task<SpeechTranscriptionResult>
        TranscribeAsync(
            VoiceCaptureResult capture,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            capture);

        cancellationToken
            .ThrowIfCancellationRequested();

        if (capture.WavData.Length == 0)
        {
            throw new InvalidDataException(
                "Rekaman suara kosong.");
        }

        EnsureSpeechPresent(
            capture,
            cancellationToken);

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
                throw new NoSpeechDetectedException();
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
        if (IsModelReady)
            return;

        if (File.Exists(_modelPath))
        {
            TryDelete(_modelPath);
        }

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
                        GgmlType.Base,
                        cancellationToken: cancellationToken);

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

            if (!IsModelFileUsable(
                    temporaryPath))
            {
                TryDelete(
                    temporaryPath);

                throw new InvalidDataException(
                    "Model Speech-to-Text yang diunduh tidak valid.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                _modelPath,
                overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static bool IsModelFileUsable(
        string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists ||
                info.Length < MinimumModelBytes)
            {
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            Span<byte> header =
                stackalloc byte[sizeof(uint)];

            if (stream.Read(header) !=
                header.Length)
            {
                return false;
            }

            return BinaryPrimitives
                .ReadUInt32LittleEndian(header) ==
                GgmlMagic;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureSpeechPresent(
        VoiceCaptureResult capture,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (capture.WavData.Length < 44)
        {
            throw new InvalidDataException(
                "Data WAV voice tidak valid.");
        }

        if (capture.Duration <
            MinimumSpeechDuration)
        {
            throw new NoSpeechDetectedException();
        }

        using var stream =
            new MemoryStream(
                capture.WavData,
                writable: false);

        using var reader =
            new WaveFileReader(stream);

        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
            reader.WaveFormat.SampleRate != 16000 ||
            reader.WaveFormat.Channels != 1 ||
            reader.WaveFormat.BitsPerSample != 16)
        {
            throw new InvalidDataException(
                "Format voice harus 16 kHz, 16-bit, mono.");
        }

        ISampleProvider samples =
            reader.ToSampleProvider();

        float[] buffer =
            new float[4096];

        long sampleCount = 0;
        double sumSquares = 0;
        float peak = 0;

        int read;

        while ((read =
            samples.Read(buffer.AsSpan())) > 0)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            for (int i = 0; i < read; i++)
            {
                float sample =
                    buffer[i];

                float absolute =
                    Math.Abs(sample);

                if (absolute > peak)
                    peak = absolute;

                sumSquares +=
                    sample * sample;

                sampleCount++;
            }
        }

        if (sampleCount < 16000 * MinimumSpeechDuration.TotalSeconds)
            throw new NoSpeechDetectedException();

        double rms =
            Math.Sqrt(
                sumSquares /
                sampleCount);

        if (peak < MinimumAudioPeak ||
            rms < MinimumAudioRms)
        {
            throw new NoSpeechDetectedException();
        }
    }

    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
