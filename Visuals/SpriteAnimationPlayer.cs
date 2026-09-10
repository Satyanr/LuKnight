using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LuKnight.Visuals;

public sealed class SpriteAnimationPlayer :
    IDisposable
{
    private readonly Image _image;

    private readonly DispatcherTimer _timer;


    private SpriteAnimationClip? _clip;

    private int _frameIndex;
    private readonly Dictionary<string, (long Length, DateTime Modified, BitmapImage Bitmap)>
        _frameCache = new(StringComparer.OrdinalIgnoreCase);

    public event Action? FrameLoadFailed;


    public SpriteAnimationPlayer(
        Image image)
    {
        _image = image;


        _timer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        1000.0 / 12.0)
            };


        _timer.Tick +=
            Timer_Tick;
    }


    public void Play(
        SpriteAnimationClip clip)
    {
        if (ReferenceEquals(_clip, clip))
        {
            return;
        }


        _clip = clip;

        _frameIndex = 0;


        _timer.Interval =
            TimeSpan.FromSeconds(
                1.0 /
                clip.FramesPerSecond);


        ShowCurrentFrame();

        // Fallback dapat memanggil Stop() saat frame pertama gagal dimuat.
        if (!ReferenceEquals(_clip, clip))
            return;

        if (clip.Frames.Count > 1)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }


    public void Stop()
    {
        _timer.Stop();

        _clip = null;

        _frameIndex = 0;
    }


    private void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        if (_clip is null ||
            _clip.Frames.Count == 0)
        {
            return;
        }


        _frameIndex++;


        if (_frameIndex >=
            _clip.Frames.Count)
        {
            if (_clip.Loop)
            {
                _frameIndex = 0;
            }
            else
            {
                _frameIndex =
                    _clip.Frames.Count - 1;

                _timer.Stop();
            }
        }


        ShowCurrentFrame();
    }


    private void ShowCurrentFrame()
    {
        if (_clip is null)
            return;

        if (_clip.Frames.Count == 0)
        {
            FailFrameLoad();
            return;
        }

        try
        {
            string resolvedPath = _clip.Frames[_frameIndex];
            if (!Path.IsPathRooted(resolvedPath))
            {
                resolvedPath = Path.Combine(AppContext.BaseDirectory, resolvedPath);
            }

            if (!File.Exists(resolvedPath))
            {
                FailFrameLoad();
                return;
            }

            var info = new FileInfo(resolvedPath);
            if (_frameCache.TryGetValue(resolvedPath, out var cached) &&
                cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc)
            {
                _image.Source = cached.Bitmap;
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(resolvedPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _frameCache[resolvedPath] = (info.Length, info.LastWriteTimeUtc, bitmap);
            _image.Source = bitmap;
        }
        catch
        {
            FailFrameLoad();
        }
    }

    private void FailFrameLoad()
    {
        _image.Source = null;
        Stop();
        FrameLoadFailed?.Invoke();
    }


    public void Dispose()
    {
        Stop();
        _frameCache.Clear();

        _timer.Tick -=
            Timer_Tick;
    }
}
