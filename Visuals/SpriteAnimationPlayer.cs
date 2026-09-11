using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LuKnight.Visuals;

/// <summary>Preloaded frames, a display-synchronized clock, and fixed-canvas pose transitions.</summary>
public sealed class SpriteAnimationPlayer : IDisposable
{
    private readonly Image _image;
    private readonly Image? _previous;
    private readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<BitmapSource, byte[]> _pixels = new();
    private WriteableBitmap? _interpolated;
    private byte[] _blendPixels = [];
    private SpriteAnimationClip? _clip;
    private SpritePuppet? _puppet;
    private SpriteFace? _face;
    private double _lookX, _lookY;
    public SpriteFace? Face => _puppet?.Face ?? _face;
    public void Look(double x, double y) { _lookX = x; _lookY = y; Face?.Look(x, y); }
    public void Glance(double x, double y, double seconds) => Face?.Glance(x, y, seconds);
    public void Blink() => Face?.Blink();
    public void Twitch() => Face?.Twitch();
    public void SetExpression(string? path) => _face?.SetExpression(path is null ? null : Load(path));
    private BitmapSource[] _frames = [];
    private TimeSpan? _lastRender;
    private double _elapsed;
    private double _transition;
    private int _frameIndex;
    private bool _subscribed;
    private const double BlendDuration = .10;
    public bool IsPlaying => _clip is not null;
    public event Action? FrameLoadFailed;

    public SpriteAnimationPlayer(Image image, Image? previous = null)
    {
        _image = image;
        _previous = previous;
    }

    public void Play(SpriteAnimationClip clip)
    {
        if (ReferenceEquals(_clip, clip)) return;
        try
        {
            if (clip.Frames.Count == 0) throw new InvalidDataException("Empty sprite clip.");
            // Decode before starting playback: no filesystem work during rendering.
            var frames = clip.Frames.Select(Load).ToArray();
            if (frames.Any(f => f.PixelWidth != 510 || f.PixelHeight != 660))
                throw new InvalidDataException("Sprites must use the shared 510 x 660 canvas.");
            if (_previous is not null)
            {
                _previous.Source = _image.Source;
                _previous.Opacity = _image.Source is null ? 0 : 1;
            }
            _puppet = clip.PuppetMotion is { } motion ? new SpritePuppet(motion) : null;
            _face = clip.StableFace ? new SpriteFace(frames[0], blinkFrame: Load("Assets/Characters/LuKnight/Idle/idle_003.png")) : null;
            Face?.Look(_lookX, _lookY);
            _clip = clip;
            _frames = frames;
            _interpolated = _puppet is null && frames.Length > 1 ? new WriteableBitmap(510, 660, 96, 96, PixelFormats.Pbgra32, null) : null;
            _blendPixels = _puppet is null && frames.Length > 1 ? new byte[510 * 660 * 4] : [];
            foreach (var frame in frames)
            {
                if (_puppet is not null || _pixels.ContainsKey(frame)) continue;
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[510 * 660 * 4];
                converted.CopyPixels(pixels, 510 * 4, 0);
                _pixels[frame] = pixels;
            }
            _elapsed = 0;
            _frameIndex = 0;
            _transition = _image.Source is null || _previous is null ? BlendDuration : 0;
            _image.Source = _puppet is not null ? _puppet.Image : _face is not null ? _face.Image : frames[0];
            _image.Opacity = _transition == 0 ? 0 : 1;
            _lastRender = null;
            if (!_subscribed)
            {
                CompositionTarget.Rendering += OnRendering;
                _subscribed = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Lu-Knight][Sprite] LOAD FAILED: {ex}");

            Stop();

            _image.Source = null;

            FrameLoadFailed?.Invoke();
        }
    }

    private BitmapSource Load(string path)
    {
        path = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path));
        if (_cache.TryGetValue(path, out var cached)) return cached;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        _cache[path] = bitmap;
        return bitmap;
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        if (args is not RenderingEventArgs render || _clip is null) return;
        if (_lastRender == render.RenderingTime) return;
        if (_lastRender is { } last)
            Advance(Math.Clamp((render.RenderingTime - last).TotalSeconds, 0, .05));
        _lastRender = render.RenderingTime;
    }

    private void Advance(double delta)
    {
        if (_clip is null) return;
        _elapsed += delta;
        _transition = Math.Min(BlendDuration, _transition + delta);
        double blend = _transition / BlendDuration;
        blend = blend * blend * (3 - 2 * blend);
        _image.Opacity = blend;
        if (_previous is not null)
        {
            _previous.Opacity = 1 - blend;
            if (blend == 1) _previous.Source = null;
        }
        double frameTime = _elapsed * _clip.FramesPerSecond;
        int absoluteIndex = (int)Math.Floor(frameTime + 1e-8);
        int index = absoluteIndex;
        index = _clip.Loop ? index % _frames.Length : Math.Min(index, _frames.Length - 1);
        _frameIndex = index;
        Face?.Advance(delta);
        if (_face is not null) { _image.Source = _face.Image; return; }
        if (_puppet is not null)
        {
            _puppet.Advance(_elapsed);
            _image.Source = _puppet.Image;
            return;
        }
        if (_interpolated is not null)
        {
            // Interpolate premultiplied RGBA, not two translucent Image layers:
            // overlapping white fur must not dim at every half-frame.
            int next = _clip.Loop ? (index + 1) % _frames.Length : Math.Min(index + 1, _frames.Length - 1);
            int weight = (int)(Math.Clamp(frameTime - absoluteIndex, 0, 1) * 256);
            var from = _pixels[_frames[index]];
            var to = _pixels[_frames[next]];
            for (int i = 0; i < _blendPixels.Length; i++)
                _blendPixels[i] = (byte)((from[i] * (256 - weight) + to[i] * weight + 128) >> 8);
            _interpolated.WritePixels(new Int32Rect(0, 0, 510, 660), _blendPixels, 510 * 4, 0);
            _image.Source = _interpolated;
        }
        else _image.Source = _frames[index];
    }

    public void Stop()
    {
        if (_subscribed) CompositionTarget.Rendering -= OnRendering;
        _subscribed = false;
        _clip = null;
        _puppet = null;
        _face = null;
        _frames = [];
        _interpolated = null;
        _blendPixels = [];
        _lastRender = null;
        _image.Opacity = 1;
        if (_previous is not null) { _previous.Source = null; _previous.Opacity = 0; }
    }

    public void Dispose()
    {
        Stop();
        _cache.Clear();
        _pixels.Clear();
    }
}
