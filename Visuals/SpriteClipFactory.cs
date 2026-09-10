using System;
using System.IO;
using System.Linq;

namespace LuKnight.Visuals;

public static class SpriteClipFactory
{
    public static SpriteAnimationClip?
        FromFolder(
            string name,
            string relativeFolder,
            double framesPerSecond,
            bool loop = true)
    {
        string absoluteFolder =
            Path.Combine(
                AppContext.BaseDirectory,
                relativeFolder);


        if (!Directory.Exists(
                absoluteFolder))
        {
            return null;
        }


        string[] frames =
            Directory
                .EnumerateFiles(
                    absoluteFolder,
                    "*.png",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(
                    Path.GetFileName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    path =>
                        Path
                            .GetRelativePath(
                                AppContext.BaseDirectory,
                                path)
                            .Replace(
                                '\\',
                                '/'))
                .ToArray();


        if (frames.Length == 0)
        {
            return null;
        }


        return new SpriteAnimationClip(
            name,
            frames,
            framesPerSecond,
            loop);
    }
}