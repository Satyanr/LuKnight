using System;
using System.Windows;

namespace LuKnight.Services;

public readonly record struct SurfaceJumpPlan(
    DesktopWindowInfo Target,
    double VelocityX,
    double VelocityY);

public enum SurfaceNavigationIntent
{
    Balanced,
    Familiar,
    Explore
}

public static class SurfaceNavigationService
{
    private const double Gravity =
        1850.0;

    private const double MinimumJumpX =
        60.0;

    private const double MaximumJumpX =
        1450.0;

    private const double MaximumTargetAbove =
        420.0;

    private const double MaximumTargetBelow =
        850.0;


    public static bool TryPlanJump(
        nint currentSupportHandle,
        Rect characterBounds,
        int preferredDirection,
        out SurfaceJumpPlan plan)
    {
        return TryPlanJump(
            currentSupportHandle,
            characterBounds,
            preferredDirection,
            environmentMemory: null,
            out plan);
    }

    public static bool TryPlanJump(
        nint currentSupportHandle,
        Rect characterBounds,
        int preferredDirection,
        DesktopEnvironmentMemory? environmentMemory,
        out SurfaceJumpPlan plan)
    {
        return TryPlanJump(
            currentSupportHandle,
            characterBounds,
            preferredDirection,
            environmentMemory,
            SurfaceNavigationIntent.Balanced,
            out plan);
    }

    public static bool TryPlanJump(
        nint currentSupportHandle,
        Rect characterBounds,
        int preferredDirection,
        DesktopEnvironmentMemory? environmentMemory,
        SurfaceNavigationIntent intent,
        out SurfaceJumpPlan plan)
    {
        plan = default;
        DateTime now = DateTime.UtcNow;


        DesktopWindowInfo bestTarget =
            default;

        double bestVelocityX = 0;
        double bestVelocityY = 0;

        double bestScore =
            double.MaxValue;

        bool found = false;


        foreach (DesktopWindowInfo candidate
                 in DesktopWindowService
                     .GetVisibleWindows())
        {
            if (candidate.Handle ==
                currentSupportHandle)
            {
                continue;
            }


            double characterWidth =
                characterBounds.Width;

            double characterHeight =
                characterBounds.Height;


            const double LandingInset = 22.0;
            double minimumTargetLeft = candidate.Bounds.Left + LandingInset;
            double maximumTargetLeft = candidate.Bounds.Right - characterWidth - LandingInset;

            // Window sempit boleh memakai seluruh lebarnya.
            if (maximumTargetLeft < minimumTargetLeft)
            {
                minimumTargetLeft = candidate.Bounds.Left;
                maximumTargetLeft = candidate.Bounds.Right - characterWidth;
            }
            if (maximumTargetLeft < minimumTargetLeft)
                continue;

            double targetLeft;
            // Pilih landing terdekat yang tetap searah dengan lompatan.
            if (preferredDirection > 0)
            {
                targetLeft = Math.Max(minimumTargetLeft, characterBounds.Left + MinimumJumpX);
                if (targetLeft > maximumTargetLeft)
                    continue;
            }
            else
            {
                targetLeft = Math.Min(maximumTargetLeft, characterBounds.Left - MinimumJumpX);
                if (targetLeft < minimumTargetLeft)
                    continue;
            }

            double targetTop =
                candidate.Bounds.Top -
                characterHeight;


            double deltaX =
                targetLeft -
                characterBounds.Left;

            double deltaY =
                targetTop -
                characterBounds.Top;


            double absoluteX =
                Math.Abs(deltaX);


            if (absoluteX <
                    MinimumJumpX ||
                absoluteX >
                    MaximumJumpX)
            {
                continue;
            }


            if (deltaY <
                    -MaximumTargetAbove ||
                deltaY >
                    MaximumTargetBelow)
            {
                continue;
            }


            // Waktu terbang semakin besar
            // untuk target yang jauh.
            double flightTime =
                0.65 +
                (absoluteX / 1200.0);


            flightTime =
                Math.Clamp(
                    flightTime,
                    0.65,
                    1.20);


            double velocityX =
                deltaX /
                flightTime;


            // s = ut + 1/2 at²
            double velocityY =
                (deltaY -
                 (0.5 *
                  Gravity *
                  flightTime *
                  flightTime))
                /
                flightTime;


            // Jangan pilih trajectory yang
            // harus dilempar ke bawah.
            if (velocityY > -80)
            {
                continue;
            }


            // Masih di bawah kemampuan
            // MaximumThrowSpeed = 1450.
            if (Math.Abs(velocityX) >
                    1350 ||
                Math.Abs(velocityY) >
                    1400)
            {
                continue;
            }


            // Prefer window dekat dan
            // yang relatif berada di depan.
            double baseScore =
                absoluteX +
                (Math.Abs(deltaY) * 0.30) +
                (candidate.ZOrder * 3.0);

            double memoryAdjustment =
                environmentMemory?.GetNavigationScoreAdjustment(candidate, now, intent) ?? 0;

            double score = baseScore + memoryAdjustment;


            if (score >= bestScore)
                continue;


            bestScore =
                score;

            bestTarget =
                candidate;

            bestVelocityX =
                velocityX;

            bestVelocityY =
                velocityY;

            found = true;
        }


        if (!found)
            return false;


        plan =
            new SurfaceJumpPlan(
                bestTarget,
                bestVelocityX,
                bestVelocityY);

        return true;
    }
}