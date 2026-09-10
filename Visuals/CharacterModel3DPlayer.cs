using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LuKnight.Views;
using static LuKnight.Visuals.Model3DGeometry;

namespace LuKnight.Visuals;

/// <summary>A single articulated model, with fixed geometry and an orthographic camera.</summary>
public sealed class CharacterModel3DPlayer : IDisposable
{
    private sealed class Joint
    {
        internal readonly Model3DGroup Model = new();
        internal readonly AxisAngleRotation3D Pitch = new(new Vector3D(1, 0, 0), 0);
        internal readonly AxisAngleRotation3D Yaw = new(new Vector3D(0, 1, 0), 0);
        internal readonly AxisAngleRotation3D Roll = new(new Vector3D(0, 0, 1), 0);
        internal readonly TranslateTransform3D Offset;

        internal Joint(Model3DGroup parent, double x, double y, double z)
        {
            Offset = new TranslateTransform3D(x, y, z);
            var transforms = new Transform3DGroup();
            transforms.Children.Add(new RotateTransform3D(Pitch));
            transforms.Children.Add(new RotateTransform3D(Yaw));
            transforms.Children.Add(new RotateTransform3D(Roll));
            transforms.Children.Add(Offset);
            Model.Transform = transforms;
            parent.Children.Add(Model);
        }

        internal void Pose(double pitch, double yaw, double roll, double blend)
        {
            Pitch.Angle += (pitch - Pitch.Angle) * blend;
            Yaw.Angle += (yaw - Yaw.Angle) * blend;
            Roll.Angle += (roll - Roll.Angle) * blend;
        }
    }

    private readonly Viewport3D _viewport;
    private readonly ModelVisual3D _visual;
    private readonly Joint _root, _head, _leftArm, _rightArm, _leftLeg, _rightLeg, _leftEar, _rightEar;
    private readonly Joint _leftBrow, _rightBrow;
    private readonly ScaleTransform3D _leftEyeScale = new(), _rightEyeScale = new();
    private readonly ScaleTransform3D _leftLidScale = new(0, 0, 0), _rightLidScale = new(0, 0, 0);
    private readonly TranslateTransform3D _leftPupil = new(), _rightPupil = new();
    private readonly Model3DGroup _mouth = new();
    private readonly Model3DGroup _smile = new(), _openMouth = new(), _frown = new();
    private readonly Material _ink = Material("#152B3C", 85);
    private TimeSpan? _lastRenderTime;
    private double _time, _blinkStarted = -10, _winkStarted = -10, _twitchStarted = -10;
    private double _landingStarted = -10, _landingStrength;
    private double _lookX, _lookY, _lookUntil = double.PositiveInfinity;
    private double _velocityX, _velocityY;
    private int _facing = 1;
    private bool _running, _disposed;
    private CharacterState _state = CharacterState.Idle;
    private CharacterMood _mood = CharacterMood.Neutral;

    public CharacterModel3DPlayer(Viewport3D viewport)
    {
        _viewport = viewport;
        // Projection and model scale never change between states or moods.
        viewport.Camera = new OrthographicCamera(new Point3D(0, 1.61, 8),
            new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 2.55)
            { NearPlaneDistance = .1, FarPlaneDistance = 20 };
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(85, 89, 100)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(155, 146, 140), new Vector3D(-3, -4, -6)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(28, 43, 56), new Vector3D(3, 1, -3)));
        var fur = Material("#F6F3F4", 24);
        var ivory = Material("#FFFCFA", 28);
        var cyan = Material("#57C9DF", 48);
        var pink = Material("#EEA3C0", 32);

        // Root pivot at the head/neck keeps the grab anchor quiet while limbs dangle.
        _root = new Joint(scene, 0, 1.36, 0);
        var body = new Joint(_root.Model, 0, -1.36, 0);
        Ellipsoid(body.Model, fur, 0, .82, 0, .37, .45, .28);
        Ellipsoid(body.Model, fur, -.26, .52, -.22, .19, .19, .18);
        Star(body.Model, cyan, 0, 1.00, .284, .09);

        _leftLeg = new Joint(body.Model, -.19, .49, 0);
        _rightLeg = new Joint(body.Model, .19, .49, 0);
        foreach (var leg in new[] { _leftLeg, _rightLeg })
        {
            Ellipsoid(leg.Model, fur, 0, -.15, 0, .155, .24, .17);
            Ellipsoid(leg.Model, ivory, 0, -.31, .095, .19, .115, .24);
        }
        _leftArm = new Joint(body.Model, -.34, 1.07, 0);
        _rightArm = new Joint(body.Model, .34, 1.07, 0);
        foreach (var arm in new[] { _leftArm, _rightArm })
            Ellipsoid(arm.Model, fur, 0, -.17, .015, .135, .265, .145);

        _head = new Joint(body.Model, 0, 1.34, .025);
        Ellipsoid(_head.Model, fur, 0, .30, 0, .625, .53, .435);
        _leftEar = new Joint(_head.Model, -.30, .73, -.025);
        _rightEar = new Joint(_head.Model, .30, .73, -.025);
        Ear(_leftEar.Model, -1, fur, cyan);
        Ear(_rightEar.Model, 1, fur, cyan);

        BuildEye(-1, cyan, _leftEyeScale, _leftLidScale, _leftPupil);
        BuildEye(1, cyan, _rightEyeScale, _rightLidScale, _rightPupil);
        Ellipsoid(_head.Model, pink, -.40, .075, .343, .082, .039, .017);
        Ellipsoid(_head.Model, pink, .40, .075, .343, .082, .039, .017);
        Ellipsoid(_head.Model, ivory, -.065, .08, .424, .087, .063, .028);
        Ellipsoid(_head.Model, ivory, .065, .08, .424, .087, .063, .028);
        Ellipsoid(_head.Model, pink, 0, .142, .456, .042, .025, .019);

        _head.Model.Children.Add(_mouth);
        for (int i = 0; i < 10; i++)
        {
            double x = -.068 + .136 * i / 10, nextX = -.068 + .136 * (i + 1) / 10;
            Line(_smile, _ink, new Point3D(x, -.013 + 5 * x * x, .432),
                new Point3D(nextX, -.013 + 5 * nextX * nextX, .432), .009);
            Line(_frown, _ink, new Point3D(x, .01 - 5 * x * x, .432),
                new Point3D(nextX, .01 - 5 * nextX * nextX, .432), .009);
        }
        Ellipsoid(_openMouth, _ink, 0, -.004, .431, .053, .062, .019);
        Ellipsoid(_openMouth, pink, 0, -.034, .449, .034, .018, .008);
        _smile.Freeze(); _frown.Freeze(); _openMouth.Freeze();
        _mouth.Children.Add(_smile);
        _leftBrow = BuildBrow(-1);
        _rightBrow = BuildBrow(1);
        _visual = new ModelVisual3D { Content = scene };
        viewport.Children.Add(_visual);
        Advance(0);
    }

    private void BuildEye(int side, Material cyan, ScaleTransform3D blink,
        ScaleTransform3D closedScale, TranslateTransform3D pupil)
    {
        var eye = new Joint(_head.Model, side * .263, .335, .366);
        eye.Yaw.Angle = side * 22;
        var lid = new Model3DGroup { Transform = blink };
        eye.Model.Children.Add(lid);
        Ellipsoid(lid, cyan, 0, 0, 0, .195, .230, .061);
        var iris = new Model3DGroup { Transform = pupil };
        lid.Children.Add(iris);
        Ellipsoid(iris, _ink, 0, 0, .032, .164, .203, .051);
        var highlight = new EmissiveMaterial(Brushes.White); highlight.Freeze();
        Ellipsoid(iris, highlight, -.043, .073, .077, .044, .055, .012);
        Ellipsoid(iris, highlight, .055, -.066, .077, .018, .020, .008);
        Star(iris, cyan, -.045, -.076, .081, .031);
        var closed = new Model3DGroup { Transform = closedScale };
        eye.Model.Children.Add(closed);
        for (int i = 0; i < 8; i++)
        {
            double x = -.14 + .28 * i / 8, next = -.14 + .28 * (i + 1) / 8;
            Line(closed, _ink, new Point3D(x, 1.8 * x * x - .025, .07),
                new Point3D(next, 1.8 * next * next - .025, .07), .014);
        }
    }

    private Joint BuildBrow(int side)
    {
        var brow = new Joint(_head.Model, side * .265, .595, .337);
        Line(brow.Model, _ink, new Point3D(-.076, 0, 0), new Point3D(.076, .013, 0), .014);
        return brow;
    }

    public void Start()
    {
        if (_disposed || _running) return;
        _running = true;
        _lastRenderTime = null;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnRendering;
        _running = false;
        _lastRenderTime = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (_lastRenderTime == time) return; // WPF can raise twice for one frame.
        double delta = _lastRenderTime is { } previous ? (time - previous).TotalSeconds : 0;
        _lastRenderTime = time;
        Advance(Math.Clamp(delta, 0, .05));
    }

    public void SetState(CharacterState state)
    {
        if (_state == state) return;
        _state = state;
        _lookX = _lookY = 0;
        _velocityX = _velocityY = 0;
        UpdateMouth();
        // Do not reset transforms/time: the next rendered pose blends from the current one.
    }

    public void SetMood(CharacterMood mood)
    {
        if (_mood == mood) return;
        _mood = mood;
        if (mood == CharacterMood.Wink) _winkStarted = _time;
        UpdateMouth();
    }

    private void UpdateMouth()
    {
        _mouth.Children.Clear();
        bool sleepy = _state == CharacterState.Sleep;
        _mouth.Children.Add(!sleepy && _mood is CharacterMood.Surprised or CharacterMood.Happy or CharacterMood.Dizzy
            ? _openMouth : !sleepy && _mood is CharacterMood.Sad or CharacterMood.Angry ? _frown : _smile);
    }

    public void SetFacingDirection(int direction) => _facing = direction < 0 ? -1 : 1;
    public void SetVelocity(double x, double y) { _velocityX = x; _velocityY = y; }
    public void Blink() { if (_state != CharacterState.Sleep) _blinkStarted = _time; }
    public void TwitchEars() { if (_state != CharacterState.Sleep) _twitchStarted = _time; }
    public void Look(double x, double y, double duration = double.PositiveInfinity)
    {
        _lookX = Math.Clamp(x, -1, 1);
        _lookY = Math.Clamp(y, -1, 1);
        _lookUntil = _time + duration;
    }
    public void Land(double impact)
    {
        _landingStarted = _time;
        _landingStrength = Math.Clamp(impact / 1000, .15, 1);
    }

    // Internal deterministic stepping also permits pose, lifecycle, and cadence QA without a desktop.
    internal void Advance(double delta)
    {
        _time += delta;
        double blend = delta == 0 ? 1 : 1 - Math.Exp(-delta / .075);
        double breathFrequency = _state == CharacterState.Walk ? Math.PI * 3.2
            : _state == CharacterState.Grabbed ? 3 : _state == CharacterState.Climbing ? 8 : 2.2;
        double breath = Math.Sin(_time * breathFrequency);
        double stride = Math.Sin(_time * Math.PI * 3.2);
        double walk = _state == CharacterState.Walk ? 1 : 0;
        bool sleep = _state == CharacterState.Sleep;
        bool grabbed = _state == CharacterState.Grabbed;
        bool falling = _state == CharacterState.Falling;
        bool hanging = _state == CharacterState.Hanging;
        bool climbing = _state == CharacterState.Climbing;
        if (_time > _lookUntil) _lookX = _lookY = 0;
        double lookX = sleep ? 0 : _lookX, lookY = sleep ? 0 : _lookY;
        double horizontal = Math.Clamp(_velocityX / 1200, -1, 1);

        double roll = grabbed ? horizontal * -7 + Math.Sin(_time * 3) * .8
            : falling ? -horizontal * 9 : walk * stride * 1.2;
        double yaw = _facing * (walk > 0 ? 32 : hanging || climbing ? 46 : 10);
        double rootPitch = sleep ? 8 : falling ? Math.Clamp(_velocityY / 180, -9, 7) : 0;
        _root.Pose(rootPitch, yaw, roll, blend);
        double impactAge = _time - _landingStarted;
        double landing = impactAge < .45 ? -Math.Sin(impactAge / .45 * Math.PI) * .085 * _landingStrength : 0;
        double offset = walk * .021 * Math.Cos(_time * Math.PI * 6.4)
            + (sleep ? -.13 : 0) + landing;
        _root.Offset.OffsetY += (1.36 + offset - _root.Offset.OffsetY) * blend;

        _head.Pose((sleep ? 15 : lookY * 7) + breath * .5, lookX * 9,
            sleep ? -7 : _mood is CharacterMood.Confused or CharacterMood.Thinking ? 7 : 0, blend);
        _head.Offset.OffsetY += (1.34 + breath * (sleep ? .010 : .007) - _head.Offset.OffsetY) * blend;
        double armPitch = falling ? -52 : hanging || climbing ? -150 : sleep ? -18 : 0;
        double armRoll = falling ? 55 : hanging || climbing ? -22 : grabbed ? 7 : sleep ? 22 : 18;
        double climbStride = climbing ? Math.Sin(_time * 8) * 20 : 0;
        _leftArm.Pose(armPitch + walk * stride * 26 + climbStride, 0, -armRoll, blend);
        _rightArm.Pose(armPitch - walk * stride * 26 - climbStride, 0, armRoll, blend);
        double shoulderY = hanging || climbing ? 1.40 : 1.07;
        double shoulderZ = hanging || climbing ? .39 : 0;
        _leftArm.Offset.OffsetY += (shoulderY - _leftArm.Offset.OffsetY) * blend;
        _rightArm.Offset.OffsetY += (shoulderY - _rightArm.Offset.OffsetY) * blend;
        _leftArm.Offset.OffsetZ += (shoulderZ - _leftArm.Offset.OffsetZ) * blend;
        _rightArm.Offset.OffsetZ += (shoulderZ - _rightArm.Offset.OffsetZ) * blend;
        double legPitch = falling ? 33 : hanging || climbing ? 18 : sleep ? -35 : 0;
        _leftLeg.Pose(legPitch - walk * stride * 29 - climbStride, 0, grabbed ? -5 : -3, blend);
        _rightLeg.Pose(legPitch + walk * stride * 29 + climbStride, 0, grabbed ? 5 : 3, blend);
        double twitchAge = _time - _twitchStarted;
        double twitch = twitchAge < .4 ? Math.Sin(twitchAge * Math.PI * 10) * Math.Exp(-twitchAge * 7) * 10 : 0;
        double earLag = falling ? -12 : sleep ? 16 : 0;
        _leftEar.Pose(earLag + stride * walk * 3, 0, breath * 1.2 + twitch, blend);
        _rightEar.Pose(earLag - stride * walk * 3, 0, -breath * 1.2 - twitch * .6, blend);

        double blink = BlinkAmount(_time - _blinkStarted);
        double wink = BlinkAmount(_time - _winkStarted);
        double moodEye = _mood == CharacterMood.Happy ? .84 : _mood is CharacterMood.Angry or CharacterMood.Determined ? .80 : 1;
        double eyeY = sleep ? .055 : moodEye * (1 - .94 * blink);
        _leftEyeScale.ScaleY += (Math.Min(eyeY, 1 - .94 * wink) - _leftEyeScale.ScaleY) * (1 - Math.Exp(-Math.Max(delta, .001) / .018));
        _rightEyeScale.ScaleY += (eyeY - _rightEyeScale.ScaleY) * (1 - Math.Exp(-Math.Max(delta, .001) / .018));
        UpdateLid(_leftEyeScale, _leftLidScale);
        UpdateLid(_rightEyeScale, _rightLidScale);
        double dizzyX = _mood == CharacterMood.Dizzy && !sleep ? Math.Sin(_time * 8) * .016 : 0;
        double dizzyY = _mood == CharacterMood.Dizzy && !sleep ? Math.Cos(_time * 8) * .016 : 0;
        UpdatePupil(_leftPupil, lookX * .018 + dizzyX, -lookY * .018 + dizzyY, blend);
        UpdatePupil(_rightPupil, lookX * .018 + dizzyX, -lookY * .018 + dizzyY, blend);
        double brow = _mood is CharacterMood.Angry or CharacterMood.Determined ? -22
            : _mood == CharacterMood.Sad ? 23 : 0;
        _leftBrow.Pose(0, 0, brow, blend);
        _rightBrow.Pose(0, 0, -brow, blend);
        double browY = _mood is CharacterMood.Surprised or CharacterMood.Curious ? .64 : .595;
        _leftBrow.Offset.OffsetY += (browY - _leftBrow.Offset.OffsetY) * blend;
        _rightBrow.Offset.OffsetY += (browY - _rightBrow.Offset.OffsetY) * blend;
    }

    private static double BlinkAmount(double age) => age is >= 0 and < .22 ? Math.Sin(age / .22 * Math.PI) : 0;

    private static void UpdateLid(ScaleTransform3D eye, ScaleTransform3D lid)
    {
        double closed = eye.ScaleY < .16 ? 1 : 0;
        eye.ScaleX = 1 - closed;
        lid.ScaleX = lid.ScaleY = lid.ScaleZ = closed;
    }

    private static void UpdatePupil(TranslateTransform3D pupil, double x, double y, double blend)
    {
        pupil.OffsetX += (x - pupil.OffsetX) * blend;
        pupil.OffsetY += (y - pupil.OffsetY) * blend;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _viewport.Children.Remove(_visual);
        _disposed = true;
    }
}
