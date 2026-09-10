using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuKnight.Physics;
using LuKnight.Behaviors;
using LuKnight.Services;
using LuKnight.Views;
using LuKnight.Visuals;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo AdvanceMethod = typeof(SpriteAnimationPlayer).GetMethod("Advance", Private)!;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "LuKnight.csproj")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository not found");
        string output = Path.Combine(root, "output", "sprites");
        Directory.CreateDirectory(output);
        CheckRigAndRendering(output);
        CheckLifecycle();
        if (args.Contains("--desktop"))
        {
            CheckGrabAndTargetReset();
            CheckWalkingCadence();
        }
        else Console.WriteLine("SKIP: native cursor/window checks (run with --desktop in an interactive Windows session).");
        CheckCadence();
        CheckHeadRegistration();
        CheckPuppet(output);
        Console.WriteLine($"PASS: {_checks} checks (sprite scale, transparent bounds, state/mood transitions, cadence, lifecycle).");
    }

    private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private | BindingFlags.Public)!.GetValue(obj)!;
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
    private static Action<double> Step(SpriteAnimationPlayer player) => AdvanceMethod.CreateDelegate<Action<double>>(player);
    private static void Settle(Action<double> step, double seconds = 1) { for (int i = 0; i < seconds * 120; i++) step(1.0 / 120); }
    private static void CheckRigAndRendering(string output)
    {
        var view = new CharacterView();
        Require(view.RenderMode == CharacterRenderMode.Sprite, "Default must be Sprite");
        var images = new List<(string Name, BitmapSource Image)>();
        foreach (var state in Enum.GetValues<CharacterState>())
        {
            view.SetState(state);
            var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
            Settle(Step(player));
            Require(view.RenderMode == CharacterRenderMode.Sprite, "State fell back to vector: " + state);
            var source = Get<Image>(view, "SpriteImage").Source;
            foreach (var mood in Enum.GetValues<CharacterMood>())
            {
                view.SetMood(mood); Settle(Step(player));
                if (state != CharacterState.Idle)
                    Require(ReferenceEquals(source, Get<Image>(view, "SpriteImage").Source) || state == CharacterState.Walk,
                        "Mood replaced a physical pose");
            }
            view.SetMood(CharacterMood.Neutral); Settle(Step(player));
            foreach (int facing in new[] { -1, 1 })
            {
                view.SetFacingDirection(facing);
                var bitmap = Render(view);
                CheckAlphaBounds(Render(Get<Grid>(view, "SpriteImpactRoot")), state + "/" + facing);
                if (facing == 1) images.Add((state.ToString(), bitmap));
            }
            double elapsed = Get<double>(player, "_elapsed");
            view.SetState(state);
            Require(Get<double>(player, "_elapsed") == elapsed, "Repeated state restarted playback");
        }
        SaveContactSheet(images, Path.Combine(output, "preview.png"));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void CheckLifecycle()
    {
        var view = new CharacterView();
        var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
        view.SetRenderMode(CharacterRenderMode.Vector);
        Require(!player.IsPlaying && !Get<bool>(player, "_subscribed"), "Inactive sprite clock must stop");
        view.SetRenderMode(CharacterRenderMode.Sprite);
        Require(player.IsPlaying, "Sprite mode should resume");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Require(!Get<bool>(player, "_subscribed"), "Unload leaks render subscription");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Require(Get<SpriteAnimationPlayer>(view, "_spritePlayer").IsPlaying, "Reload must recreate playback");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void CheckGrabAndTargetReset()
    {
        var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Sprite);
        var window = new Window { Width = 190, Height = 240, Content = view };
        new WindowInteropHelper(window).EnsureHandle(); // Hidden test window; no cursor input is synthesized.
        using (var physics = new CharacterPhysicsController(window, view))
        {
            typeof(CharacterPhysicsController).GetField("_targetWindowHandle", Private)!.SetValue(physics, new IntPtr(123));
            Require(physics.BeginGrab(), "Grab should start with a cursor available");
            Require(Get<nint>(physics, "_targetWindowHandle") == nint.Zero && !physics.IsFalling, "Grab must clear autonomous trajectory");
            var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
            Step(player)(.016);
            var before = Get<double>(player, "_elapsed");
            physics.UpdateGrab(); physics.UpdateGrab();
            Require(view.CurrentState == CharacterState.Grabbed && Get<double>(player, "_elapsed") == before, "Pointer samples must not restart the animation");
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
        view.SetRenderMode(CharacterRenderMode.Sprite);
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
        var indices = new List<int>();
        var clip = SpriteClipFactory.FromFolder("walk", "Assets/Characters/LuKnight/Walk", 12)!;
        foreach (int hz in new[] { 30, 60, 120, 144 })
        {
            using var player = new SpriteAnimationPlayer(new Image(), new Image());
            player.Play(clip);
            var step = Step(player);
            for (int i = 0; i < hz * 3; i++) step(1.0 / hz);
            indices.Add(Get<int>(player, "_frameIndex"));
        }
        Require(indices.Distinct().Count() == 1, "Frame cadence differs across refresh rates");
        var boundaryImage = new Image();
        using (var boundaryPlayer = new SpriteAnimationPlayer(boundaryImage))
        {
            boundaryPlayer.Play(clip);
            for (int i = 1; i <= 24; i++)
            {
                Step(boundaryPlayer)(1.0 / 12);
                var expected = Get<BitmapSource[]>(boundaryPlayer, "_frames")[i % clip.Frames.Count];
                byte[] actualPixels = new byte[510 * 660 * 4];
                ((BitmapSource)boundaryImage.Source).CopyPixels(actualPixels, 510 * 4, 0);
                Require(actualPixels.SequenceEqual(Get<Dictionary<BitmapSource, byte[]>>(boundaryPlayer, "_pixels")[expected]),
                    "Floating-point phase boundary skipped a drawing");
            }
        }
        using var clockPlayer = new SpriteAnimationPlayer(new Image(), new Image());
        clockPlayer.Play(clip);
        var rendering = typeof(SpriteAnimationPlayer).GetMethod("OnRendering", Private)!;
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1) });
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1.016) });
        double time = Get<double>(clockPlayer, "_elapsed");
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1.016) });
        Require(time == Get<double>(clockPlayer, "_elapsed"), "Duplicate callback advanced clock");
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(8) });
        Require(Get<double>(clockPlayer, "_elapsed") - time <= .050001, "Stall must not skip a motion cycle");
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 120; i++) Step(clockPlayer)(1.0 / 60);
        watch.Stop();
        Console.WriteLine($"Walk interpolation CPU: {watch.Elapsed.TotalMilliseconds / 120:0.00} ms/update (offscreen).");
        var front = new Image(); var back = new Image();
        using var blendPlayer = new SpriteAnimationPlayer(front, back);
        blendPlayer.Play(clip); Step(blendPlayer)(.2);
        blendPlayer.Play(SpriteClipFactory.FromFolder("sleep", "Assets/Characters/LuKnight/Sleep", 1)!);
        Require(back.Source is not null && front.Opacity == 0, "Transition lost outgoing pose");
        Step(blendPlayer)(.05);
        Require(Math.Abs(front.Opacity - .5) < .001 && Math.Abs(back.Opacity - .5) < .001, "Transition should blend smoothly");
        Step(blendPlayer)(.05);
        Require(front.Opacity == 1 && back.Source is null, "Transition leaves a ghost image");
        bool failed = false;
        blendPlayer.FrameLoadFailed += () => failed = true;
        blendPlayer.Play(new SpriteAnimationClip("missing", new[] { "missing.png" }, 1, true));
        Require(failed && !blendPlayer.IsPlaying && front.Source is null, "Missing frame must stop safely");
    }

    private static void CheckHeadRegistration()
    {
        var widths = new List<int>(); var centers = new List<double>();
        foreach (string folder in new[] { "Idle", "Expressions" })
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets/Characters/LuKnight", folder), "*.png"))
        {
            var source = new BitmapImage(new Uri(path));
            var frame = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            byte[] pixels = new byte[510 * 660 * 4]; frame.CopyPixels(pixels, 510 * 4, 0);
            int widest = 0; double center = 0;
            for (int y = 375; y < 465; y++)
            {
                int left = 510, right = -1;
                for (int x = 0; x < 510; x++) if (pixels[(y * 510 + x) * 4 + 3] > 220) { left = Math.Min(left, x); right = x; }
                if (right - left + 1 > widest) { widest = right - left + 1; center = (left + right) / 2.0; }
            }
            widths.Add(widest); centers.Add(center);
        }
        Console.WriteLine($"Head registration: width {widths.Min()}..{widths.Max()} px, center {centers.Min()}..{centers.Max()} px.");
        Require(widths.Max() - widths.Min() <= 8, "Walk/expression head scale jitters");
        Require(centers.Max() - centers.Min() <= 5, "Walk/expression center jitters");
    }

    private static void CheckPuppet(string output)
    {
        var samples = new List<(string Name, BitmapSource Image)>();
        foreach (var motion in Enum.GetValues<SpritePuppetMotion>())
        {
            var rig = new SpritePuppet(motion);
            double period = motion == SpritePuppetMotion.Walk ? .8 : 1.1;
            rig.Advance(period / 4);
            double firstNear = rig.NearLegAngle, firstFar = rig.FarLegAngle;
            rig.Advance(period * .75);
            Require(Math.Abs(firstNear - rig.NearLegAngle) > 45, "Near leg is not stepping");
            Require(Math.Abs(firstFar - rig.FarLegAngle) > 45, "Far leg is not stepping");
            Require(Math.Sign(firstNear - rig.NearLegAngle) != Math.Sign(firstFar - rig.FarLegAngle), "Legs must alternate opposite phases");
            for (int i = 0; i < 24; i++)
            {
                rig.Advance(period * i / 24);
                Require(rig.Image.Width == 510 && rig.Image.Height == 660, "Joint movement changed the sprite canvas");
                foreach (int direction in new[] { -1, 1 })
                {
                    var image = new Image { Source = rig.Image, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(direction, 1) };
                    var rendered = Render(image); CheckAlphaBounds(rendered, $"{motion}/{direction}/{i}");
                    if (i % 6 == 0) samples.Add(($"{motion} {(direction == 1 ? "right" : "left")} {i / 6}", rendered));
                }
            }
            SavePuppetGif(motion, Path.Combine(output, motion.ToString().ToLowerInvariant() + "-directions.gif"));
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) rig.Advance(i / 60.0);
            watch.Stop();
            Console.WriteLine($"{motion} cutout rig: {watch.Elapsed.TotalMilliseconds / 1000:0.000} ms/update.");
        }
        SaveContactSheet(samples, Path.Combine(output, "directional-motion.png"));
        var view = new CharacterView(); view.SetState(CharacterState.Idle);
        view.Blink();
        Require(Get<SpriteAnimationClip>(Get<SpriteAnimationPlayer>(view, "_spritePlayer"), "_clip").Name == "blink-both-eyes", "Blink must close both eyes instead of winking");
        view.SetState(CharacterState.Walk);
        Require(Get<SpritePuppet>(Get<SpriteAnimationPlayer>(view, "_spritePlayer"), "_puppet") is not null, "Runtime walking must use the two-leg rig");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void SavePuppetGif(SpritePuppetMotion motion, string path)
    {
        var rig = new SpritePuppet(motion);
        var encoder = new GifBitmapEncoder();
        double period = motion == SpritePuppetMotion.Walk ? .8 : 1.1;
        int count = motion == SpritePuppetMotion.Walk ? 20 : 22;
        for (int i = 0; i < count; i++)
        {
            rig.Advance(i * period / count);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 41, 54)), null, new Rect(0, 0, 420, 270));
                dc.DrawText(new FormattedText(motion + "  LEFT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1), new Point(20, 12));
                dc.DrawText(new FormattedText(motion + "  RIGHT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1), new Point(230, 12));
                dc.PushTransform(new ScaleTransform(-1, 1, 105, 145));
                dc.DrawImage(rig.Image, new Rect(20, 35, 170, 220)); dc.Pop();
                dc.DrawImage(rig.Image, new Rect(230, 35, 170, 220));
            }
            var bitmap = new RenderTargetBitmap(420, 270, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)Math.Round(period / count * 100));
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
        using var stream = new MemoryStream(); encoder.Save(stream);
        byte[] gif = stream.ToArray();
        int header = 13 + ((gif[10] & 0x80) != 0 ? 3 * (1 << ((gif[10] & 7) + 1)) : 0);
        byte[] loop = { 0x21, 0xff, 0x0b, 78, 69, 84, 83, 67, 65, 80, 69, 50, 46, 48, 3, 1, 0, 0, 0 };
        using var output = File.Create(path); output.Write(gif, 0, header); output.Write(loop); output.Write(gif, header, gif.Length - header);
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
                throw new InvalidOperationException($"Clipped sprite at canvas border: {label} ({x},{y})");
        }
        Require(visible > 30000, "Sprite render is empty: " + label);
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

}
