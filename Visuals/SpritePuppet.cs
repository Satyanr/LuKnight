using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuKnight.Services;

namespace LuKnight.Visuals;

public enum SpritePuppetMotion { Walk, Climb }

/// <summary>A cutout sprite rig. All parts are PNG artwork; there is no 3D scene.</summary>
public sealed class SpritePuppet
{
    private sealed class Limb
    {
        public readonly RotateTransform Rotation;
        public readonly ScaleTransform Stretch;
        private readonly TransformGroup _transforms;
        private readonly List<Point> _outline = new();
        public Limb(DrawingGroup scene, ImageSource image, Rect bounds, Point joint)
        {
            Stretch = new ScaleTransform(1, 1, joint.X, joint.Y);
            Rotation = new RotateTransform(0, joint.X, joint.Y);
            var transforms = new TransformGroup(); transforms.Children.Add(Stretch); transforms.Children.Add(Rotation);
            _transforms = transforms;
            var group = new DrawingGroup { Transform = transforms };
            group.Children.Add(new ImageDrawing(image, bounds)); scene.Children.Add(group);
            if (image is BitmapSource bitmap)
            {
                int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
                byte[] pixels = new byte[w * h * 4];
                new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0).CopyPixels(pixels, w * 4, 0);
                // Row endpoints follow the actual fur/sole outline, not transparent PNG corners.
                for (int y = 0; y < h; y++)
                {
                    int left = w, right = -1;
                    for (int x = 0; x < w; x++) if (pixels[(y * w + x) * 4 + 3] > 128) { left = Math.Min(left, x); right = x; }
                    if (right < 0) continue;
                    _outline.Add(new Point(bounds.Left + bounds.Width * left / w, bounds.Top + bounds.Height * (y + 1) / h));
                    _outline.Add(new Point(bounds.Left + bounds.Width * (right + 1) / w, bounds.Top + bounds.Height * (y + 1) / h));
                }
            }
        }
        public double Bottom => _outline.Max(p => _transforms.Transform(p).Y);
    }
    private readonly DrawingGroup _scene = new();
    private readonly TranslateTransform _bounce = new();
    private readonly Limb _nearLeg, _farLeg, _nearArm, _farArm;
    private readonly SpritePuppetMotion _motion;
    public DrawingImage Image { get; }
    public SpriteFace Face { get; }
    public double NearLegAngle => _nearLeg.Rotation.Angle;
    public double FarLegAngle => _farLeg.Rotation.Angle;
    public double NearArmAngle => _nearArm.Rotation.Angle;
    public double FarArmAngle => _farArm.Rotation.Angle;

    public SpritePuppet(SpritePuppetMotion motion)
    {
        _motion = motion;
        var root = new DrawingGroup();
        // Fixed transparent bounds prevent WPF fitting each new joint pose to Image.
        root.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 510, 660))));
        root.Children.Add(_scene);
        _scene.Transform = _bounce;
        _farArm = new Limb(_scene, Load("far-arm"), new Rect(267, 435, 61, 76), new Point(286, 435));
        _farLeg = new Limb(_scene, Load("far-leg"), new Rect(262, 537, 72, 98), new Point(285, 537));
        _nearLeg = new Limb(_scene, Load("near-leg"), new Rect(245, 537, 72, 98), new Point(265, 537));
        Face = new SpriteFace(Load("body"), profile: true);
        _scene.Children.Add(new ImageDrawing(Face.Image, new Rect(9, 150, 406, 398)));
        _nearArm = new Limb(_scene, Load("near-arm"), new Rect(247, 435, 61, 76), new Point(266, 435));
        Image = new DrawingImage(root);
        Advance(0);
    }

    private static BitmapImage Load(string name)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets/Characters/LuKnight/Rig", name + ".png"));
        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    public void Advance(double elapsed)
    {
        double phase = elapsed * 2 * Math.PI / (_motion == SpritePuppetMotion.Walk ? .8 : 1.1);
        double stride = Math.Sin(phase);
        if (_motion == SpritePuppetMotion.Walk)
        {
            _nearLeg.Rotation.Angle = -27 * stride;
            _farLeg.Rotation.Angle = 27 * stride;
            _nearArm.Rotation.Angle = 22 * stride;
            _farArm.Rotation.Angle = -22 * stride;
            // The swing leg lifts, the opposite stance leg stays extended.
            _nearLeg.Stretch.ScaleY = 1 - .14 * Math.Max(0, Math.Cos(phase));
            _farLeg.Stretch.ScaleY = 1 - .14 * Math.Max(0, -Math.Cos(phase));
            // Keep the planted sole on the shared ground line throughout the stride.
            _bounce.Y = CharacterGrounding.SpriteSoleY - Math.Max(_nearLeg.Bottom, _farLeg.Bottom);
        }
        else
        {
            _nearArm.Rotation.Angle = -110 - 28 * stride;
            _farArm.Rotation.Angle = -110 + 28 * stride;
            _nearLeg.Rotation.Angle = -20 + 25 * stride;
            _farLeg.Rotation.Angle = -20 - 25 * stride;
            _nearLeg.Stretch.ScaleY = .88 + .12 * stride;
            _farLeg.Stretch.ScaleY = .88 - .12 * stride;
            _bounce.Y = -3 * (1 - Math.Cos(phase * 2));
        }
    }
}
