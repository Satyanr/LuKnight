using System;
using System.Collections.Generic;

namespace LuKnight.Visuals;

public sealed class SpriteAnimationClip
{
    public string Name { get; }

    public IReadOnlyList<string> Frames { get; }

    public double FramesPerSecond { get; }

    public bool Loop { get; }


    public SpriteAnimationClip(
        string name,
        IReadOnlyList<string> frames,
        double framesPerSecond,
        bool loop)
    {
        Name = name;

        Frames = frames;

        FramesPerSecond =
            Math.Max(
                1,
                framesPerSecond);

        Loop = loop;
    }
}