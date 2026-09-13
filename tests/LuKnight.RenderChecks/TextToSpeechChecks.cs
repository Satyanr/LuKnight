using LuKnight.Services;

internal static partial class Program
{
    private sealed class BlockingTextToSpeechService : ITextToSpeechService
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSpeaking { get; private set; }
        public int StopCalls { get; private set; }
        public Task Started => _started.Task;
        public IReadOnlyList<string> GetInstalledVoices() => ["Fake Voice"];

        public async Task SpeakAsync(string text, TextToSpeechOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsSpeaking = true;
            _started.TrySetResult(true);
            try
            {
                Task cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                await Task.WhenAny(cancelled, _stopped.Task);
                cancellationToken.ThrowIfCancellationRequested();
                if (_stopped.Task.IsCompleted)
                    throw new OperationCanceledException();
            }
            finally
            {
                IsSpeaking = false;
            }
        }

        public void Stop()
        {
            StopCalls++;
            _stopped.TrySetResult(true);
        }

        public void Dispose() => Stop();
    }

    private sealed class FakeTextToSpeechService : ITextToSpeechService
    {
        public bool IsSpeaking { get; private set; }
        public int SpeakCalls { get; private set; }
        public int StopCalls { get; private set; }
        public List<string> SpokenTexts { get; } = new();
        public TextToSpeechOptions? LastOptions { get; private set; }

        public IReadOnlyList<string> GetInstalledVoices() => ["Fake Indonesian", "Fake English"];

        public Task SpeakAsync(
            string text,
            TextToSpeechOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpeakCalls++;
            SpokenTexts.Add(text);
            LastOptions = options;
            IsSpeaking = true;
            IsSpeaking = false;
            return Task.CompletedTask;
        }

        public void Stop()
        {
            StopCalls++;
            IsSpeaking = false;
        }

        public void Dispose()
        {
            IsSpeaking = false;
        }
    }

    private static async Task CheckTextToSpeechAsync()
    {
        var fake = new FakeTextToSpeechService();
        TextToSpeechOptions options = new("Fake Indonesian", Rate: 2, Volume: 75);
        await fake.SpeakAsync("Halo dari Lu-Knight", options);

        Require(
            fake.SpeakCalls == 1 &&
            fake.SpokenTexts.Count == 1 &&
            fake.SpokenTexts[0] == "Halo dari Lu-Knight",
            "Text-to-Speech service did not receive assistant text.");
        Require(fake.LastOptions == options, "TTS voice options were not preserved.");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool cancellationObserved = false;

        try
        {
            await fake.SpeakAsync("Tidak boleh dibaca.", new TextToSpeechOptions(), cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }

        Require(
            cancellationObserved && fake.SpeakCalls == 1,
            "Text-to-Speech ignored pre-cancellation.");

        fake.Stop();
        Require(
            fake.StopCalls == 1 && !fake.IsSpeaking,
            "Text-to-Speech Stop did not clear speaking state.");

        var local = new WindowsTextToSpeechService();
        await local.SpeakAsync("   ", new TextToSpeechOptions());
        Require(!local.IsSpeaking, "Empty text started local speech output.");

        bool localCancellationObserved = false;
        try
        {
            await local.SpeakAsync("Tidak boleh dibaca.", new TextToSpeechOptions(), cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            localCancellationObserved = true;
        }
        Require(
            localCancellationObserved && !local.IsSpeaking,
            "Windows Text-to-Speech ignored pre-cancellation.");

        var blocking = new BlockingTextToSpeechService();
        using var activeCancellation = new CancellationTokenSource();
        Task speaking = blocking.SpeakAsync("Kalimat yang cukup panjang.", new TextToSpeechOptions(), activeCancellation.Token);
        await blocking.Started;
        Require(blocking.IsSpeaking, "TTS did not enter speaking state.");
        activeCancellation.Cancel();
        bool activeCancelled = false;
        try { await speaking; }
        catch (OperationCanceledException) { activeCancelled = true; }
        Require(activeCancelled && !blocking.IsSpeaking, "Active speech did not stop after cancellation.");

        var stoppable = new BlockingTextToSpeechService();
        Task stoppableSpeech = stoppable.SpeakAsync("Kalimat kedua.", new TextToSpeechOptions());
        await stoppable.Started;
        stoppable.Stop();
        bool stopCancelled = false;
        try { await stoppableSpeech; }
        catch (OperationCanceledException) { stopCancelled = true; }
        Require(stopCancelled && stoppable.StopCalls == 1 && !stoppable.IsSpeaking,
            "TTS Stop did not interrupt active speech.");

        local.Dispose();
        bool disposedRejected = false;
        try
        {
            await local.SpeakAsync("Tidak boleh dibaca.", new TextToSpeechOptions());
        }
        catch (ObjectDisposedException)
        {
            disposedRejected = true;
        }
        Require(disposedRejected, "Disposed Text-to-Speech service accepted speech.");
    }
}
