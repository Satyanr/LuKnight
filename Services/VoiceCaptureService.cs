using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace LuKnight.Services;

public sealed record VoiceCaptureResult(
    byte[] WavData,
    TimeSpan Duration,
    WaveFormat Format);

public interface IVoiceCaptureService : IDisposable
{
    bool IsRecording { get; }
    void Start();
    Task<VoiceCaptureResult> StopAsync(CancellationToken cancellationToken = default);
}

public sealed class VoiceCaptureService : IVoiceCaptureService
{
    private readonly object _sync = new();
    private WaveIn? _input;
    private WaveFileWriter? _writer;
    private MemoryStream? _stream;
    private TaskCompletionSource<VoiceCaptureResult>? _completion;
    private Stopwatch? _timer;
    private bool _disposed;

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _input is not null;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_input is not null)
                throw new InvalidOperationException("Voice recording sudah aktif.");

            var stream = new MemoryStream();
            var input = new WaveIn
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
                NumberOfBuffers = 3
            };
            var writer = new WaveFileWriter(stream, input.WaveFormat);
            var completion = new TaskCompletionSource<VoiceCaptureResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            input.DataAvailable += (_, e) =>
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_input, input))
                        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                }
            };
            input.RecordingStopped += (_, e) => CompleteRecording(input, e.Exception);

            _stream = stream;
            _writer = writer;
            _input = input;
            _completion = completion;
            _timer = Stopwatch.StartNew();

            try
            {
                input.StartRecording();
            }
            catch
            {
                CleanupUnsafe();
                throw;
            }
        }
    }

    public async Task<VoiceCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Task<VoiceCaptureResult> task;
        WaveIn input;

        lock (_sync)
        {
            if (_input is null || _completion is null)
                throw new InvalidOperationException("Voice recording belum aktif.");

            input = _input;
            task = _completion.Task;
        }

        input.StopRecording();
        return await task.WaitAsync(cancellationToken);
    }

    private void CompleteRecording(WaveIn input, Exception? error)
    {
        TaskCompletionSource<VoiceCaptureResult>? completion;
        VoiceCaptureResult? result = null;

        lock (_sync)
        {
            // WinMM may deliver a queued event after disposal or a new Start.
            if (!ReferenceEquals(_input, input)) return;
            completion = _completion;
            if (completion is null)
            {
                CleanupUnsafe();
                return;
            }

            TimeSpan duration = _timer?.Elapsed ?? TimeSpan.Zero;
            try
            {
                try
                {
                    _writer?.Dispose();
                    _writer = null;
                    if (error is null && _stream is not null && _input is not null)
                    {
                        result = new VoiceCaptureResult(
                            _stream.ToArray(),
                            duration,
                            _input.WaveFormat);
                    }
                }
                finally
                {
                    CleanupUnsafe(disposeWriter: false);
                }
            }
            catch (Exception ex)
            {
                error ??= ex;
            }
        }

        if (error is not null)
        {
            completion.TrySetException(error);
            return;
        }

        if (result is null)
        {
            completion.TrySetException(new InvalidOperationException(
                "Rekaman suara tidak dapat diselesaikan."));
            return;
        }

        completion.TrySetResult(result);
    }

    private void CleanupUnsafe(bool disposeWriter = true)
    {
        WaveFileWriter? writer = _writer;
        WaveIn? input = _input;
        MemoryStream? stream = _stream;
        _writer = null;
        _input = null;
        _stream = null;
        _timer = null;
        _completion = null;
        // Clear session ownership before disposing native resources, since
        // disposal can cause reentrant or queued RecordingStopped callbacks.
        try { if (disposeWriter) writer?.Dispose(); }
        finally
        {
            try { input?.Dispose(); }
            finally { stream?.Dispose(); }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _completion?.TrySetCanceled();
            try
            {
                _input?.StopRecording();
            }
            catch
            {
            }

            CleanupUnsafe();
        }
    }
}
