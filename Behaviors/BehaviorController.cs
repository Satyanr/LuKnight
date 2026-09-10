using System;
using System.Windows;
using System.Windows.Threading;
using LuKnight.Views;
using LuKnight.Services;
using System.Diagnostics;

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

    private TimeSpan _sleepAfter = TimeSpan.FromSeconds(75);
    private DateTime _sleepUntil = DateTime.MinValue;

    private const double MinimumAwakeBeforeNapSeconds = 60.0;
    private const double MaximumAwakeBeforeNapSeconds = 120.0;
    private const double MinimumNapSeconds = 7.0;
    private const double MaximumNapSeconds = 15.0;

    private bool _walking;
    private bool _paused;

    private int _direction = 1;

    private const double WalkSpeed = 70.0;

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

    private readonly SurfaceBehaviorController _surfaceController;

    private DesktopApplicationKind
    _lastDebugApplicationKind =
        DesktopApplicationKind.Unknown;

    private void DebugApplicationContext(
        DesktopApplicationKind appKind)
    {
        if (appKind ==
            _lastDebugApplicationKind)
        {
            return;
        }


        _lastDebugApplicationKind =
            appKind;


        Debug.WriteLine(
            $"[Lu-Knight] App context: {appKind}");
    }

    public event Action? SupportLost;

    public event Action<
        double,
        double,
        nint>? SurfaceLaunchRequested;

    public void SetSupportWindow(nint? windowHandle)
    {
        _surfaceController.SetSupportWindow(windowHandle);
    }

    public void ClearSupportWindow()
    {
        _surfaceController.ClearSupportWindow();
    }

    private void SurfaceController_SupportLost()
    {
        _walking = false;
        _paused = true;
        SupportLost?.Invoke();
    }

    private void SurfaceController_SurfaceLaunchRequested(
        double velocityX,
        double velocityY,
        nint targetWindowHandle)
    {
        _walking = false;
        _paused = true;
        SurfaceLaunchRequested?.Invoke(
            velocityX,
            velocityY,
            targetWindowHandle);
    }

    private void SurfaceController_DirectionChanged(int direction)
    {
        _direction = direction < 0 ? -1 : 1;
    }

    private void SurfaceController_DecisionDelayRequested(
        double minSeconds,
        double maxSeconds)
    {
        ScheduleNextDecision(minSeconds, maxSeconds);
    }

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

    private enum AmbientAction
    {
        Idle,
        Walk,
        Look,
        Twitch,
        CuriousPause,
        HappyPause
    }


    private AmbientAction _lastAmbientAction =
        AmbientAction.Idle;


    private int _ambientRepeatCount;

    private AmbientAction SelectAmbientAction(
    AmbientContext context,
    bool userActive,
    DesktopApplicationKind appKind)
    {
        AmbientAction selected =
            AmbientAction.Idle;


        for (int attempt = 0;
             attempt < 3;
             attempt++)
        {
            double roll =
                _random.NextDouble();


            // =================================
            // USER SEDANG AKTIF BEKERJA
            // =================================

            if (userActive)
            {
                if (roll < 0.18)
                {
                    selected =
                        AmbientAction.Walk;
                }
                else if (roll < 0.45)
                {
                    selected =
                        AmbientAction.Look;
                }
                else if (roll < 0.60)
                {
                    selected =
                        AmbientAction.Twitch;
                }
                else if (roll < 0.76)
                {
                    selected =
                        AmbientAction.CuriousPause;
                }
                else if (roll < 0.82)
                {
                    selected =
                        AmbientAction.HappyPause;
                }
                else
                {
                    selected =
                        AmbientAction.Idle;
                }
            }

            // =================================
            // BERDIRI DI ATAS APP WINDOW
            // =================================

            else if (context ==
                AmbientContext.ApplicationWindow)
            {
                selected =
                    SelectApplicationAction(
                        roll,
                        appKind);
            }

            // =================================
            // DESKTOP NORMAL
            // =================================

            else
            {
                if (roll < 0.32)
                {
                    selected =
                        AmbientAction.Walk;
                }
                else if (roll < 0.58)
                {
                    selected =
                        AmbientAction.Look;
                }
                else if (roll < 0.72)
                {
                    selected =
                        AmbientAction.Twitch;
                }
                else if (roll < 0.84)
                {
                    selected =
                        AmbientAction.CuriousPause;
                }
                else if (roll < 0.92)
                {
                    selected =
                        AmbientAction.HappyPause;
                }
                else
                {
                    selected =
                        AmbientAction.Idle;
                }
            }

            if (selected !=
                _lastAmbientAction)
            {
                break;
            }

            if (_random.NextDouble() <
                0.22)
            {
                break;
            }
        }


        return selected;
    }

    private void RememberAmbientAction(
    AmbientAction action)
    {
        if (action ==
            _lastAmbientAction)
        {
            _ambientRepeatCount++;
        }
        else
        {
            _ambientRepeatCount = 0;
        }


        _lastAmbientAction =
            action;
    }

    private enum AmbientContext
    {
        Desktop,
        ApplicationWindow
    }

    private DesktopApplicationKind
        GetSupportedApplicationKind()
    {
        if (!_surfaceController.HasSupport)
        {
            return
                DesktopApplicationKind.Unknown;
        }


        if (!DesktopApplicationService
            .TryGetApplication(
                _surfaceController
                    .SupportWindowHandle,
                out DesktopApplicationContext app))
        {
            return
                DesktopApplicationKind.Unknown;
        }


        return app.Kind;
    }


    private AmbientAction
        SelectApplicationAction(
            double roll,
            DesktopApplicationKind appKind)
    {
        switch (appKind)
        {
            // =================================
            // VS CODE / IDE
            // lebih observatif & penasaran
            // =================================

            case DesktopApplicationKind.CodeEditor:

                if (roll < 0.14)
                    return AmbientAction.Walk;

                if (roll < 0.42)
                    return AmbientAction.Look;

                if (roll < 0.54)
                    return AmbientAction.Twitch;

                if (roll < 0.78)
                    return AmbientAction.CuriousPause;

                if (roll < 0.84)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;


            // =================================
            // BROWSER
            // aktif melihat-lihat
            // =================================

            case DesktopApplicationKind.Browser:

                if (roll < 0.22)
                    return AmbientAction.Walk;

                if (roll < 0.52)
                    return AmbientAction.Look;

                if (roll < 0.64)
                    return AmbientAction.Twitch;

                if (roll < 0.82)
                    return AmbientAction.CuriousPause;

                if (roll < 0.90)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;


            // =================================
            // PHOTOSHOP / CREATIVE
            // penasaran + happy
            // =================================

            case DesktopApplicationKind.Creative:

                if (roll < 0.16)
                    return AmbientAction.Walk;

                if (roll < 0.42)
                    return AmbientAction.Look;

                if (roll < 0.52)
                    return AmbientAction.Twitch;

                if (roll < 0.74)
                    return AmbientAction.CuriousPause;

                if (roll < 0.88)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;


            // =================================
            // WORD / EXCEL / PPT
            // lebih tenang
            // =================================

            case DesktopApplicationKind.Office:

                if (roll < 0.12)
                    return AmbientAction.Walk;

                if (roll < 0.40)
                    return AmbientAction.Look;

                if (roll < 0.52)
                    return AmbientAction.Twitch;

                if (roll < 0.68)
                    return AmbientAction.CuriousPause;

                if (roll < 0.74)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;


            // =================================
            // EXPLORER
            // suka eksplor
            // =================================

            case DesktopApplicationKind.FileManager:

                if (roll < 0.28)
                    return AmbientAction.Walk;

                if (roll < 0.55)
                    return AmbientAction.Look;

                if (roll < 0.67)
                    return AmbientAction.Twitch;

                if (roll < 0.82)
                    return AmbientAction.CuriousPause;

                if (roll < 0.88)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;


            // =================================
            // DISCORD / TEAMS / ETC
            // lebih sosial
            // =================================

            case DesktopApplicationKind.Communication:

                if (roll < 0.14)
                    return AmbientAction.Walk;

                if (roll < 0.36)
                    return AmbientAction.Look;

                if (roll < 0.48)
                    return AmbientAction.Twitch;

                if (roll < 0.68)
                    return AmbientAction.CuriousPause;

                if (roll < 0.88)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;


            // =================================
            // UNKNOWN APPLICATION
            // =================================

            default:

                if (roll < 0.24)
                    return AmbientAction.Walk;

                if (roll < 0.52)
                    return AmbientAction.Look;

                if (roll < 0.65)
                    return AmbientAction.Twitch;

                if (roll < 0.83)
                    return AmbientAction.CuriousPause;

                if (roll < 0.90)
                    return AmbientAction.HappyPause;

                return AmbientAction.Idle;
        }
    }

    private AmbientContext GetAmbientContext()
    {
        return _surfaceController.HasSupport
            ? AmbientContext.ApplicationWindow
            : AmbientContext.Desktop;
    }
    public BehaviorController(
        Window window,
        CharacterView character)
    {
        _window = window;
        _character = character;

        _surfaceController =
            new SurfaceBehaviorController(window, character, _random);

        _surfaceController.SupportLost +=
            SurfaceController_SupportLost;

        _surfaceController.SurfaceLaunchRequested +=
            SurfaceController_SurfaceLaunchRequested;

        _surfaceController.DirectionChanged +=
            SurfaceController_DirectionChanged;

        _surfaceController.DecisionDelayRequested +=
            SurfaceController_DecisionDelayRequested;

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

        ScheduleNextSleepCycle();

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

        if (_surfaceController.HasSupport)
        {
            if (!_surfaceController.UpdateSupportWindow())
            {
                return;
            }
        }
        else
        {
            PlaceOnDesktopBottom();
        }

        _surfaceController.RestoreCharacterState(_direction);

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


        // Window di bawah Lu-Knight bisa bergerak
        // bahkan saat chat sedang terbuka.
        if (_surfaceController.HasSupport &&
            !_surfaceController.UpdateSupportWindow())
        {
            return;
        }


        if (_paused)
            return;

        deltaSeconds =
            Math.Clamp(
                deltaSeconds,
                0,
                0.1);

        if (_surfaceController.Update(now, _thinking))
        {
            if (now >= _nextBlinkAt)
            {
                _character.Blink();

                ScheduleNextBlink();
            }

            return;
        }

        UpdateMood(now);

        bool cursorHasAttention =
            UpdateCursorAwareness(now);

        // =========================
        // AUTO WAKE
        // =========================

        if (_sleeping)
        {
            if (now >= _sleepUntil)
            {
                WakeUp();
            }
            else
            {
                return;
            }
        }

        // =========================
        // AUTO NAP
        // =========================

        if (!_thinking &&
            !_surfaceController.IsBusy &&
            now - _lastInteractionAt >= _sleepAfter)
        {
            EnterSleep();
            return;
        }

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


        DateTime now =
            DateTime.UtcNow;


        AmbientContext context =
            GetAmbientContext();


        bool userActive =
            DesktopActivityService
                .IsUserActive(
                    thresholdSeconds: 8);


        DesktopApplicationKind appKind =
            context ==
                AmbientContext.ApplicationWindow
                ? GetSupportedApplicationKind()
                : DesktopApplicationKind.Unknown;


        AmbientAction action =
            SelectAmbientAction(
                context,
                userActive,
                appKind);


        RememberAmbientAction(
            action);


        switch (action)
        {
            // =========================
            // WALK
            // =========================

            case AmbientAction.Walk:

                StartWalking();

                break;


            // =========================
            // LOOK AROUND
            // =========================

            case AmbientAction.Look:
                {
                    _character.SetState(
                        CharacterState.Idle);


                    int lookDirection =
                        _random.Next(0, 2) == 0
                            ? -1
                            : 1;


                    _character.SetFacingDirection(
                        _direction);


                    _character.LookSide(
                        lookDirection);


                    if (_random.NextDouble() <
                        0.22)
                    {
                        _character.TwitchEars();
                    }


                    ScheduleNextDecision(
                        1.4,
                        3.0);

                    break;
                }


            // =========================
            // EAR TWITCH
            // =========================

            case AmbientAction.Twitch:

                _character.SetState(
                    CharacterState.Idle);


                _character.TwitchEars();


                ScheduleNextDecision(
                    1.0,
                    2.4);

                break;


            // =========================
            // CURIOUS
            // =========================

            case AmbientAction.CuriousPause:
                {
                    _character.SetState(
                        CharacterState.Idle);


                    SetTemporaryMood(
                        CharacterMood.Curious,
                        1.2);


                    int direction =
                        _random.Next(0, 2) == 0
                            ? -1
                            : 1;


                    _character.LookSide(
                        direction);


                    if (_random.NextDouble() <
                        0.45)
                    {
                        _character.TwitchEars();
                    }


                    ScheduleNextDecision(
                        1.6,
                        3.2);

                    break;
                }


            // =========================
            // HAPPY
            // =========================

            case AmbientAction.HappyPause:

                _character.SetState(
                    CharacterState.Idle);


                SetTemporaryMood(
                    CharacterMood.Happy,
                    0.75);


                if (_random.NextDouble() <
                    0.35)
                {
                    _character.TwitchEars();
                }


                ScheduleNextDecision(
                    1.6,
                    3.0);

                break;


            // =========================
            // DO NOTHING
            // =========================

            default:

                _character.SetState(
                    CharacterState.Idle);


                _character.SetFacingDirection(
                    _direction);


                ScheduleNextDecision(
                    2.0,
                    4.5);

                break;
        }
    }


    private void StartWalking()
    {
        _walking = true;

        // Lebih sering lanjut ke arah sebelumnya.
        // Kadang baru berubah pikiran.
        if (_random.NextDouble() <
            0.35)
        {
            _direction *= -1;
        }

        _character.SetState(
            CharacterState.Walk);

        _character.SetFacingDirection(
            _direction);

        _character.LookSide(
            _direction);

        if (_surfaceController.HasSupport)
        {
            // Di atas application window
            // lebih hati-hati dan jalan pendek.
            ScheduleNextDecision(
                0.7,
                2.2);

            return;
        }

        double walkStyle =
         _random.NextDouble();


        if (walkStyle < 0.25)
        {
            // Jalan pendek / seperti pindah posisi.
            ScheduleNextDecision(
                0.8,
                1.6);
        }
        else if (walkStyle < 0.85)
        {
            // Jalan normal.
            ScheduleNextDecision(
                1.8,
                3.8);
        }
        else
        {
            // Sesekali jalan agak jauh.
            ScheduleNextDecision(
                3.8,
                5.8);
        }
    }

    private void StopWalking()
    {
        _walking = false;

        _character.SetState(
            CharacterState.Idle);

        _character.SetFacingDirection(
            _direction);

        double reaction =
            _random.NextDouble();


        if (reaction < 0.18)
        {
            _character.TwitchEars();
        }
        else if (reaction < 0.30)
        {
            _character.LookSide(
                _direction);
        }
        else if (reaction < 0.36)
        {
            SetTemporaryMood(
                CharacterMood.Happy,
                0.65);
        }

        if (DesktopActivityService
            .IsUserActive(8))
        {
            ScheduleNextDecision(
                2.0,
                5.0);
        }
        else
        {
            ScheduleNextDecision(
                1.0,
                4.0);
        }
    }

    private void MoveCharacter(
    double deltaSeconds)
    {
        // ====================================
        // WALKING ON APPLICATION WINDOW
        // ====================================

        if (_surfaceController.HasSupport)
        {
            _surfaceController.MoveOnSupportWindow(
                deltaSeconds,
                _direction,
                WalkSpeed);

            if (_surfaceController.IsBusy)
            {
                _walking = false;
            }

            return;
        }


        // ====================================
        // WALKING ON DESKTOP / TASKBAR
        // ====================================

        Rect windowBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);

        DesktopMonitorInfo monitor =
            DesktopMonitorService
                .GetMonitorForWindow(
                    _window);


        double width =
            windowBounds.Width;


        double newLeft =
            windowBounds.Left +
            (_direction *
             WalkSpeed *
             deltaSeconds);


        double newTop =
            monitor.WorkArea.Bottom -
            windowBounds.Height;


        double minLeft =
            monitor.WorkArea.Left;

        double maxLeft =
            monitor.WorkArea.Right -
            width;


        if (newLeft <= minLeft)
        {
            bool taskbarBlocks =
                monitor.HasTaskbarLeft;

            bool hasNeighbor =
                DesktopMonitorService
                    .TryGetNeighbor(
                        monitor,
                        MonitorDirection.Left,
                        out _);


            if (taskbarBlocks ||
                !hasNeighbor)
            {
                newLeft =
                    minLeft;

                _direction = 1;

                _character
                    .SetFacingDirection(
                        _direction);
            }
        }
        else if (newLeft >= maxLeft)
        {
            bool taskbarBlocks =
                monitor.HasTaskbarRight;

            bool hasNeighbor =
                DesktopMonitorService
                    .TryGetNeighbor(
                        monitor,
                        MonitorDirection.Right,
                        out _);


            if (taskbarBlocks ||
                !hasNeighbor)
            {
                newLeft =
                    maxLeft;

                _direction = -1;

                _character
                    .SetFacingDirection(
                        _direction);
            }
        }


        DesktopMonitorService
            .SetWindowPosition(
                _window,
                newLeft,
                newTop);
    }
    private void PlaceOnDesktopBottom()
    {
        Rect windowBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);

        DesktopMonitorInfo monitor =
            DesktopMonitorService
                .GetMonitorForWindow(
                    _window);

        Rect workArea =
            monitor.WorkArea;


        double maxLeft =
            Math.Max(
                workArea.Left,
                workArea.Right -
                windowBounds.Width);


        double left =
            Math.Clamp(
                windowBounds.Left,
                workArea.Left,
                maxLeft);


        double top =
            workArea.Bottom -
            windowBounds.Height;


        DesktopMonitorService
            .SetWindowPosition(
                _window,
                left,
                top);
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
        _surfaceController.SupportLost -=
            SurfaceController_SupportLost;

        _surfaceController.SurfaceLaunchRequested -=
            SurfaceController_SurfaceLaunchRequested;

        _surfaceController.DirectionChanged -=
            SurfaceController_DirectionChanged;

        _surfaceController.DecisionDelayRequested -=
            SurfaceController_DecisionDelayRequested;

        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void ScheduleNextSleepCycle()
    {
        double seconds =
            MinimumAwakeBeforeNapSeconds +
            (_random.NextDouble() *
             (MaximumAwakeBeforeNapSeconds - MinimumAwakeBeforeNapSeconds));

        _sleepAfter = TimeSpan.FromSeconds(seconds);
    }

    private void EnterSleep()
    {
        if (_sleeping || _thinking || _surfaceController.IsBusy)
        {
            return;
        }

        _walking = false;
        _sleeping = true;

        double napSeconds =
            MinimumNapSeconds +
            (_random.NextDouble() * (MaximumNapSeconds - MinimumNapSeconds));

        _sleepUntil = DateTime.UtcNow.AddSeconds(napSeconds);

        _character.SetMood(CharacterMood.Neutral);
        _character.SetState(CharacterState.Sleep);
        _character.SetFacingDirection(_direction);
    }

    private void WakeUp()
    {
        if (!_sleeping)
            return;

        _sleeping = false;
        _sleepUntil = DateTime.MinValue;

        // Bangun memulai siklus aktif baru.
        DateTime now = DateTime.UtcNow;
        _lastInteractionAt = now;
        ScheduleNextSleepCycle();

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(_direction);
        _character.TwitchEars();

        ScheduleNextDecision(0.8, 2.0);
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
