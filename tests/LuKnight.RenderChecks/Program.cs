using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using LuKnight.Physics;
using LuKnight.Behaviors;
using LuKnight.Services;
using LuKnight.Views;
using LuKnight.Visuals;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo AdvanceMethod = typeof(CharacterModel3DPlayer).GetMethod("Advance", Private)!;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "LuKnight.csproj")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository not found");
        string output = Path.Combine(root, "output", "model3d");
        Directory.CreateDirectory(output);
        CheckRigAndRendering(output);
        CheckLifecycle();
        CheckGrabAndTargetReset();
        CheckWalkingCadence();
        CheckCadence();
        if (args.Contains("--export")) ExportSprites(root);
        Console.WriteLine($"PASS: {_checks} checks (rig scale, transparent bounds, state/mood transitions, cadence, grab, lifecycle).");
    }

    private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private | BindingFlags.Public)!.GetValue(obj)!;
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
    private static Action<double> Step(CharacterModel3DPlayer player) => AdvanceMethod.CreateDelegate<Action<double>>(player);
    private static void Settle(Action<double> step, double seconds = 1) { for (int i = 0; i < seconds * 120; i++) step(1.0 / 120); }
    private static Matrix3D RootMatrix(CharacterModel3DPlayer player) => Get<Model3DGroup>(Get<object>(player, "_root"), "Model").Transform.Value;

    private static void CheckRigAndRendering(string output)
    {
        var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Model3D);
        var player = Get<CharacterModel3DPlayer>(view, "_modelPlayer");
        var step = Step(player);
        var viewport = Get<Viewport3D>(view, "ModelViewport");
        var spriteSource = Get<Image>(view, "SpriteImage").Source;
        var camera = (OrthographicCamera)viewport.Camera;
        var head = (GeometryModel3D)Get<Model3DGroup>(Get<object>(player, "_head"), "Model").Children[0];
        Rect3D headBounds = head.Bounds;
        var pictures = new List<(string Name, BitmapSource Image)>();
        foreach (var state in Enum.GetValues<CharacterState>())
        {
            view.SetState(state);
            view.SetMood(CharacterMood.Neutral);
            Settle(step);
            foreach (int direction in new[] { -1, 1 })
            {
                view.SetFacingDirection(direction);
                Settle(step, .5);
                for (int frame = 0; frame < 8; frame++)
                {
                    Settle(step, .08);
                    var bitmap = Render(view);
                    CheckAlphaBounds(bitmap, $"{state}/{direction}/{frame}");
                }
            }
            var image = Render(view);
            Save(image, Path.Combine(output, state + ".png"));
            pictures.Add((state.ToString(), image));
            Require(view.RenderMode == CharacterRenderMode.Model3D, "All physical states use the model");
            Require(head.IsFrozen && head.Bounds == headBounds, "The head geometry must never resize");
            Require(camera.Width == 2.55 && camera.Position.Y == 1.61, "Camera must remain fixed");
            Matrix3D pose = RootMatrix(player);
            double time = Get<double>(player, "_time");
            view.SetState(state);
            Require(RootMatrix(player) == pose && Get<double>(player, "_time") == time, "Repeated state resets the pose/clock");
        }
        view.SetState(CharacterState.Idle);
        foreach (var mood in Enum.GetValues<CharacterMood>())
        {
            view.SetMood(mood);
            Settle(step, .25);
            CheckAlphaBounds(Render(view), mood.ToString());
            Require(head.Bounds == headBounds, "Mood changes must not substitute a differently sized body");
            Require(Get<CharacterMood>(player, "_mood") == mood, "Mood not forwarded to model");
        }
        view.SetState(CharacterState.Walk); Settle(step);
        var before = RootMatrix(player);
        view.SetState(CharacterState.Grabbed);
        Require(RootMatrix(player) == before, "Grabbing must not instantly snap the rig");
        view.SetAirborneVelocity(1400, -600); Settle(step);
        Require(Math.Abs(Get<AxisAngleRotation3D>(Get<object>(player, "_root"), "Roll").Angle) < 9,
            "Drag sway must stay bounded around the anchor");
        Require(ReferenceEquals(Get<Image>(view, "SpriteImage").Source, spriteSource), "3D path should not decode sprite frames");
        SaveContactSheet(pictures, Path.Combine(output, "preview.png"));
        player.Dispose();
    }

    private static void CheckLifecycle()
    {
        var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Model3D);
        var model = Get<CharacterModel3DPlayer>(view, "_modelPlayer");
        model.Start(); model.Start();
        Require(Get<bool>(model, "_running"), "Model renderer failed to start");
        view.SetRenderMode(CharacterRenderMode.Vector);
        Require(!Get<bool>(model, "_running"), "Inactive model must release rendering subscription");
        Require(Get<Viewport3D>(view, "ModelViewport").Visibility == Visibility.Collapsed, "3D layer must hide in Vector mode");
        view.SetRenderMode(CharacterRenderMode.Sprite);
        view.SetState(CharacterState.Grabbed);
        var clips = Get<Dictionary<CharacterState, SpriteAnimationClip>>(view, "_spriteStateClips");
        Require(clips[CharacterState.Grabbed].Frames.Count == 1, "Sprite fallback drag must use one stable pose");
        view.SetRenderMode(CharacterRenderMode.Model3D);
        Require(Get<Grid>(view, "VectorLayer").Visibility == Visibility.Collapsed &&
            Get<Grid>(view, "SpriteLayer").Visibility == Visibility.Collapsed, "Legacy layers should not render behind 3D");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Require(Get<bool>(model, "_disposed") && !Get<bool>(model, "_running"), "Unload leaks model renderer");
        Require(Get<Viewport3D>(view, "ModelViewport").Children.Count == 0, "Unload leaks scene");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Require(Get<Viewport3D>(view, "ModelViewport").Children.Count == 1, "Reload should recreate exactly one scene");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void CheckGrabAndTargetReset()
    {
        var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Model3D);
        var window = new Window { Width = 190, Height = 240, Content = view };
        new WindowInteropHelper(window).EnsureHandle(); // Hidden test window; no cursor input is synthesized.
        using (var physics = new CharacterPhysicsController(window, view))
        {
            typeof(CharacterPhysicsController).GetField("_targetWindowHandle", Private)!.SetValue(physics, new IntPtr(123));
            Require(physics.BeginGrab(), "Grab should start with a cursor available");
            Require(Get<nint>(physics, "_targetWindowHandle") == nint.Zero && !physics.IsFalling, "Grab must clear autonomous trajectory");
            var player = Get<CharacterModel3DPlayer>(view, "_modelPlayer");
            Step(player)(.016);
            var before = RootMatrix(player);
            physics.UpdateGrab(); physics.UpdateGrab();
            Require(view.CurrentState == CharacterState.Grabbed && RootMatrix(player) == before, "Pointer samples must not restart the animation");
            physics.EndGrab();
            Require(physics.IsFalling && view.CurrentState == CharacterState.Falling, "Release must hand back to falling physics");
        }
        window.Close();
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static RenderingEventArgs RenderAt(double seconds) => (RenderingEventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), Private, null, new object[] { TimeSpan.FromSeconds(seconds) }, null)!;

    private static void CheckWalkingCadence()
    {
        foreach (int hz in new[] { 30, 60, 120, 144 })
        {
            var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Model3D);
            var window = new Window { Width = 190, Height = 240, Content = view };
            new WindowInteropHelper(window).EnsureHandle();
            using (var behavior = new BehaviorController(window, view))
            {
                var workArea = DesktopMonitorService.GetMonitorForWindow(window).WorkArea;
                DesktopMonitorService.SetWindowPosition(window, workArea.Left + 200, workArea.Bottom - 240);
                view.SetState(CharacterState.Walk);
                typeof(BehaviorController).GetField("_walking", Private)!.SetValue(behavior, true);
                typeof(BehaviorController).GetField("_canWalkOnRender", Private)!.SetValue(behavior, true);
                typeof(BehaviorController).GetField("_direction", Private)!.SetValue(behavior, 1);
                var update = typeof(BehaviorController).GetMethod("OnMovementRendering", Private)!;
                update.Invoke(behavior, new object?[] { null, RenderAt(10) });
                var start = DesktopMonitorService.GetWindowBounds(window);
                for (int i = 1; i <= hz; i++)
                    update.Invoke(behavior, new object?[] { null, RenderAt(10 + (double)i / hz) });
                var end = DesktopMonitorService.GetWindowBounds(window);
                double speed = (double)typeof(BehaviorController).GetField("WalkSpeed", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
                Require(Math.Abs(end.Left - start.Left - speed) <= 1.01, $"Walking loses fractional distance at {hz} Hz");
                update.Invoke(behavior, new object?[] { null, RenderAt(11) });
                Require(DesktopMonitorService.GetWindowBounds(window) == end, "Duplicate render callback moves the window twice");
                behavior.Pause(BehaviorPauseReason.UserDrag);
                update.Invoke(behavior, new object?[] { null, RenderAt(11.016) });
                Require(DesktopMonitorService.GetWindowBounds(window) == end, "Autonomous movement must pause during drag");
            }
            window.Close();
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }

    private static void CheckCadence()
    {
        var poses = new List<double>();
        foreach (int hz in new[] { 30, 60, 120, 144 })
        {
            using var player = new CharacterModel3DPlayer(new Viewport3D());
            player.SetState(CharacterState.Walk);
            var step = Step(player);
            for (int i = 0; i < hz * 3; i++) step(1.0 / hz);
            poses.Add(Get<AxisAngleRotation3D>(Get<object>(player, "_leftLeg"), "Pitch").Angle);
        }
        Require(poses.Max() - poses.Min() < 4, "Animation speed differs across display refresh rates");
        using var clockPlayer = new CharacterModel3DPlayer(new Viewport3D());
        var rendering = typeof(CharacterModel3DPlayer).GetMethod("OnRendering", Private)!;
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1) });
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1.016) });
        double time = Get<double>(clockPlayer, "_time");
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1.016) });
        Require(time == Get<double>(clockPlayer, "_time"), "Duplicate render callback advances the animation twice");
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(8) });
        Require(Get<double>(clockPlayer, "_time") - time <= .050001, "A stalled frame must not jump across a whole motion cycle");
        var update = Step(clockPlayer); Settle(update);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 3000; i++) update(1.0 / 60);
        watch.Stop();
        Console.WriteLine($"3D animation CPU: {watch.Elapsed.TotalMilliseconds / 3000:0.000} ms/update (offscreen transform benchmark).");
    }

    private static BitmapSource Render(FrameworkElement view)
    {
        view.Measure(new Size(170, 220)); view.Arrange(new Rect(0, 0, 170, 220)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(510, 660, 288, 288, PixelFormats.Pbgra32);
        bitmap.Render(view); bitmap.Freeze();
        return bitmap;
    }
    private static void CheckAlphaBounds(BitmapSource image, string label)
    {
        byte[] pixels = new byte[510 * 660 * 4]; image.CopyPixels(pixels, 510 * 4, 0);
        int visible = 0;
        for (int y = 0; y < 660; y++)
        for (int x = 0; x < 510; x++)
        {
            byte alpha = pixels[(y * 510 + x) * 4 + 3];
            if (alpha > 0) visible++;
            if ((x == 0 || y == 0 || x == 509 || y == 659) && alpha > 0)
                throw new InvalidOperationException($"Clipped model at canvas border: {label} ({x},{y})");
        }
        Require(visible > 30000, "Model render is empty: " + label);
    }
    private static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(stream);
    }
    private static void SaveContactSheet(List<(string Name, BitmapSource Image)> images, string path)
    {
        int height = (int)Math.Ceiling(images.Count / 4.0) * 290;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 41, 54)), null, new Rect(0, 0, 1020, height));
            for (int i = 0; i < images.Count; i++)
            {
                double x = i % 4 * 255, y = i / 4 * 290;
                dc.DrawText(new FormattedText(images[i].Name, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15, Brushes.White, 1), new Point(x + 20, y + 8));
                dc.DrawImage(images[i].Image, new Rect(x + 25, y + 25, 204, 264));
            }
        }
        var bitmap = new RenderTargetBitmap(1020, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); Save(bitmap, path);
    }

    private static void ExportSprites(string root)
    {
        string assets = Path.Combine(root, "Assets", "Characters", "LuKnight");
        foreach (var state in Enum.GetValues<CharacterState>())
        {
            var viewport = new Viewport3D();
            using var player = new CharacterModel3DPlayer(viewport);
            player.SetState(state);
            var step = Step(player);
            double period = state == CharacterState.Walk ? .625 : state == CharacterState.Grabbed ? 2 * Math.PI / 3
                : state == CharacterState.Climbing ? 2 * Math.PI / 8 : 2 * Math.PI / 2.2;
            // Warm up for whole cycles. Export uses one fixed camera; never trim/fit individual silhouettes.
            for (int i = 0; i < 16 * 8 * 5; i++) step(period / (16 * 8));
            for (int frame = 0; frame < 16; frame++)
            {
                var bitmap = Render(viewport); CheckAlphaBounds(bitmap, $"export/{state}/{frame}");
                Save(bitmap, Path.Combine(assets, state.ToString(), $"{state.ToString().ToLowerInvariant()}_{frame:000}.png"));
                for (int i = 0; i < 8; i++) step(period / (16 * 8));
            }
        }
        var moods = new[] { CharacterMood.Happy, CharacterMood.Wink, CharacterMood.Sad, CharacterMood.Dizzy,
            CharacterMood.Angry, CharacterMood.Surprised, CharacterMood.Determined, CharacterMood.Neutral };
        foreach (var mood in moods)
        {
            var viewport = new Viewport3D();
            using var player = new CharacterModel3DPlayer(viewport);
            var step = Step(player); Settle(step); player.SetMood(mood);
            Settle(step, mood == CharacterMood.Wink ? .11 : .5);
            var bitmap = Render(viewport); CheckAlphaBounds(bitmap, "export/" + mood);
            Save(bitmap, mood == CharacterMood.Neutral ? Path.Combine(assets, "Reference", "luknight_master.png")
                : Path.Combine(assets, "Expressions", mood.ToString().ToLowerInvariant() + ".png"));
        }
        Console.WriteLine("Exported 112 frames + 7 expressions + master from the same 3D rig.");
    }
}
