using System.Windows;

namespace LuKnight.Physics;

/// <summary>Recent screen-space motion, independent of mouse polling and rendering cadence.</summary>
public sealed class PointerVelocityTracker
{
    private readonly List<(Point Position, double Time)> _samples = new();
    private const double WindowSeconds = .08;

    public void Reset(Point position, double time) { _samples.Clear(); Add(position, time); }

    public void Add(Point position, double time)
    {
        if (_samples.Count > 0 && time <= _samples[^1].Time) return;
        _samples.Add((position, time));
        // Keep one sample before the cutoff for interpolation at all refresh rates.
        while (_samples.Count > 2 && _samples[1].Time <= time - WindowSeconds) _samples.RemoveAt(0);
    }

    public Vector Velocity
    {
        get
        {
            if (_samples.Count < 2) return default;
            var end = _samples[^1]; var start = _samples[0];
            double cutoff = end.Time - WindowSeconds;
            if (start.Time < cutoff)
            {
                var next = _samples[1];
                double t = (cutoff - start.Time) / (next.Time - start.Time);
                start = (start.Position + (next.Position - start.Position) * t, cutoff);
            }
            double duration = end.Time - start.Time;
            return duration >= .001 ? (end.Position - start.Position) / duration : default;
        }
    }
}
