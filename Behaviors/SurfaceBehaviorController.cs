using System;
using System.Windows;
using LuKnight.Services;
using LuKnight.Views;

namespace LuKnight.Behaviors;

public sealed class SurfaceBehaviorController
{
    private readonly Window _window;
    private readonly CharacterView _character;
    private readonly Random _random;


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

        JumpPreparing,

        ClimbingUp
    }


    private SurfaceAction _surfaceAction =
        SurfaceAction.None;


    private int _surfaceEdgeDirection = 1;

    private DateTime _surfaceActionUntil =
        DateTime.MinValue;

    private DateTime _climbStartedAt =
        DateTime.MinValue;

    private DateTime _sideClimbStartedAt =
        DateTime.MinValue;


    private double _sideGripOffsetY;

    private double _sideClimbStartOffsetY;
    private double _sideClimbTargetOffsetY;


    private double _pendingJumpVelocityX;
    private double _pendingJumpVelocityY;

    private nint _pendingJumpTargetHandle =
        nint.Zero;


    private const double HangGripOffsetY =
        90.0;


    private static readonly TimeSpan
        ClimbDuration =
            TimeSpan.FromMilliseconds(650);


    private static readonly TimeSpan
        SideClimbDuration =
            TimeSpan.FromMilliseconds(850);


    public bool HasSupport =>
        _supportWindowHandle !=
        nint.Zero;

    public nint SupportWindowHandle =>
        _supportWindowHandle;


    public bool IsBusy =>
        _surfaceAction !=
        SurfaceAction.None;


    public event Action? SupportLost;


    public event Action<
        double,
        double,
        nint>? SurfaceLaunchRequested;


    public event Action<int>?
        DirectionChanged;


    public event Action<
        double,
        double>? DecisionDelayRequested;


    public SurfaceBehaviorController(
        Window window,
        CharacterView character,
        Random random)
    {
        _window = window;
        _character = character;
        _random = random;
    }

    public void SetSupportWindow(
    nint? windowHandle)
    {
        _surfaceAction =
            SurfaceAction.None;

        _surfaceActionUntil =
            DateTime.MinValue;

        _sideGripOffsetY = 0;

        _pendingJumpVelocityX = 0;
        _pendingJumpVelocityY = 0;
        _pendingJumpTargetHandle = nint.Zero;

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

        _pendingJumpVelocityX = 0;
        _pendingJumpVelocityY = 0;
        _pendingJumpTargetHandle = nint.Zero;

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
    DateTime now,
    bool thinking)
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
        int newDirection =
            -_surfaceEdgeDirection;


        _character.SetState(
            CharacterState.Idle);


        _character.SetFacingDirection(
            newDirection);


        if (!thinking)
        {
            _character.SetMood(
                CharacterMood.Neutral);
        }


        DirectionChanged?.Invoke(newDirection);

        DecisionDelayRequested?.Invoke(
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

    private bool BeginTargetJump(
        DateTime now)
    {
        if (_supportWindowHandle ==
                nint.Zero ||
            !DesktopWindowService.TryGetWindow(
                _supportWindowHandle,
                out DesktopWindowInfo support))
        {
            return false;
        }

        Rect characterBounds =
            DesktopMonitorService
                .GetWindowBounds(
                    _window);

        if (!SurfaceNavigationService
            .TryPlanJump(
                _supportWindowHandle,
                // JumpPreparing memakai anchor sisi, termasuk jump dari edge.
                new Rect(
                    GetHangingLeft(support.Bounds, characterBounds.Width),
                    support.Bounds.Top + _sideGripOffsetY - HangGripOffsetY,
                    characterBounds.Width,
                    characterBounds.Height),
                _surfaceEdgeDirection,
                out SurfaceJumpPlan plan))
        {
            return false;
        }

        _pendingJumpVelocityX =
            plan.VelocityX;

        _pendingJumpVelocityY =
            plan.VelocityY;

        _pendingJumpTargetHandle =
            plan.Target.Handle;

        _surfaceAction =
            SurfaceAction.JumpPreparing;

        _surfaceActionUntil =
            now.AddMilliseconds(430);

        int lookDirection =
            plan.VelocityX < 0
                ? -1
                : 1;

        _character.SetState(
            CharacterState.Hanging);

        _character.SetMood(
            CharacterMood.Curious);

        _character.SetFacingDirection(
            lookDirection);

        _character.LookSide(
            lookDirection);

        return true;
    }

    public bool Update(
    DateTime now,
    bool thinking)
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


                // Coba langsung melompat jika ada window tujuan yang sesuai.
                if (_random.NextDouble() < 0.35 &&
                    BeginTargetJump(now))
                {
                    return true;
                }

                double choice =
                    _random.NextDouble();


                // 45% balik
                if (choice < 0.45)
                {
                    TurnBackFromEdge(thinking);
                    return true;
                }


                // 35% mengintip
                if (choice < 0.80)
                {
                    BeginPeeking(now);
                    return true;
                }


                // 20% bergantung
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
                    TurnBackFromEdge(thinking);
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


                // 40% naik kembali
                if (hangChoice < 0.40)
                {
                    BeginClimbingUp(now);

                    return true;
                }


                // 20% turun sisi
                if (hangChoice < 0.60)
                {
                    BeginSideClimbDown(now);

                    return true;
                }


                // 30% coba lompat ke window lain
                if (hangChoice < 0.90)
                {
                    if (BeginTargetJump(now))
                    {
                        return true;
                    }

                    BeginClimbingUp(now);

                    return true;
                }


                // 10% benar-benar nekat jatuh
                ReleaseFromSurface(
                    _surfaceEdgeDirection *
                    180.0,

                    100.0);

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


                double sideChoice =
                    _random.NextDouble();


                // 60% naik
                if (sideChoice < 0.60)
                {
                    BeginSideClimbUp(now);
                }
                else if (sideChoice < 0.85)
                {
                    // 25% mencoba loncat
                    if (BeginTargetJump(now))
                    {
                        return true;
                    }

                    BeginSideClimbUp(now);
                }
                else
                {
                    // 15% melepas diri dari sisi
                    ReleaseFromSurface(
                        _surfaceEdgeDirection *
                        160.0,

                        100.0);
                }

                return true;


            case SurfaceAction.JumpPreparing:

                if (now <
                    _surfaceActionUntil)
                {
                    return true;
                }

                double velocityX =
                    _pendingJumpVelocityX;

                double velocityY =
                    _pendingJumpVelocityY;

                nint targetWindowHandle =
                    _pendingJumpTargetHandle;

                _pendingJumpVelocityX = 0;
                _pendingJumpVelocityY = 0;
                _pendingJumpTargetHandle = nint.Zero;

                ReleaseFromSurface(
                    velocityX,
                    velocityY,
                    targetWindowHandle);

                return true;


            case SurfaceAction.SideClimbingUp:

                UpdateSideClimb(
                    now,
                    climbingUp: true);

                return true;

            case SurfaceAction.ClimbingUp:

                UpdateClimbingUp(now, thinking);

                return true;


            default:

                return false;
        }
    }

    private void TurnBackFromEdge(bool thinking)
    {
        _surfaceAction =
            SurfaceAction.None;


        int newDirection =
            -_surfaceEdgeDirection;


        _character.SetState(
            CharacterState.Idle);


        _character.SetFacingDirection(
            newDirection);


        if (!thinking)
        {
            _character.SetMood(
                CharacterMood.Neutral);
        }


        DirectionChanged?.Invoke(newDirection);

        DecisionDelayRequested?.Invoke(
            0.4,
            1.0);
    }

    public bool UpdateSupportWindow()
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

        bool isSideAttached =
            _surfaceAction ==
                SurfaceAction.Hanging
            ||
            _surfaceAction ==
                SurfaceAction.SideHolding
            ||
            _surfaceAction ==
                SurfaceAction.JumpPreparing;


        double left;


        if (isSideAttached)
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
    double verticalVelocity,
    nint targetWindowHandle = default)
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

        _pendingJumpVelocityX = 0;
        _pendingJumpVelocityY = 0;
        _pendingJumpTargetHandle = nint.Zero;





        _character.SetMood(
            CharacterMood.Surprised);

        SurfaceLaunchRequested?.Invoke(
            horizontalVelocity,
            verticalVelocity,
            targetWindowHandle);
    }

    private void LoseWindowSupport()
    {
        _surfaceAction =
            SurfaceAction.None;

        _surfaceActionUntil =
            DateTime.MinValue;

        _sideGripOffsetY = 0;

        _pendingJumpVelocityX = 0;
        _pendingJumpVelocityY = 0;
        _pendingJumpTargetHandle = nint.Zero;

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



        SupportLost?.Invoke();
    }

    public void MoveOnSupportWindow(
    double deltaSeconds,
    int direction,
    double walkSpeed)
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
            (direction *
             walkSpeed *
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

    public void RestoreCharacterState(int defaultDirection)
    {
        if (_surfaceAction == SurfaceAction.Hanging ||
            _surfaceAction == SurfaceAction.SideHolding ||
            _surfaceAction == SurfaceAction.JumpPreparing)
        {
            _character.SetState(CharacterState.Hanging);
            _character.SetFacingDirection(_surfaceEdgeDirection);
            return;
        }

        if (_surfaceAction == SurfaceAction.ClimbingUp ||
            _surfaceAction == SurfaceAction.SideClimbingDown ||
            _surfaceAction == SurfaceAction.SideClimbingUp)
        {
            _character.SetState(CharacterState.Climbing);
            _character.SetFacingDirection(_surfaceEdgeDirection);
            return;
        }

        _character.SetState(CharacterState.Idle);
        _character.SetFacingDirection(defaultDirection);
    }
}
