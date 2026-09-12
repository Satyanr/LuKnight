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
                    _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                }
            };
            input.RecordingStopped += (_, e) => CompleteRecording(e.Exception);

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

    private void CompleteRecording(Exception? error)
    {
        TaskCompletionSource<VoiceCaptureResult>? completion;
        VoiceCaptureResult? result = null;

        lock (_sync)
        {
            completion = _completion;
            if (completion is null)
            {
                CleanupUnsafe();
                return;
            }

            TimeSpan duration = _timer?.Elapsed ?? TimeSpan.Zero;
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
        if (disposeWriter)
            _writer?.Dispose();

        _writer = null;
        _input?.Dispose();
        _input = null;
        _stream?.Dispose();
        _stream = null;
        _timer = null;
        _completion = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
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
