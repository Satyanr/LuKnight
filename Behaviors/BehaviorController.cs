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

    private nint _supportWindowHandle =
    nint.Zero;

    private Rect _lastSupportWindowBounds =
        Rect.Empty;

    private enum SurfaceAction
    {
        None,

        EdgePause,
        Peeking,

        Hanging,

        SideClimbingDown,
        SideHolding,
        SideClimbingUp,

        ClimbingUp
    }

    private DateTime _sideClimbStartedAt =
    DateTime.MinValue;

    private double _sideGripOffsetY;

    private double _sideClimbStartOffsetY;
    private double _sideClimbTargetOffsetY;


    private static readonly TimeSpan
        SideClimbDuration =
            TimeSpan.FromMilliseconds(850);

    private SurfaceAction _surfaceAction =
    SurfaceAction.None;

    private int _surfaceEdgeDirection = 1;

    private DateTime _surfaceActionUntil =
        DateTime.MinValue;

    private DateTime _climbStartedAt =
        DateTime.MinValue;


    private const double HangGripOffsetY =
        90.0;


    private static readonly TimeSpan
        ClimbDuration =
            TimeSpan.FromMilliseconds(650);

    public event Action? SupportLost;

    public event Action<
        double,
        double>? SurfaceLaunchRequested;

    public void SetSupportWindow(
    nint? windowHandle)
    {
        _surfaceAction =
            SurfaceAction.None;

        _surfaceActionUntil =
            DateTime.MinValue;

        _sideGripOffsetY = 0;

        _sideClimbStartOffsetY = 0;
        _sideClimbTargetOffsetY = 0;

        _supportWindowHandle =
            windowHandle ??
            nint.Zero;

        _lastSupportWindowBounds =
            Rect.Empty;

        if (_supportWindowHandle ==
            nint.Zero)
        {
            return;
        }


        if (DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo info))
        {
            _lastSupportWindowBounds =
                info.Bounds;
        }
    }


    public void ClearSupportWindow()
    {
        _surfaceAction =
            SurfaceAction.None;

        _surfaceActionUntil =
            DateTime.MinValue;

        _sideGripOffsetY = 0;

        _sideClimbStartOffsetY = 0;
        _sideClimbTargetOffsetY = 0;

        _supportWindowHandle =
            nint.Zero;

        _lastSupportWindowBounds =
            Rect.Empty;

    }

    private double GetHangingLeft(
    Rect supportBounds,
    double characterWidth)
    {
        const double overhang = 18;


        if (_surfaceEdgeDirection < 0)
        {
            return supportBounds.Left -
                   overhang;
        }


        return supportBounds.Right -
               characterWidth +
               overhang;
    }

    private void BeginEdgePause(
    int edgeDirection)
    {
        _walking = false;

        _surfaceEdgeDirection =
            edgeDirection < 0
                ? -1
                : 1;


        _surfaceAction =
            SurfaceAction.EdgePause;


        _surfaceActionUntil =
            DateTime.UtcNow
                .AddMilliseconds(550);


        _character.SetState(
            CharacterState.Idle);


        _character.SetFacingDirection(
            _surfaceEdgeDirection);


        _character.SetMood(
            CharacterMood.Curious);


        _character.LookDown();
    }

    private void BeginPeeking(
    DateTime now)
    {
        _surfaceAction =
            SurfaceAction.Peeking;


        _surfaceActionUntil =
            now.AddMilliseconds(950);


        _character.SetState(
            CharacterState.Idle);


        _character.SetMood(
            CharacterMood.Curious);


        _character.PlayEdgePeek(
            _surfaceEdgeDirection);
    }

    private void BeginHanging(
    DateTime now)
    {
        _walking = false;


        _surfaceAction =
            SurfaceAction.Hanging;


        _surfaceActionUntil =
            now.AddSeconds(
                1.5 +
                (_random.NextDouble() *
                 1.8));

        _sideGripOffsetY = 0;

        _character.SetState(
            CharacterState.Hanging);


        _character.SetMood(
            CharacterMood.Curious);


        UpdateSupportWindow();
    }

    private void BeginSideClimbDown(
    DateTime now)
    {
        if (!DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo support))
        {
            LoseWindowSupport();
            return;
        }


        // Seberapa jauh boleh turun.
        double availableDistance =
            Math.Max(
                0,
                support.Bounds.Height - 90);


        double targetDistance =
            Math.Min(
                160,
                availableDistance);


        // Window terlalu pendek.
        if (targetDistance < 50)
        {
            BeginClimbingUp(now);
            return;
        }


        _surfaceAction =
            SurfaceAction.SideClimbingDown;


        _sideClimbStartedAt = now;

        _sideClimbStartOffsetY =
            _sideGripOffsetY;

        _sideClimbTargetOffsetY =
            targetDistance;


        _character.SetState(
            CharacterState.Climbing);


        _character.SetMood(
            CharacterMood.Curious);


        _character.SetFacingDirection(
            _surfaceEdgeDirection);
    }

    private void BeginSideHold(
    DateTime now)
    {
        _surfaceAction =
            SurfaceAction.SideHolding;


        _surfaceActionUntil =
            now.AddSeconds(
                0.8 +
                (_random.NextDouble() *
                 1.2));


        _character.SetState(
            CharacterState.Hanging);


        _character.SetMood(
            CharacterMood.Curious);
    }

    private void BeginSideClimbUp(
    DateTime now)
    {
        _surfaceAction =
            SurfaceAction.SideClimbingUp;


        _sideClimbStartedAt = now;

        _sideClimbStartOffsetY =
            _sideGripOffsetY;

        _sideClimbTargetOffsetY =
            0;


        _character.SetState(
            CharacterState.Climbing);


        _character.SetMood(
            CharacterMood.Curious);


        _character.SetFacingDirection(
            _surfaceEdgeDirection);
    }

    private void UpdateSideClimb(
    DateTime now,
    bool climbingUp)
    {
        if (!DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo support))
        {
            LoseWindowSupport();
            return;
        }


        double progress =
            (now - _sideClimbStartedAt)
            .TotalMilliseconds /
            SideClimbDuration
                .TotalMilliseconds;


        progress =
            Math.Clamp(
                progress,
                0,
                1);


        // smoothstep
        double eased =
            progress *
            progress *
            (3 - (2 * progress));


        _sideGripOffsetY =
            Lerp(
                _sideClimbStartOffsetY,
                _sideClimbTargetOffsetY,
                eased);


        Rect characterBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);


        double left =
            GetHangingLeft(
                support.Bounds,
                characterBounds.Width);


        double top =
            support.Bounds.Top +
            _sideGripOffsetY -
            HangGripOffsetY;


        DesktopMonitorService
            .SetWindowPosition(
                _window,
                left,
                top);


        _lastSupportWindowBounds =
            support.Bounds;


        if (progress < 1)
            return;


        if (climbingUp)
        {
            _sideGripOffsetY = 0;

            BeginClimbingUp(now);
        }
        else
        {
            BeginSideHold(now);
        }
    }

    private void BeginClimbingUp(
    DateTime now)
    {
        _surfaceAction =
            SurfaceAction.ClimbingUp;


        _climbStartedAt = now;


        _character.SetState(
            CharacterState.Climbing);


        _character.SetMood(
            CharacterMood.Curious);
    }

    private void UpdateClimbingUp(
    DateTime now)
    {
        if (!DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo support))
        {
            LoseWindowSupport();
            return;
        }


        Rect characterBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);


        double progress =
            (now - _climbStartedAt)
            .TotalMilliseconds /
            ClimbDuration.TotalMilliseconds;


        progress =
            Math.Clamp(
                progress,
                0,
                1);


        // ease-out
        double eased =
            1 -
            Math.Pow(
                1 - progress,
                3);


        double hangingLeft =
            GetHangingLeft(
                support.Bounds,
                characterBounds.Width);


        double standingLeft =
            _surfaceEdgeDirection < 0
                ? support.Bounds.Left
                : support.Bounds.Right -
                  characterBounds.Width;


        double hangingTop =
            support.Bounds.Top -
            HangGripOffsetY;


        double standingTop =
            support.Bounds.Top -
            characterBounds.Height;


        double left =
            Lerp(
                hangingLeft,
                standingLeft,
                eased);


        double top =
            Lerp(
                hangingTop,
                standingTop,
                eased);


        DesktopMonitorService
            .SetWindowPosition(
                _window,
                left,
                top);


        _lastSupportWindowBounds =
            support.Bounds;


        if (progress < 1)
            return;


        _surfaceAction =
            SurfaceAction.None;


        // setelah naik, menghadap ke dalam
        _direction =
            -_surfaceEdgeDirection;


        _character.SetState(
            CharacterState.Idle);


        _character.SetFacingDirection(
            _direction);


        if (!_thinking)
        {
            _character.SetMood(
                CharacterMood.Neutral);
        }


        ScheduleNextDecision(
            0.6,
            1.4);
    }

    private static double Lerp(
    double from,
    double to,
    double amount)
    {
        return from +
               ((to - from) *
                amount);
    }

    private bool UpdateSurfaceAction(
    DateTime now)
    {
        switch (_surfaceAction)
        {
            case SurfaceAction.None:

                return false;


            case SurfaceAction.EdgePause:

                if (now <
                    _surfaceActionUntil)
                {
                    return true;
                }


                double choice =
                    _random.NextDouble();


                // 55% balik
                if (choice < 0.55)
                {
                    TurnBackFromEdge();
                    return true;
                }


                // 30% mengintip
                if (choice < 0.85)
                {
                    BeginPeeking(now);
                    return true;
                }


                // 15% bergantung
                BeginHanging(now);

                return true;


            case SurfaceAction.Peeking:

                if (now <
                    _surfaceActionUntil)
                {
                    return true;
                }


                // Setelah peek masih ada
                // kemungkinan nekat hanging.
                if (_random.NextDouble() <
                    0.25)
                {
                    BeginHanging(now);
                }
                else
                {
                    TurnBackFromEdge();
                }

                return true;


            case SurfaceAction.Hanging:

                if (now <
                    _surfaceActionUntil)
                {
                    return true;
                }


                double hangChoice =
                    _random.NextDouble();


                // 55% naik kembali
                if (hangChoice < 0.55)
                {
                    BeginClimbingUp(now);

                    return true;
                }


                // 25% menjelajah sisi window
                if (hangChoice < 0.80)
                {
                    BeginSideClimbDown(now);

                    return true;
                }


                // 20% nekat melepas pegangan
                ReleaseFromSurface(
                    _surfaceEdgeDirection *
                    220.0,

                    80.0);

                return true;

            case SurfaceAction.SideClimbingDown:

                UpdateSideClimb(
                    now,
                    climbingUp: false);

                return true;


            case SurfaceAction.SideHolding:

                if (now <
                    _surfaceActionUntil)
                {
                    return true;
                }


                // 70% naik kembali
                if (_random.NextDouble() <
                    0.70)
                {
                    BeginSideClimbUp(now);
                }
                else
                {
                    // 30% melepas diri dari sisi
                    ReleaseFromSurface(
                        _surfaceEdgeDirection *
                        180.0,

                        100.0);
                }

                return true;


            case SurfaceAction.SideClimbingUp:

                UpdateSideClimb(
                    now,
                    climbingUp: true);

                return true;

            case SurfaceAction.ClimbingUp:

                UpdateClimbingUp(now);

                return true;


            default:

                return false;
        }
    }

    private void TurnBackFromEdge()
    {
        _surfaceAction =
            SurfaceAction.None;


        _direction =
            -_surfaceEdgeDirection;


        _character.SetState(
            CharacterState.Idle);


        _character.SetFacingDirection(
            _direction);


        if (!_thinking)
        {
            _character.SetMood(
                CharacterMood.Neutral);
        }


        ScheduleNextDecision(
            0.4,
            1.0);
    }

    private bool UpdateSupportWindow()
    {
        if (_supportWindowHandle ==
            nint.Zero)
        {
            return true;
        }


        if (!DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo support))
        {
            LoseWindowSupport();
            return false;
        }


        if (_surfaceAction ==
                SurfaceAction.ClimbingUp
            ||
            _surfaceAction ==
                SurfaceAction.SideClimbingDown
            ||
            _surfaceAction ==
                SurfaceAction.SideClimbingUp)
        {
            // Posisi selama climbing dikontrol
            // oleh update animasi climbing.
            return true;
        }

        Rect characterBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);

        double left;


        if (_surfaceAction ==
            SurfaceAction.Hanging)
        {
            left =
                GetHangingLeft(
                    support.Bounds,
                    characterBounds.Width);
        }
        else
        {
            double deltaX = 0;


            if (!_lastSupportWindowBounds.IsEmpty)
            {
                deltaX =
                    support.Bounds.Left -
                    _lastSupportWindowBounds.Left;
            }


            left =
                characterBounds.Left +
                deltaX;


            double minimumLeft =
                support.Bounds.Left;

            double maximumLeft =
                Math.Max(
                    minimumLeft,
                    support.Bounds.Right -
                    characterBounds.Width);


            left =
                Math.Clamp(
                    left,
                    minimumLeft,
                    maximumLeft);
        }


                bool isSideAttached =
                        _surfaceAction ==
                                SurfaceAction.Hanging
                        ||
                        _surfaceAction ==
                                SurfaceAction.SideHolding;


                double top =
                        isSideAttached
                                ? support.Bounds.Top +
                                    _sideGripOffsetY -
                                    HangGripOffsetY
                                : support.Bounds.Top -
                                    characterBounds.Height;


        DesktopMonitorService
            .SetWindowPosition(
                _window,
                left,
                top);


        _lastSupportWindowBounds =
            support.Bounds;

        return true;
    }

    private void ReleaseFromSurface(
    double horizontalVelocity,
    double verticalVelocity)
    {
        if (_supportWindowHandle ==
            nint.Zero)
        {
            return;
        }


        _supportWindowHandle =
            nint.Zero;

        _lastSupportWindowBounds =
            Rect.Empty;


        _surfaceAction =
            SurfaceAction.None;

        _surfaceActionUntil =
            DateTime.MinValue;


        _sideGripOffsetY = 0;

        _walking = false;


        // Behavior berhenti sementara,
        // physics mengambil alih.
        _paused = true;


        _character.SetMood(
            CharacterMood.Surprised);


        SurfaceLaunchRequested?.Invoke(
            horizontalVelocity,
            verticalVelocity);
    }

    private void LoseWindowSupport()
    {
        _surfaceAction =
            SurfaceAction.None;

        _surfaceActionUntil =
            DateTime.MinValue;

        _sideGripOffsetY = 0;

        _sideClimbStartOffsetY = 0;
        _sideClimbTargetOffsetY = 0;
        if (_supportWindowHandle ==
            nint.Zero)
        {
            return;
        }


        _supportWindowHandle =
            nint.Zero;

        _lastSupportWindowBounds =
            Rect.Empty;

        _walking = false;

        // Physics sekarang mengambil alih.
        _paused = true;

        SupportLost?.Invoke();
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

        if (_supportWindowHandle !=
            nint.Zero)
        {
            if (!UpdateSupportWindow())
                return;
        }
        else
        {
            PlaceOnDesktopBottom();
        }

        if (_surfaceAction ==
                SurfaceAction.Hanging
            ||
            _surfaceAction ==
                SurfaceAction.SideHolding)
        {
            _character.SetState(
                CharacterState.Hanging);
        }
        else if (_surfaceAction ==
                     SurfaceAction.ClimbingUp
                 ||
                 _surfaceAction ==
                     SurfaceAction.SideClimbingDown
                 ||
                 _surfaceAction ==
                     SurfaceAction.SideClimbingUp)
        {
            _character.SetState(
                CharacterState.Climbing);
        }
        else
        {
            _character.SetState(
                CharacterState.Idle);
        }

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


        // Window di bawah Lu-Knight bisa bergerak
        // bahkan saat chat sedang terbuka.
        if (_supportWindowHandle !=
                nint.Zero &&
            !UpdateSupportWindow())
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

        if (UpdateSurfaceAction(now))
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

    private void MoveOnSupportWindow(
    double deltaSeconds)
    {
        if (_surfaceAction !=
            SurfaceAction.None)
        {
            return;
        }

        if (!DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo support))
        {
            LoseWindowSupport();
            return;
        }


        Rect characterBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);


        double minimumLeft =
            support.Bounds.Left;

        double maximumLeft =
            Math.Max(
                minimumLeft,
                support.Bounds.Right -
                characterBounds.Width);


        double newLeft =
            characterBounds.Left +
            (_direction *
             WalkSpeed *
             deltaSeconds);

        double supportTop =
            support.Bounds.Top -
            characterBounds.Height;


        // Phase 3D-A:
        // sementara Lu-Knight berbalik
        // di tepi window.
        if (newLeft <= minimumLeft)
        {
            newLeft =
                minimumLeft;


            DesktopMonitorService
                .SetWindowPosition(
                    _window,
                    newLeft,
                    supportTop);


            BeginEdgePause(-1);

            return;
        }
        else if (newLeft >= maximumLeft)
        {
            newLeft =
                maximumLeft;


            DesktopMonitorService
                .SetWindowPosition(
                    _window,
                    newLeft,
                    supportTop);


            BeginEdgePause(1);

            return;
        }

        DesktopMonitorService
            .SetWindowPosition(
                _window,
                newLeft,
                supportTop);


        _lastSupportWindowBounds =
            support.Bounds;
    }

    private void MoveCharacter(
    double deltaSeconds)
    {
        // ====================================
        // WALKING ON APPLICATION WINDOW
        // ====================================

        if (_supportWindowHandle !=
            nint.Zero)
        {
            MoveOnSupportWindow(
                deltaSeconds);

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