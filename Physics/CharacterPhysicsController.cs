using System;
using System.Windows;
using System.Windows.Threading;
using LuKnight.Services;
using LuKnight.Views;

namespace LuKnight.Physics;

public sealed class CharacterPhysicsController : IDisposable
{
    private readonly Window _window;
    private readonly CharacterView _character;

    private readonly DispatcherTimer _timer;

    private bool _isGrabbed;
    private bool _isFalling;

    private Point _grabOffset;

    private Point _lastCursor;
    private DateTime _lastCursorAt;

    private double _velocityX;
    private double _velocityY;

    private int _bounceCount;

    private DateTime _lastTickAt;

    private const double Gravity = 1850.0;

    private const double MaximumThrowSpeed = 1450.0;

    private const double WallBounce = 0.42;
    private const double FloorBounce = 0.30;

    private const double GroundFriction = 0.62;

    private double _maximumImpactSpeed;

    public bool IsGrabbed => _isGrabbed;
    public bool IsFalling => _isFalling;

    public bool IsActive =>
        _isGrabbed || _isFalling;

    public event Action<double>? Landed;

    public CharacterPhysicsController(
        Window window,
        CharacterView character)
    {
        _window = window;
        _character = character;

        _timer = new DispatcherTimer
        {
            Interval =
                TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += OnTick;
        _timer.Start();

        _lastTickAt =
            DateTime.UtcNow;
    }

    public bool BeginGrab()
    {
        if (!TryGetCursor(out Point cursor))
            return false;

        _isGrabbed = true;
        _isFalling = false;

        _bounceCount = 0;
        _maximumImpactSpeed = 0;

        _velocityX = 0;
        _velocityY = 0;

        _grabOffset =
            new Point(
                cursor.X - _window.Left,
                cursor.Y - _window.Top);

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

            // Sedikit smoothing supaya throw
            // tidak terlalu sensitif.
            _velocityX =
                Lerp(
                    _velocityX,
                    instantVelocityX,
                    0.38);

            _velocityY =
                Lerp(
                    _velocityY,
                    instantVelocityY,
                    0.38);

            _velocityX =
                Math.Clamp(
                    _velocityX,
                    -MaximumThrowSpeed,
                    MaximumThrowSpeed);

            _velocityY =
                Math.Clamp(
                    _velocityY,
                    -MaximumThrowSpeed,
                    MaximumThrowSpeed);
        }

        _window.Left =
            cursor.X -
            _grabOffset.X;

        _window.Top =
            cursor.Y -
            _grabOffset.Y;

        _lastCursor = cursor;
        _lastCursorAt = now;
    }

    public void EndGrab()
    {
        if (!_isGrabbed)
            return;

        _isGrabbed = false;
        _isFalling = true;

        _bounceCount = 0;
        _maximumImpactSpeed = 0;

        _character.SetState(
            CharacterState.Falling);

        _character.SetMood(
            CharacterMood.Surprised);

        _lastTickAt =
            DateTime.UtcNow;
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
        Rect area =
            SystemParameters.WorkArea;

        double width =
            GetWindowWidth();

        double height =
            GetWindowHeight();

        _velocityY +=
            Gravity * delta;

        double nextLeft =
            _window.Left +
            (_velocityX * delta);

        double nextTop =
            _window.Top +
            (_velocityY * delta);

        double minLeft =
            area.Left;

        double maxLeft =
            area.Right - width;

        double floor =
            area.Bottom - height;


        // LEFT WALL
        if (nextLeft <= minLeft)
        {
            nextLeft = minLeft;

            _velocityX =
                Math.Abs(_velocityX)
                * WallBounce;

            _character.SetFacingDirection(1);
        }


        // RIGHT WALL
        if (nextLeft >= maxLeft)
        {
            nextLeft = maxLeft;

            _velocityX =
                -Math.Abs(_velocityX)
                * WallBounce;

            _character.SetFacingDirection(-1);
        }


        // FLOOR
        if (nextTop >= floor)
        {
            double impactSpeed =
                Math.Abs(_velocityY);
            _maximumImpactSpeed =
                Math.Max(
                    _maximumImpactSpeed,
                    impactSpeed);

            nextTop = floor;

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
                _window.Left =
                    nextLeft;

                _window.Top =
                    floor;

                Settle(
                    _maximumImpactSpeed);

                return;
            }
        }

        _window.Left =
            nextLeft;

        _window.Top =
            nextTop;
    }

    private void Settle(
    double impactSpeed)
    {
        _isFalling = false;

        _velocityX = 0;
        _velocityY = 0;

        _bounceCount = 0;

        _character.SetState(
            CharacterState.Idle);

        Landed?.Invoke(
            impactSpeed);

        _maximumImpactSpeed = 0;
    }
    
    private bool TryGetCursor(
        out Point position)
    {
        return DesktopCursorService
            .TryGetPositionDip(
                _window,
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

    private static double Lerp(
        double from,
        double to,
        double amount)
    {
        return from +
               ((to - from) * amount);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}