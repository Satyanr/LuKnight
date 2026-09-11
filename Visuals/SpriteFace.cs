using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LuKnight.Visuals;

/// <summary>Local facial deformation over one registered sprite; the body never changes frames.</summary>
public sealed class SpriteFace
{
    private readonly int _width, _height;
    private readonly byte[] _base, _skin, _output, _closed;
    private readonly bool _profile;
    private BitmapSource? _expression;
    private double _targetX, _targetY, _lookX, _lookY;
    private double _blinkTime = 1, _twitchTime = 1;
    private bool _dirty = true;
    private double _glanceRemaining;
    public WriteableBitmap Image { get; }
    public double LookX => _lookX;
    public double LookY => _lookY;
    public bool IsBlinking => _blinkTime < .2;

    public SpriteFace(BitmapSource source, bool profile = false, BitmapSource? blinkFrame = null)
    {
        _profile = profile; _width = source.PixelWidth; _height = source.PixelHeight;
        _base = new byte[_width * _height * 4];
        new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0).CopyPixels(_base, _width * 4, 0);
        _skin = (byte[])_base.Clone(); _output = (byte[])_base.Clone(); _closed = (byte[])_base.Clone();
        if (blinkFrame is not null)
            new FormatConvertedBitmap(blinkFrame, PixelFormats.Pbgra32, null, 0).CopyPixels(_closed, _width * 4, 0);
        if (_profile)
        {
            // A local eyelid closes over the side eye; no resampling across the muzzle/head outline.
            for (int y = 163; y < 302; y++) for (int x = 379; x < 490; x++)
            {
                int i = (y * _width + x) * 4;
                double t = Math.Clamp((y - 170) / 125.0, 0, 1);
                for (int c = 0; c < 3; c++) _closed[i + c] = (byte)(_base[(173 * _width + 431) * 4 + c] * (1 - t) + _base[(238 * _width + 381) * 4 + c] * t);
                double u = (x - 433) / 31.0;
                double lineY = 240 + 8 * (1 - u * u);
                double ink = Math.Abs(u) <= 1 ? Math.Clamp(2.4 - Math.Abs(y - lineY), 0, 1) : 0;
                for (int c = 0; c < 3; c++) _closed[i + c] = (byte)(_closed[i + c] * (1 - ink) + 35 * ink);
            }
        }
        Image = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32, null);
        Image.WritePixels(new Int32Rect(0, 0, _width, _height), _output, _width * 4, 0);
    }

    public void Look(double x, double y) { _glanceRemaining = 0; _targetX = Math.Clamp(x, -1, 1); _targetY = Math.Clamp(y, -1, 1); }
    public void Glance(double x, double y, double seconds) { Look(x, y); _glanceRemaining = seconds; }
    public void Blink() { if (!IsBlinking) _blinkTime = 0; }
    public void Twitch() { if (_twitchTime >= .45) _twitchTime = 0; }

    public void SetExpression(BitmapSource? source)
    {
        if (_profile || ReferenceEquals(_expression, source)) return;
        _expression = source; Array.Copy(_base, _skin, _base.Length);
        if (source is not null)
        {
            var pixels = new byte[_base.Length];
            new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0).CopyPixels(pixels, _width * 4, 0);
            // Feather only the facial interior. Ears, head outline, torso and feet stay exact.
            for (int y = 320; y < 490; y++) for (int x = 125; x < 385; x++)
            {
                double r = Math.Sqrt(Math.Pow((x - 255) / 128.0, 2) + Math.Pow((y - 405) / 85.0, 2));
                double weight = Math.Clamp((1 - r) / .22, 0, 1);
                weight = weight * weight * (3 - 2 * weight);
                int i = (y * _width + x) * 4;
                for (int c = 0; c < 4; c++) _skin[i + c] = (byte)Math.Round(_base[i + c] * (1 - weight) + pixels[i + c] * weight);
            }
        }
        _dirty = true;
    }

    public void Advance(double delta)
    {
        bool timedGlance = _glanceRemaining > 0;
        if (timedGlance && (_glanceRemaining -= delta) <= 0) { _targetX = 0; _targetY = 0; }
        bool moving = Math.Abs(_lookX - _targetX) > .0005 || Math.Abs(_lookY - _targetY) > .0005;
        if (!_dirty && !moving && !timedGlance && _blinkTime >= .25 && _twitchTime >= .5) return;
        double smooth = 1 - Math.Exp(-delta * 16);
        _lookX += (_targetX - _lookX) * smooth; _lookY += (_targetY - _lookY) * smooth;
        _blinkTime += delta; _twitchTime += delta;
        double blink = _blinkTime < .2 ? Math.Sin(Math.PI * _blinkTime / .2) : 0;
        double twitch = _twitchTime < .45 ? Math.Sin(_twitchTime * Math.PI * 6 / .45) * Math.Sin(Math.PI * _twitchTime / .45) : 0;
        Array.Copy(_skin, _output, _skin.Length);
        if (_profile)
        {
            Warp(433, 232, 52, 64, _lookX * 10, _lookY * 7, preserveSilhouette: true);
            BlendLid(433, 232, 52, 64, blink);
            if (twitch != 0) Warp(158, 94, 148, 88, twitch * 5, twitch * 4);
        }
        else
        {
            Warp(197, 384, 52, 52, _lookX * 10, _lookY * 7, preserveSilhouette: true);
            BlendLid(197, 384, 52, 52, blink);
            Warp(318, 410, 50, 49, _lookX * 10, _lookY * 7, preserveSilhouette: true);
            BlendLid(318, 410, 50, 49, blink);
            if (twitch != 0)
            {
                Warp(148, 235, 87, 76, twitch * 5, twitch * 3);
                Warp(369, 265, 77, 78, -twitch * 3, twitch * 4);
            }
        }
        Image.WritePixels(new Int32Rect(0, 0, _width, _height), _output, _width * 4, 0);
        _dirty = false;
    }

    private void BlendLid(double cx, double cy, double rx, double ry, double amount)
    {
        if (amount <= 0) return;
        for (int y = (int)(cy - ry); y < cy + ry; y++)
        for (int x = (int)(cx - rx); x < cx + rx; x++)
        {
            double r = Math.Sqrt(Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2));
            double w = Math.Clamp((1 - r) / .18, 0, 1) * amount;
            int i = (y * _width + x) * 4;
            for (int c = 0; c < 3; c++) _output[i + c] = (byte)Math.Min(_output[i + 3], Math.Round(_output[i + c] * (1 - w) + _closed[i + c] * w));
        }
    }

    private void Warp(double cx, double cy, double rx, double ry, double dx, double dy, bool preserveSilhouette = false)
    {
        for (int y = Math.Max(0, (int)(cy - ry)); y < Math.Min(_height, cy + ry); y++)
        for (int x = Math.Max(0, (int)(cx - rx)); x < Math.Min(_width, cx + rx); x++)
        {
            double r = Math.Sqrt(Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2));
            if (r >= 1) continue;
            double weight = Math.Clamp((1 - r) / .32, 0, 1);
            weight = weight * weight * (3 - 2 * weight);
            double sx = x - dx * weight, sy = y - dy * weight;
            sx = Math.Clamp(sx, 0, _width - 1.001); sy = Math.Clamp(sy, 0, _height - 1.001);
            int ix = (int)sx, iy = (int)sy, dst = (y * _width + x) * 4;
            double fx = sx - ix, fy = sy - iy;
            double Sample(int c)
            {
                double top = _skin[(iy * _width + ix) * 4 + c] * (1 - fx) + _skin[(iy * _width + ix + 1) * 4 + c] * fx;
                double bottom = _skin[((iy + 1) * _width + ix) * 4 + c] * (1 - fx) + _skin[((iy + 1) * _width + ix + 1) * 4 + c] * fx;
                return top * (1 - fy) + bottom * fy;
            }
            double alpha = Sample(3);
            // Eye movement stays inside the original head. Never pull background into fur.
            if (preserveSilhouette && alpha < 1) continue;
            double scale = preserveSilhouette ? _skin[dst + 3] / alpha : 1;
            for (int c = 0; c < 3; c++) _output[dst + c] = (byte)Math.Round(Sample(c) * scale);
            if (!preserveSilhouette) _output[dst + 3] = (byte)Math.Round(alpha);
        }
    }
}
