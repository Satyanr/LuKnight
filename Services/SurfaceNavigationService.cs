using System;
using System.Windows;

namespace LuKnight.Services;

public readonly record struct SurfaceJumpPlan(
    DesktopWindowInfo Target,
    double VelocityX,
    double VelocityY);

public static class SurfaceNavigationService
{
    private const double Gravity =
        1850.0;

    private const double MinimumJumpX =
        90.0;

    private const double MaximumJumpX =
        680.0;

    private const double MaximumTargetAbove =
        170.0;

    private const double MaximumTargetBelow =
        520.0;


    public static bool TryPlanJump(
        nint currentSupportHandle,
        Rect characterBounds,
        int preferredDirection,
        out SurfaceJumpPlan plan)
    {
        plan = default;


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


            // Target posisi karakter agar
            // berada kira-kira di tengah
            // window tujuan.
            double targetLeft =
                candidate.Bounds.Left +
                ((candidate.Bounds.Width -
                  characterWidth) / 2.0);


            double minimumTargetLeft =
                candidate.Bounds.Left;

            double maximumTargetLeft =
                Math.Max(
                    minimumTargetLeft,
                    candidate.Bounds.Right -
                    characterWidth);


            targetLeft =
                Math.Clamp(
                    targetLeft,
                    minimumTargetLeft,
                    maximumTargetLeft);


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


            // Hanya pilih target pada arah
            // yang sedang dilihat.
            if (preferredDirection < 0 &&
                deltaX >= 0)
            {
                continue;
            }


            if (preferredDirection > 0 &&
                deltaX <= 0)
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


            // Masih jauh di bawah kemampuan
            // MaximumThrowSpeed = 1450.
            if (Math.Abs(velocityX) >
                    1050 ||
                Math.Abs(velocityY) >
                    1200)
            {
                continue;
            }


            // Prefer window dekat dan
            // yang relatif berada di depan.
            double score =
                absoluteX +
                (Math.Abs(deltaY) * 0.30) +
                (candidate.ZOrder * 3.0);


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