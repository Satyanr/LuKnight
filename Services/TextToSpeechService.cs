using System.Speech.Synthesis;
using System.Linq;

namespace LuKnight.Services;

public sealed record TextToSpeechOptions(
    string VoiceName = "",
    int Rate = 0,
    int Volume = 100);

public interface ITextToSpeechService : IDisposable
{
    bool IsSpeaking { get; }

    IReadOnlyList<string> GetInstalledVoices();

    Task SpeakAsync(
        string text,
        TextToSpeechOptions options,
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

    public IReadOnlyList<string> GetInstalledVoices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            using var synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                .Where(x => x.Enabled)
                .Select(x => x.VoiceInfo.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task SpeakAsync(
        string text,
        TextToSpeechOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Rate is < -10 or > 10)
            throw new ArgumentOutOfRangeException(nameof(options.Rate));
        if (options.Volume is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options.Volume));

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
            synthesizer.Rate = options.Rate;
            synthesizer.Volume = options.Volume;
            if (!string.IsNullOrWhiteSpace(options.VoiceName))
            {
                string? installedVoice = synthesizer.GetInstalledVoices()
                    .Where(x => x.Enabled)
                    .Select(x => x.VoiceInfo.Name)
                    .FirstOrDefault(x => string.Equals(x, options.VoiceName, StringComparison.Ordinal));
                if (installedVoice is not null)
                    synthesizer.SelectVoice(installedVoice);
            }
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
