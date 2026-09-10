using System;
using System.Windows;
using System.Windows.Threading;
using LuKnight.Views;
using LuKnight.Services;

namespace LuKnight.Behaviors;

public sealed class BehaviorController : IDisposable
{
    private readonly Window _window;
    private readonly CharacterView _character;

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    private DateTime _lastTickAt;
    private DateTime _nextDecisionAt;

    private DateTime _nextBlinkAt;
    private DateTime _lastInteractionAt;

    private bool _sleeping;

    private static readonly TimeSpan SleepAfter =
        TimeSpan.FromSeconds(30);

    private bool _walking;
    private bool _paused;

    private int _direction = 1;

    private const double WalkSpeed = 55.0;

    private bool _cursorNearby;
    private bool _cursorVeryClose;

    private DateTime _nextCursorReactionAt;

    private const double CursorAttentionRadius = 320;
    private const double CursorCuriousRadius = 140;
    private const double CursorWakeRadius = 100;

    private DateTime _cursorCloseSince;

    private DateTime _temporaryMoodUntil =
        DateTime.MinValue;

    private bool _thinking;

    private bool HasTemporaryMood(
    DateTime now)
    {
        return now < _temporaryMoodUntil;
    }


    private void SetAmbientMood(
        CharacterMood mood,
        DateTime now)
    {
        if (_thinking ||
            HasTemporaryMood(now))
        {
            return;
        }

        if (_character.CurrentMood != mood)
        {
            _character.SetMood(mood);
        }
    }


    private void SetTemporaryMood(
        CharacterMood mood,
        double seconds)
    {
        _temporaryMoodUntil =
            DateTime.UtcNow
                .AddSeconds(seconds);

        _character.SetMood(mood);
    }


    private void UpdateMood(
        DateTime now)
    {
        if (_thinking)
            return;

        if (now < _temporaryMoodUntil)
            return;

        if (_temporaryMoodUntil !=
            DateTime.MinValue)
        {
            _temporaryMoodUntil =
                DateTime.MinValue;

            if (_cursorVeryClose)
            {
                _character.SetMood(
                    CharacterMood.Happy);
            }
            else if (_cursorNearby)
            {
                _character.SetMood(
                    CharacterMood.Curious);
            }
            else
            {
                _character.SetMood(
                    CharacterMood.Neutral);
            }
        }
    }

    private bool UpdateCursorAwareness(
    DateTime now)
    {
        if (!DesktopCursorService.TryGetPosition(
                out Point cursorScreen))
        {
            _character.RelaxCursorLook();
            return false;
        }

        // GetCursorPos menggunakan screen pixels.
        // WPF mengubahnya menjadi coordinate system
        // window/DPI yang benar.
        Point cursorInWindow;

        try
        {
            cursorInWindow =
                _window.PointFromScreen(
                    cursorScreen);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        double characterWidth =
            _character.ActualWidth;

        double characterHeight =
            _character.ActualHeight;

        if (characterWidth <= 0 ||
            characterHeight <= 0)
        {
            return false;
        }

        // Posisi kira-kira pusat wajah Lu-Knight.
        Point faceCenter =
            _character.TranslatePoint(
                new Point(
                    characterWidth * 0.50,
                    characterHeight * 0.50),
                _window);

        double deltaX =
            cursorInWindow.X -
            faceCenter.X;

        double deltaY =
            cursorInWindow.Y -
            faceCenter.Y;

        double distance =
            Math.Sqrt(
                (deltaX * deltaX) +
                (deltaY * deltaY));


        // =========================
        // SLEEPING
        // =========================

        if (_sleeping)
        {
            if (distance <= CursorWakeRadius)
            {
                _lastInteractionAt = now;

                WakeUp();

                _character.TwitchEars();
            }

            return false;
        }


        // =========================
        // CURSOR FAR AWAY
        // =========================

        if (distance > CursorAttentionRadius)
        {
            if (_cursorNearby)
            {
                _cursorNearby = false;
                _cursorVeryClose = false;

                ScheduleNextDecision(
                    0.8,
                    1.8);
            }

            _character.RelaxCursorLook();

            SetAmbientMood(
    CharacterMood.Neutral,
    now);

            return false;
        }


        // =========================
        // CURSOR ENTERED AREA
        // =========================

        if (!_cursorNearby)
        {
            _cursorNearby = true;

            _cursorCloseSince = now;

            SetAmbientMood(
                CharacterMood.Curious,
                now);

            // Lu-Knight berhenti untuk melihat user.
            _walking = false;

            _character.SetState(
                CharacterState.Idle);

            _character.SetFacingDirection(
                _direction);

            _character.TwitchEars();

            _nextCursorReactionAt =
                now.AddSeconds(1.5);
        }


        // =========================
        // EYE TRACKING
        // =========================

        double normalizedX =
            Math.Clamp(
                deltaX / 130.0,
                -1,
                1);

        double normalizedY =
            Math.Clamp(
                deltaY / 100.0,
                -1,
                1);

        _character.TrackCursor(
            normalizedX,
            normalizedY);


        // =========================
        // VERY CLOSE / CURIOUS
        // =========================

        if (distance <= CursorCuriousRadius)
        {
            _lastInteractionAt = now;

            if (!_cursorVeryClose)
            {
                _cursorVeryClose = true;
                _cursorCloseSince = now;

                _character.TwitchEars();
            }

            if (now - _cursorCloseSince
                >= TimeSpan.FromSeconds(1.1))
            {
                SetAmbientMood(
                    CharacterMood.Happy,
                    now);
            }
            else
            {
                SetAmbientMood(
                    CharacterMood.Curious,
                    now);
            }
            // Sesekali telinga bereaksi lagi
            // jika cursor tetap dekat.
            if (now >= _nextCursorReactionAt)
            {
                _character.TwitchEars();

                _nextCursorReactionAt =
                    now.AddSeconds(
                        1.5 +
                        (_random.NextDouble() * 2));
            }
        }
        else if (distance >
                 CursorCuriousRadius + 35)
        {
            // Hysteresis supaya status tidak
            // berkedip-kedip di batas radius.
            _cursorVeryClose = false;

            SetAmbientMood(
                CharacterMood.Curious,
                now);
        }

        return true;
    }

    public void ReactToClick()
    {
        NotifyUserInteraction();

        SetTemporaryMood(
            CharacterMood.Surprised,
            0.30);
    }


    public void ReactHappy()
    {
        SetTemporaryMood(
            CharacterMood.Happy,
            0.90);
    }

    public void ReactDizzy()
    {
        SetTemporaryMood(
            CharacterMood.Dizzy,
            2.20);
    }


    public void ReactConfused()
    {
        SetTemporaryMood(
            CharacterMood.Confused,
            1.20);
    }


    public void SetThinking(bool thinking)
    {
        _thinking = thinking;

        _temporaryMoodUntil =
            DateTime.MinValue;

        if (thinking)
        {
            _character.SetMood(
                CharacterMood.Thinking);

            return;
        }

        if (_cursorVeryClose)
        {
            _character.SetMood(
                CharacterMood.Happy);
        }
        else if (_cursorNearby)
        {
            _character.SetMood(
                CharacterMood.Curious);
        }
        else
        {
            _character.SetMood(
                CharacterMood.Neutral);
        }
    }
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

        _character.SetState(
            CharacterState.Idle);

        _character.SetFacingDirection(
            _direction);

        DateTime now =
            DateTime.UtcNow;

        _lastTickAt = now;
        _lastInteractionAt = now;

        ScheduleNextDecision(
            1.5,
            3.5);

        ScheduleNextBlink();

        _timer.Start();
    }

    public void Pause()
    {
        if (_paused)
            return;

        _paused = true;
        _walking = false;

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(_direction);
    }

    public void Resume()
    {
        if (!_paused)
            return;

        _paused = false;
        _walking = false;

        PlaceOnDesktopBottom();

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(_direction);

        _lastTickAt = DateTime.UtcNow;

        ScheduleNextDecision(
            minSeconds: 0.8,
            maxSeconds: 2.0);
    }

    public void NotifyUserInteraction()
    {
        _lastInteractionAt =
            DateTime.UtcNow;

        if (_sleeping)
        {
            WakeUp();
        }
    }

    private void OnTick(
    object? sender,
    EventArgs e)
    {
        DateTime now =
            DateTime.UtcNow;

        double deltaSeconds =
            (now - _lastTickAt)
            .TotalSeconds;

        _lastTickAt = now;

        if (_paused)
            return;

        deltaSeconds =
            Math.Clamp(
                deltaSeconds,
                0,
                0.1);

        UpdateMood(now);

        bool cursorHasAttention =
            UpdateCursorAwareness(now);

        // =========================
        // AUTO SLEEP
        // =========================

        if (!_sleeping &&
            now - _lastInteractionAt
                >= SleepAfter)
        {
            EnterSleep();
            return;
        }

        if (_sleeping)
            return;


        // =========================
        // AUTO BLINK
        // =========================

        if (now >= _nextBlinkAt)
        {
            _character.Blink();

            ScheduleNextBlink();
        }


        // =========================
        // BEHAVIOUR
        // =========================

        if (!cursorHasAttention &&
            now >= _nextDecisionAt)
        {
            ChooseNextBehavior();
        }


        if (_walking)
        {
            MoveCharacter(
                deltaSeconds);
        }
    }
    private void ChooseNextBehavior()
    {
        if (_walking)
        {
            StopWalking();
            return;
        }

        double decision =
            _random.NextDouble();

        if (decision < 0.40)
        {
            StartWalking();
            return;
        }

        if (decision < 0.60)
        {
            _character.TwitchEars();

            ScheduleNextDecision(
                1.0,
                2.5);

            return;
        }

        if (decision < 0.90)
        {
            int lookDirection =
                _random.Next(0, 2) == 0
                    ? -1
                    : 1;

            _character.LookSide(
                lookDirection);

            ScheduleNextDecision(
                1.0,
                2.5);

            return;
        }

        _character.SetState(
            CharacterState.Idle);

        _character.SetFacingDirection(
            _direction);

        ScheduleNextDecision(
            1.0,
            3.0);
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

        _character.LookSide(
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

    private void EnterSleep()
    {
        _walking = false;
        _sleeping = true;

        _character.SetMood(
    CharacterMood.Neutral);

        _character.SetState(
            CharacterState.Sleep);

        _character.SetFacingDirection(
            _direction);
    }


    private void WakeUp()
    {
        if (!_sleeping)
            return;

        _sleeping = false;

        _character.SetState(
            CharacterState.Idle);

        _character.SetFacingDirection(
            _direction);

        ScheduleNextDecision(
            0.8,
            2.0);

        ScheduleNextBlink();
    }

    private void ScheduleNextBlink()
    {
        double delay =
            2.5 +
            (_random.NextDouble() * 3.5);

        _nextBlinkAt =
            DateTime.UtcNow
                .AddSeconds(delay);
    }
}