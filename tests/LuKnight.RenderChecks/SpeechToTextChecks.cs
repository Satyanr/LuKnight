using System.Buffers.Binary;
using System.IO;
using LuKnight.Models;
using LuKnight.Services;
using NAudio.Wave;

internal static partial class Program
{
    private sealed class FakeSpeechToTextService
        : ISpeechToTextService
    {
        private readonly string _text;

        public FakeSpeechToTextService(
            string text)
        {
            _text = text;
        }

        public bool IsModelReady => true;

        public int Calls { get; private set; }

        public Task<SpeechTranscriptionResult>
            TranscribeAsync(
                VoiceCaptureResult capture,
                CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Calls++;

            return Task.FromResult(
                new SpeechTranscriptionResult(
                    _text));
        }
    }

    private static async Task
        CheckSpeechToTextHardeningAsync()
    {
        var fake =
            new FakeSpeechToTextService(
                "tolong buka notepad");

        var settings =
            new SettingsService();

        settings.Update(
            settings.Current with
            {
                Chat = new ChatSettings
                {
                    Provider =
                        ChatProvider.Local,

                    UseVoiceInput = true
                }
            });

        var services =
            new AppServices(
                settings,
                new FakeCredentials(),
                speechToText: fake);

        VoiceCaptureResult audible =
            CreateVoiceCapture(
                amplitude: 2500);

        SpeechTranscriptionResult result =
            await services
                .SpeechToText
                .TranscribeAsync(
                    audible);

        Require(
            result.Text ==
                "tolong buka notepad" &&
            fake.Calls == 1,
            "Injected Speech-to-Text service was not used.");

        Require(
            services.Assistant
                .Conversation.Count == 0,
            "Phase 9B routed transcript into Assistant before Phase 9C.");

        string tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-STT-" +
                Guid.NewGuid()
                    .ToString("N"));

        Directory.CreateDirectory(
            tempDirectory);

        try
        {
            string modelPath =
                Path.Combine(
                    tempDirectory,
                    "ggml-base.bin");

            await using (
                FileStream corrupt =
                    new(
                        modelPath,
                        FileMode.Create,
                        FileAccess.Write))
            {
                corrupt.SetLength(
                    2 * 1024 * 1024);
            }

            var local =
                new LocalWhisperSpeechToTextService(
                    modelPath);

            Require(
                !local.IsModelReady,
                "Corrupt Whisper model was treated as ready.");

            await File.WriteAllBytesAsync(modelPath, []);
            Require(!local.IsModelReady, "Empty model was treated as ready.");
            await File.WriteAllBytesAsync(modelPath, [0x6c, 0x6d, 0x67, 0x67]);
            Require(!local.IsModelReady, "Truncated model was treated as ready.");

            foreach (var quiet in new[]
            {
                CreateVoiceCapture(10),
                CreateVoiceCapture(2500, 100),
                CreateVoiceCapture(2500, 100) with { Duration = TimeSpan.FromSeconds(1) },
                CreateVoiceCapture(0, 0) with { Duration = TimeSpan.FromSeconds(1) }
            })
            {
                bool rejected = false;
                try { await local.TranscribeAsync(quiet); }
                catch (NoSpeechDetectedException) { rejected = true; }
                Require(rejected, "Quiet or short WAV reached model loading.");
            }

            bool emptyRejected = false;
            try { await local.TranscribeAsync(audible with { WavData = [] }); }
            catch (InvalidDataException) { emptyRejected = true; }
            Require(emptyRejected, "Empty WAV was accepted.");

            byte[] wrongRate = (byte[])audible.WavData.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(wrongRate.AsSpan(24, 4), 8000);
            bool formatRejected = false;
            try { await local.TranscribeAsync(audible with { WavData = wrongRate }); }
            catch (InvalidDataException) { formatRejected = true; }
            Require(formatRejected, "Unsupported WAV sample rate was accepted.");
            Require(new FileInfo(modelPath).Length == 4 && !File.Exists(modelPath + ".download"),
                "Rejected audio modified the model or started a download.");

            VoiceCaptureResult silence =
                CreateVoiceCapture(
                    amplitude: 0);

            bool silenceRejected = false;

            try
            {
                await local
                    .TranscribeAsync(
                        silence);
            }
            catch (
                NoSpeechDetectedException)
            {
                silenceRejected = true;
            }

            Require(
                silenceRejected,
                "Silent audio reached Whisper instead of being rejected.");

            using var cancelled =
                new CancellationTokenSource();

            cancelled.Cancel();

            bool cancellationObserved =
                false;

            try
            {
                await local
                    .TranscribeAsync(
                        audible,
                        cancelled.Token);
            }
            catch (
                OperationCanceledException)
            {
                cancellationObserved =
                    true;
            }

            Require(
                cancellationObserved,
                "Speech-to-Text ignored cancellation.");

            var invalidCapture =
                new VoiceCaptureResult(
                    [1, 2, 3, 4],
                    TimeSpan.FromSeconds(1),
                    new WaveFormat(
                        16000,
                        16,
                        1));

            bool invalidAudioRejected =
                false;

            try
            {
                await local
                    .TranscribeAsync(
                        invalidCapture);
            }
            catch (
                InvalidDataException)
            {
                invalidAudioRejected =
                    true;
            }

            Require(
                invalidAudioRejected,
                "Invalid WAV data was accepted.");
        }
        finally
        {
            try
            {
                Directory.Delete(
                    tempDirectory,
                    recursive: true);
            }
            catch
            {
            }
        }
    }

    private static VoiceCaptureResult
        CreateVoiceCapture(
            short amplitude,
            int milliseconds = 500)
    {
        var format =
            new WaveFormat(
                16000,
                16,
                1);

        int sampleCount =
            format.SampleRate *
            milliseconds /
            1000;

        byte[] pcm =
            new byte[
                sampleCount *
                sizeof(short)];

        for (int i = 0;
             i < sampleCount;
             i++)
        {
            BinaryPrimitives
                .WriteInt16LittleEndian(
                    pcm.AsSpan(
                        i * sizeof(short),
                        sizeof(short)),
                    amplitude);
        }

        byte[] wav;

        using (
            var stream =
                new MemoryStream())
        {
            using (
                var writer =
                    new WaveFileWriter(
                        stream,
                        format))
            {
                writer.Write(
                    pcm,
                    0,
                    pcm.Length);

                writer.Flush();
            }

            wav =
                stream.ToArray();
        }

        return new VoiceCaptureResult(
            wav,
            TimeSpan.FromMilliseconds(
                milliseconds),
            format);
    }
}
