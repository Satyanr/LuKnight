using System;
using System.Windows;
using System.Windows.Threading;
using LuKnight.Views;

namespace LuKnight.Behaviors;

public sealed class BehaviorController : IDisposable
{
    private readonly Window _window;
    private readonly CharacterView _character;

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    private DateTime _lastTickAt;
    private DateTime _nextDecisionAt;

    private bool _walking;
    private bool _paused;

    private int _direction = 1;

    private const double WalkSpeed = 55.0;

    public BehaviorController(
        Window window,
        CharacterView character)
    {
        _window = window;
        _character = character;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _timer.Tick += OnTick;
    }

    public void Start()
    {
        PlaceOnDesktopBottom();

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(_direction);

        _lastTickAt = DateTime.UtcNow;

        ScheduleNextDecision(
            minSeconds: 1.5,
            maxSeconds: 3.5);

        _timer.Start();
    }

    public void Pause()
    {
        if (_paused)
            return;

        _paused = true;

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(_direction);
    }

    public void Resume()
    {
        if (!_paused)
            return;

        _paused = false;

        // Phase pertama:
        // setelah di-drag, Lu-Knight kembali ke "lantai".
        PlaceOnDesktopBottom();

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(_direction);

        _lastTickAt = DateTime.UtcNow;

        ScheduleNextDecision(
            minSeconds: 0.8,
            maxSeconds: 2.0);
    }

    private void OnTick(
        object? sender,
        EventArgs e)
    {
        DateTime now = DateTime.UtcNow;

        double deltaSeconds =
            (now - _lastTickAt).TotalSeconds;

        _lastTickAt = now;

        if (_paused)
            return;

        deltaSeconds =
            Math.Clamp(
                deltaSeconds,
                0,
                0.1);

        if (now >= _nextDecisionAt)
        {
            ChooseNextBehavior();
        }

        if (_walking)
        {
            MoveCharacter(deltaSeconds);
        }
    }

    private void ChooseNextBehavior()
    {
        if (_walking)
        {
            StopWalking();
            return;
        }

        // Tidak selalu langsung jalan.
        // Kadang Lu-Knight memilih tetap diam.
        double decision = _random.NextDouble();

        if (decision < 0.70)
        {
            StartWalking();
        }
        else
        {
            _character.SetState(
                CharacterState.Idle);

            _character.SetFacingDirection(
                _direction);

            ScheduleNextDecision(
                minSeconds: 1.0,
                maxSeconds: 3.0);
        }
    }

    private void StartWalking()
    {
        _walking = true;

        _direction =
            _random.Next(0, 2) == 0
                ? -1
                : 1;

        _character.SetState(
            CharacterState.Walk);

        _character.SetFacingDirection(
            _direction);

        ScheduleNextDecision(
            minSeconds: 2.0,
            maxSeconds: 5.0);
    }

    private void StopWalking()
    {
        _walking = false;

        _character.SetState(
            CharacterState.Idle);

        _character.SetFacingDirection(
            _direction);

        ScheduleNextDecision(
            minSeconds: 1.0,
            maxSeconds: 4.0);
    }

    private void MoveCharacter(
        double deltaSeconds)
    {
        Rect workArea =
            SystemParameters.WorkArea;

        double characterWidth =
            _window.ActualWidth;

        double minLeft =
            workArea.Left;

        double maxLeft =
            workArea.Right -
            characterWidth;

        double newLeft =
            _window.Left +
            (_direction *
             WalkSpeed *
             deltaSeconds);

        if (newLeft <= minLeft)
        {
            newLeft = minLeft;
            _direction = 1;

            _character.SetFacingDirection(
                _direction);
        }
        else if (newLeft >= maxLeft)
        {
            newLeft = maxLeft;
            _direction = -1;

            _character.SetFacingDirection(
                _direction);
        }

        _window.Left = newLeft;
    }

    private void PlaceOnDesktopBottom()
    {
        Rect workArea =
            SystemParameters.WorkArea;

        double width =
            _window.ActualWidth > 0
                ? _window.ActualWidth
                : _window.Width;

        double height =
            _window.ActualHeight > 0
                ? _window.ActualHeight
                : _window.Height;

        double maxLeft =
            Math.Max(
                workArea.Left,
                workArea.Right - width);

        _window.Left =
            Math.Clamp(
                _window.Left,
                workArea.Left,
                maxLeft);

        _window.Top =
            workArea.Bottom -
            height;
    }

    private void ScheduleNextDecision(
        double minSeconds,
        double maxSeconds)
    {
        double delay =
            minSeconds +
            (_random.NextDouble() *
             (maxSeconds - minSeconds));

        _nextDecisionAt =
            DateTime.UtcNow.AddSeconds(delay);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}