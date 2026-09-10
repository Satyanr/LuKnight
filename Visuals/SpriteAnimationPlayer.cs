using System;
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
        if (_clip?.Name ==
            clip.Name)
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
        if (_clip is null ||
            _clip.Frames.Count == 0)
        {
            return;
        }


        string path =
            _clip.Frames[
                _frameIndex];


        try
        {
            var bitmap =
                new BitmapImage();


            bitmap.BeginInit();

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.UriSource =
                new Uri(
                    path,
                    UriKind.RelativeOrAbsolute);

            bitmap.EndInit();

            bitmap.Freeze();


            _image.Source =
                bitmap;
        }
        catch
        {
            // Sprite asset belum tersedia /
            // invalid.
            // Vector renderer tetap menjadi
            // fallback.
        }
    }


    public void Dispose()
    {
        _timer.Stop();

        _timer.Tick -=
            Timer_Tick;
    }
}