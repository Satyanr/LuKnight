using System;
using System.Windows;
using System.Windows.Media;
using LuKnight.Services;
using LuKnight.Views;

namespace LuKnight.Physics;

public sealed class CharacterPhysicsController : IDisposable
{
    private readonly Window _window;
    private readonly CharacterView _character;

    private TimeSpan? _lastRenderTime;
    private bool _suspended;

    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        _lastRenderTime = null;
        _lastTickAt = DateTime.UtcNow;
    }

    private bool _isGrabbed;
    private bool _isFalling;

    private Point _grabOffset;
    private readonly PointerVelocityTracker _pointerVelocity = new();
    private bool _trackingPointer;
    private readonly Func<double> _pointerClock;
    private readonly Func<Point?>? _cursorSource;
    private double PointerTime => _pointerClock();

    public void PrepareGrab(Point position, double time)
    {
        _trackingPointer = true;
        _pointerVelocity.Reset(position, time);
    }

    public void CancelPreparedGrab()
    {
        _trackingPointer = false;
        _lastTickAt = DateTime.UtcNow;
    }

    public void SamplePointer(Point position)
    {
        if (!_trackingPointer) return;
        _pointerVelocity.Add(position, PointerTime);
        if (!_isGrabbed) return;
        Vector velocity = _pointerVelocity.Velocity;
        _velocityX = Math.Clamp(velocity.X, -MaximumThrowSpeed, MaximumThrowSpeed);
        _velocityY = Math.Clamp(velocity.Y, -MaximumThrowSpeed, MaximumThrowSpeed);
    }

    private Point _lastCursor;
    private DateTime _lastCursorAt;

    private double _velocityX;
    private double _velocityY;

    private nint _targetWindowHandle;

    private int _bounceCount;

    private DateTime _lastTickAt;

    private const double Gravity = 1850.0;

    private const double MaximumThrowSpeed = 1450.0;

    private const double WallBounce = 0.42;
    private const double FloorBounce = 0.30;

    private const double GroundFriction = 0.62;

    private double _maximumImpactSpeed;

    private int _shakeReversalCount;
    private int _lastShakeDirection;

    private DateTime _shakeWindowStartedAt;
    private DateTime _lastShakeImpulseAt;

    private bool _wasShakenDuringGrab;

    private const double ShakeVelocityThreshold =
        650.0;

    private const int ShakeReversalsRequired =
        4;

    private static readonly TimeSpan ShakeWindow =
        TimeSpan.FromSeconds(1.20);

    private static readonly TimeSpan ShakeImpulseCooldown =
        TimeSpan.FromMilliseconds(75);

    private void DetectShake(
double velocityX,
double velocityY,
DateTime now)
    {
        double absX =
            Math.Abs(velocityX);

        double absY =
            Math.Abs(velocityY);

        double strongestVelocity =
            Math.Max(absX, absY);

        if (strongestVelocity <
            ShakeVelocityThreshold)
        {
            return;
        }


        if (now - _shakeWindowStartedAt >
            ShakeWindow)
        {
            _shakeWindowStartedAt = now;

            _shakeReversalCount = 0;
            _lastShakeDirection = 0;
        }


        if (now - _lastShakeImpulseAt <
            ShakeImpulseCooldown)
        {
            return;
        }


        int direction;

        if (absX >= absY)
        {
            direction =
                velocityX >= 0
                    ? 1
                    : -1;
        }
        else
        {
            direction =
                velocityY >= 0
                    ? 2
                    : -2;
        }


        if (_lastShakeDirection != 0 &&
             direction == -_lastShakeDirection)
        {
            _shakeReversalCount++;
        }


        _lastShakeDirection =
            direction;

        _lastShakeImpulseAt =
            now;


        if (_shakeReversalCount <
            ShakeReversalsRequired)
        {
            return;
        }


        if (_wasShakenDuringGrab)
            return;


        _wasShakenDuringGrab = true;

        Shaken?.Invoke();
    }

    public bool IsGrabbed => _isGrabbed;
    public bool IsFalling => _isFalling;

    public bool IsActive =>
        _isGrabbed || _isFalling;

    public bool WasShakenDuringGrab =>
        _wasShakenDuringGrab;

    public event Action? Shaken;

    public event Action<
    double,
    bool,
    nint?>? Landed;

    public CharacterPhysicsController(
        Window window,
        CharacterView character,
        Func<Point?>? cursorSource = null,
        Func<double>? pointerClock = null)
    {
        _window = window;
        _character = character;
        _cursorSource = cursorSource;
        _pointerClock = pointerClock ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);

        CompositionTarget.Rendering += OnRendering;

        _lastTickAt =
            DateTime.UtcNow;
    }

    public bool BeginGrab(Point? anchor = null, double? anchorTime = null)
    {
        if (!TryGetCursor(out Point cursor))
            return false;

        _isGrabbed = true;
        _isFalling = false;

        _bounceCount = 0;
        _maximumImpactSpeed = 0;

        _targetWindowHandle =
            nint.Zero;

        _shakeReversalCount = 0;
        _lastShakeDirection = 0;

        _shakeWindowStartedAt =
            DateTime.UtcNow;

        _lastShakeImpulseAt =
            DateTime.MinValue;

        _wasShakenDuringGrab = false;

        _velocityX = 0;
        _velocityY = 0;

        Rect windowBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);

        _grabOffset =
            new Point(
                (anchor ?? cursor).X -
                windowBounds.Left,
                (anchor ?? cursor).Y -
                windowBounds.Top);

        if (!_trackingPointer) PrepareGrab(anchor ?? cursor, anchorTime ?? PointerTime);
        SamplePointer(cursor);

        _lastCursor = cursor;
        _lastCursorAt = DateTime.UtcNow;

        _character.SetState(
            CharacterState.Grabbed);

        _character.SetMood(
            CharacterMood.Surprised);

        return true;
    }

    public void UpdateGrab()
    {
        if (!_isGrabbed)
            return;

        if (_character.CurrentState !=
            CharacterState.Grabbed)
        {
            _character.SetState(
                CharacterState.Grabbed);
        }

        if (!TryGetCursor(out Point cursor))
            return;

        DateTime now =
            DateTime.UtcNow;

        double delta =
            (now - _lastCursorAt)
            .TotalSeconds;

        if (delta > 0.001)
        {
            double instantVelocityX =
                (cursor.X - _lastCursor.X)
                / delta;

            double instantVelocityY =
                (cursor.Y - _lastCursor.Y)
                / delta;

            DetectShake(
            instantVelocityX,
            instantVelocityY,
            now);

        }

        SamplePointer(cursor);

        DesktopMonitorService
            .SetWindowPosition(
                _window,
                cursor.X -
                _grabOffset.X,
                cursor.Y -
                _grabOffset.Y);

        _character.SetAirborneVelocity(_velocityX, _velocityY);

        _lastCursor = cursor;
        _lastCursorAt = now;
    }

    public void EndGrab()
    {
        if (!_isGrabbed)
            return;

        // Include the final cursor sample before releasing into the ballistic path.
        UpdateGrab();
        _trackingPointer = false;
        _isGrabbed = false;
        _isFalling = true;

        _bounceCount = 0;
        _maximumImpactSpeed = 0;

        _character.SetState(
            CharacterState.Falling);

        _character.SetMood(
        _wasShakenDuringGrab
            ? CharacterMood.Dizzy
            : CharacterMood.Surprised);

        _lastTickAt =
            DateTime.UtcNow;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_suspended) return;
        if (_trackingPointer && !_isGrabbed)
        {
            if (TryGetCursor(out Point cursor)) SamplePointer(cursor);
            _lastTickAt = DateTime.UtcNow;
            return;
        }
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (_lastRenderTime == time) return;
        _lastRenderTime = time;
        OnTick(sender, e);
    }

    private void OnTick(
        object? sender,
        EventArgs e)
    {
        DateTime now =
            DateTime.UtcNow;

        double delta =
            (now - _lastTickAt)
            .TotalSeconds;

        _lastTickAt = now;

        if (_isGrabbed)
        {
            UpdateGrab();
            return;
        }

        if (!_isFalling)
            return;

        delta =
            Math.Clamp(
                delta,
                0,
                0.05);

        UpdateFalling(delta);
    }

    private void UpdateFalling(
    double delta)
    {
        Rect windowBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);

        DesktopMonitorInfo monitor =
            DesktopMonitorService
                .GetMonitorForWindow(
                    _window);

        Rect area =
            monitor.WorkArea;


        double width =
            windowBounds.Width;

        double height =
            CharacterGrounding.GetFootOffset(_window, _character, windowBounds);
        Rect collisionBounds = new(windowBounds.Left, windowBounds.Top, windowBounds.Width, height);


        _velocityY +=
            Gravity * delta;

        _character.SetAirborneVelocity(
            _velocityX,
            _velocityY);


        double nextLeft =
            windowBounds.Left +
            (_velocityX * delta);

        double nextTop =
            windowBounds.Top +
            (_velocityY * delta);


        double minLeft =
            area.Left;

        double maxLeft =
            area.Right -
            width;

        double ceiling =
            area.Top;

        double floor =
            area.Bottom -
            height;


        // =========================
        // LEFT EDGE
        // =========================

        if (nextLeft <= minLeft)
        {
            bool canCross =
                !monitor.HasTaskbarLeft &&
                DesktopMonitorService
                    .TryGetNeighbor(
                        monitor,
                        MonitorDirection.Left,
                        out _);

            if (!canCross)
            {
                nextLeft =
                    minLeft;

                _velocityX =
                    Math.Abs(_velocityX) *
                    WallBounce;

                _character
                    .SetFacingDirection(1);
            }
        }


        // =========================
        // RIGHT EDGE
        // =========================

        if (nextLeft >= maxLeft)
        {
            bool canCross =
                !monitor.HasTaskbarRight &&
                DesktopMonitorService
                    .TryGetNeighbor(
                        monitor,
                        MonitorDirection.Right,
                        out _);

            if (!canCross)
            {
                nextLeft =
                    maxLeft;

                _velocityX =
                    -Math.Abs(_velocityX) *
                    WallBounce;

                _character
                    .SetFacingDirection(-1);
            }
        }


        // =========================
        // TOP / CEILING
        // =========================

        if (nextTop <= ceiling)
        {
            bool canCross =
                !monitor.HasTaskbarTop &&
                DesktopMonitorService
                    .TryGetNeighbor(
                        monitor,
                        MonitorDirection.Up,
                        out _);

            if (!canCross)
            {
                nextTop =
                    ceiling;

                _velocityY =
                    Math.Abs(_velocityY) *
                    0.25;
            }
        }

        // =========================
        // INTENTIONAL JUMP TARGET
        // =========================

        if (_targetWindowHandle != nint.Zero &&
            DesktopWindowService.TryGetWindow(
                _targetWindowHandle,
                out DesktopWindowInfo targetWindow))
        {
            double targetSurfaceTop =
                targetWindow.Bounds.Top;

            double nextBottom =
                nextTop + height;

            double characterLeft =
                nextLeft + 14;

            double characterRight =
                nextLeft + width - 14;

            bool overlapsTarget =
                characterRight > targetWindow.Bounds.Left + 28 &&
                characterLeft < targetWindow.Bounds.Right - 28;

            bool reachesTargetHeight =
                _velocityY > 0 &&
                collisionBounds.Bottom <= targetSurfaceTop + 1 &&
                nextBottom >= targetSurfaceTop;

            if (reachesTargetHeight)
            {
                if (overlapsTarget)
                {
                    double impactSpeed =
                        Math.Abs(_velocityY);

                    DesktopMonitorService
                        .SetWindowPosition(
                            _window,
                            nextLeft,
                            targetSurfaceTop - height);

                    Settle(
                        impactSpeed,
                        targetWindow.Handle);

                    return;
                }

                // Target terlewat; aktifkan kembali landing normal.
                _targetWindowHandle = nint.Zero;
            }
            else if (_velocityY > 0 &&
                     collisionBounds.Bottom > targetSurfaceTop + 1)
            {
                // Target bergerak hingga permukaannya sudah terlewati.
                _targetWindowHandle = nint.Zero;
            }
        }
        else
        {
            _targetWindowHandle = nint.Zero;
        }

        // =========================
        // APPLICATION WINDOW TOP
        // =========================

        if (_velocityY > 0 &&
            DesktopWindowService
                .TryFindLandingSurface(
                    collisionBounds,
                    nextLeft,
                    nextTop,
                    out DesktopWindowInfo
                        landingWindow))
        {
            double impactSpeed =
                Math.Abs(
                    _velocityY);


            _maximumImpactSpeed =
                Math.Max(
                    _maximumImpactSpeed,
                    impactSpeed);


            double windowSurfaceTop =
                landingWindow.Bounds.Top;


            nextTop =
                windowSurfaceTop -
                height;


            _bounceCount++;


            if (impactSpeed >= 170 &&
                _bounceCount <= 2)
            {
                _velocityY =
                    -impactSpeed *
                    FloorBounce;

                _velocityX *=
                    GroundFriction;
            }
            else
            {
                DesktopMonitorService
                    .SetWindowPosition(
                        _window,
                        nextLeft,
                        nextTop);


                Settle(
                    _maximumImpactSpeed,
                    landingWindow.Handle);

                return;
            }
        }


        // =========================
        // FLOOR
        // =========================

        if (nextTop >= floor)
        {
            bool canCrossDown =
                !monitor.HasTaskbarBottom &&
                DesktopMonitorService
                    .TryGetNeighbor(
                        monitor,
                        MonitorDirection.Down,
                        out _);


            if (!canCrossDown)
            {
                double impactSpeed =
                    Math.Abs(
                        _velocityY);


                _maximumImpactSpeed =
                    Math.Max(
                        _maximumImpactSpeed,
                        impactSpeed);


                nextTop =
                    floor;

                _bounceCount++;


                if (impactSpeed >= 170 &&
                    _bounceCount <= 2)
                {
                    _velocityY =
                        -impactSpeed *
                        FloorBounce;

                    _velocityX *=
                        GroundFriction;
                }
                else
                {
                    DesktopMonitorService
                        .SetWindowPosition(
                            _window,
                            nextLeft,
                            floor);

                    Settle(
                        _maximumImpactSpeed,
                        null);

                    return;
                }
            }
        }


        DesktopMonitorService
            .SetWindowPosition(
                _window,
                nextLeft,
                nextTop);
    }

    public void StartFallFromRest()
    {
        StartFall(
            0,
            0);
    }


    public void StartFall(
        SurfaceJumpPlan jumpPlan)
    {
        StartFall(
            jumpPlan.VelocityX,
            jumpPlan.VelocityY,
            jumpPlan.Target.Handle);
    }

    public void StartFall(
        double velocityX,
        double velocityY)
    {
        StartFall(
            velocityX,
            velocityY,
            nint.Zero);
    }

    public void StartFall(
        double velocityX,
        double velocityY,
        nint targetWindowHandle)
    {
        if (_isGrabbed ||
            _isFalling)
        {
            return;
        }


        _isFalling = true;

        _bounceCount = 0;

        _maximumImpactSpeed = 0;

        _wasShakenDuringGrab = false;

        _targetWindowHandle =
            targetWindowHandle;


        _velocityX =
            Math.Clamp(
                velocityX,
                -MaximumThrowSpeed,
                MaximumThrowSpeed);

        _velocityY =
            Math.Clamp(
                velocityY,
                -MaximumThrowSpeed,
                MaximumThrowSpeed);


        _character.SetState(
            CharacterState.Falling);

        _character.SetMood(
            CharacterMood.Surprised);


        _lastTickAt =
            DateTime.UtcNow;
    }
    private void Settle(
    double impactSpeed,
    nint? supportWindow)
    {
        _isFalling = false;

        _velocityX = 0;
        _velocityY = 0;

        _targetWindowHandle = nint.Zero;

        _bounceCount = 0;

        _character.SetState(
            CharacterState.Idle);

        bool wasShaken =
        _wasShakenDuringGrab;

        Landed?.Invoke(
        impactSpeed,
        wasShaken,
        supportWindow);

        _wasShakenDuringGrab = false;

        _maximumImpactSpeed = 0;
    }

    private bool TryGetCursor(
    out Point position)
    {
        if (_cursorSource is not null)
        {
            Point? sample = _cursorSource();
            position = sample ?? default;
            return sample.HasValue;
        }
        return DesktopCursorService
            .TryGetPosition(
                out position);
    }

    private double GetWindowWidth()
    {
        return _window.ActualWidth > 0
            ? _window.ActualWidth
            : _window.Width;
    }

    private double GetWindowHeight()
    {
        return _window.ActualHeight > 0
            ? _window.ActualHeight
            : _window.Height;
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
    }
}
