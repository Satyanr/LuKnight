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

        public bool IsModelReady(SpeechModel model) => true;

        public bool DeleteModel(SpeechModel model) => true;

        public int Calls { get; private set; }

        public Task<SpeechTranscriptionResult>
            TranscribeAsync(
                VoiceCaptureResult capture,
                SpeechToTextOptions options,
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
        Require(
            SpeechToTextCatalog.GetLanguageCode(SpeechLanguage.Automatic) == "auto" &&
            SpeechToTextCatalog.GetLanguageCode(SpeechLanguage.Indonesia) == "id" &&
            SpeechToTextCatalog.GetLanguageCode(SpeechLanguage.English) == "en",
            "Speech language mapping is incorrect.");

        Require(
            SpeechToTextCatalog.GetFileName(SpeechModel.Tiny) == "ggml-tiny.bin" &&
            SpeechToTextCatalog.GetFileName(SpeechModel.Base) == "ggml-base.bin",
            "Speech model file mapping is incorrect.");

        ChatSettings defaults = new();
        Require(
            defaults.VoiceLanguage == SpeechLanguage.Automatic &&
            defaults.VoiceModel == SpeechModel.Base,
            "Voice settings defaults changed unexpectedly.");

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
                    audible,
                    new SpeechToTextOptions());

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
            string settingsPath = Path.Combine(tempDirectory, "settings.json");
            var savedSettings = new SettingsService(settingsPath);
            savedSettings.Update(savedSettings.Current with
            {
                Chat = savedSettings.Current.Chat with
                {
                    VoiceLanguage = SpeechLanguage.Indonesia,
                    VoiceModel = SpeechModel.Tiny
                }
            });
            savedSettings.Save();
            var loadedSettings = new SettingsService(settingsPath);
            loadedSettings.Load();
            Require(
                loadedSettings.Current.Chat.VoiceLanguage == SpeechLanguage.Indonesia &&
                loadedSettings.Current.Chat.VoiceModel == SpeechModel.Tiny,
                "Voice language and model settings did not persist.");

            bool invalidVoiceSettingRejected = false;
            try
            {
                SettingsService.Validate(new AppSettings
                {
                    Chat = new ChatSettings { VoiceModel = (SpeechModel)99 }
                });
            }
            catch (ArgumentException)
            {
                invalidVoiceSettingRejected = true;
            }
            Require(invalidVoiceSettingRejected, "Invalid voice model setting was accepted.");

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
                    tempDirectory);

            Require(
                !local.IsModelReady(SpeechModel.Base),
                "Corrupt Whisper model was treated as ready.");

            await File.WriteAllBytesAsync(modelPath, []);
            Require(!local.IsModelReady(SpeechModel.Base), "Empty model was treated as ready.");
            await File.WriteAllBytesAsync(modelPath, [0x6c, 0x6d, 0x67, 0x67]);
            Require(!local.IsModelReady(SpeechModel.Base), "Truncated model was treated as ready.");

            foreach (var quiet in new[]
            {
                CreateVoiceCapture(10),
                CreateVoiceCapture(2500, 100),
                CreateVoiceCapture(2500, 100) with { Duration = TimeSpan.FromSeconds(1) },
                CreateVoiceCapture(0, 0) with { Duration = TimeSpan.FromSeconds(1) }
            })
            {
                bool rejected = false;
                try { await local.TranscribeAsync(quiet, new SpeechToTextOptions()); }
                catch (NoSpeechDetectedException) { rejected = true; }
                Require(rejected, "Quiet or short WAV reached model loading.");
            }

            bool emptyRejected = false;
            try { await local.TranscribeAsync(audible with { WavData = [] }, new SpeechToTextOptions()); }
            catch (InvalidDataException) { emptyRejected = true; }
            Require(emptyRejected, "Empty WAV was accepted.");

            byte[] wrongRate = (byte[])audible.WavData.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(wrongRate.AsSpan(24, 4), 8000);
            bool formatRejected = false;
            try { await local.TranscribeAsync(audible with { WavData = wrongRate }, new SpeechToTextOptions()); }
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
                        silence,
                        new SpeechToTextOptions());
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
                        new SpeechToTextOptions(),
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
                        invalidCapture,
                        new SpeechToTextOptions());
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

            string tinyPath = Path.Combine(tempDirectory, "ggml-tiny.bin");
            await using (FileStream tiny = new(tinyPath, FileMode.Create, FileAccess.Write))
            {
                tiny.SetLength(2 * 1024 * 1024);
                tiny.Position = 0;
                await tiny.WriteAsync(new byte[] { 0x6c, 0x6d, 0x67, 0x67 });
            }
            await using (FileStream baseModel = new(modelPath, FileMode.Create, FileAccess.Write))
            {
                baseModel.SetLength(2 * 1024 * 1024);
                baseModel.Position = 0;
                await baseModel.WriteAsync(new byte[] { 0x6c, 0x6d, 0x67, 0x67 });
            }
            Require(
                local.IsModelReady(SpeechModel.Tiny) && local.IsModelReady(SpeechModel.Base),
                "Valid local model fixtures were not recognized.");
            Require(
                local.DeleteModel(SpeechModel.Tiny) &&
                !File.Exists(tinyPath) &&
                File.Exists(modelPath),
                "Deleting Tiny removed the wrong selected model.");
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
