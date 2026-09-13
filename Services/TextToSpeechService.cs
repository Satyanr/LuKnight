using System.Speech.Synthesis;

namespace LuKnight.Services;

public interface ITextToSpeechService : IDisposable
{
    bool IsSpeaking { get; }

    Task SpeakAsync(
        string text,
        CancellationToken cancellationToken = default);

    void Stop();
}

public sealed class WindowsTextToSpeechService : ITextToSpeechService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private SpeechSynthesizer? _active;
    private bool _disposed;

    public bool IsSpeaking
    {
        get
        {
            lock (_sync)
                return _active is not null;
        }
    }

    public async Task SpeakAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);

        SpeechSynthesizer? synthesizer = null;
        EventHandler<SpeakCompletedEventArgs>? completedHandler = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            synthesizer = new SpeechSynthesizer();
            synthesizer.SetOutputToDefaultAudioDevice();

            lock (_sync)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(WindowsTextToSpeechService));

                _active = synthesizer;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Prompt? prompt = null;

            completedHandler = (_, args) =>
            {
                if (prompt is not null && !ReferenceEquals(args.Prompt, prompt))
                    return;

                if (args.Error is not null)
                    completion.TrySetException(args.Error);
                else if (args.Cancelled)
                    completion.TrySetCanceled();
                else
                    completion.TrySetResult(true);
            };

            synthesizer.SpeakCompleted += completedHandler;
            prompt = synthesizer.SpeakAsync(normalized);

            using CancellationTokenRegistration registration =
                cancellationToken.Register(() =>
                {
                    try
                    {
                        synthesizer.SpeakAsyncCancel(prompt);
                    }
                    catch
                    {
                    }
                });

            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            if (synthesizer is not null && completedHandler is not null)
                synthesizer.SpeakCompleted -= completedHandler;

            lock (_sync)
            {
                if (ReferenceEquals(_active, synthesizer))
                    _active = null;
            }

            synthesizer?.Dispose();
            _gate.Release();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_active is null)
                return;

            try
            {
                _active.SpeakAsyncCancelAll();
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        Stop();
    }
}
