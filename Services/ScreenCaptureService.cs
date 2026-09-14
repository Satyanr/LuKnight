using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace LuKnight.Services;

public sealed record ScreenCaptureSnapshot(
    string MimeType,
    string Base64Data,
    int Width,
    int Height);

public sealed record ScreenRegionCaptureSnapshot(
    string MimeType,
    byte[] EncodedBytes,
    Drawing.Rectangle SourceBounds,
    int Width,
    int Height);

public static class ScreenCaptureService
{
    private const int PrimaryMaxWidth =
        1600;

    private const int PrimaryMaxHeight =
        1000;

    private const int PrimaryMaxEncodedBytes =
        4 * 1024 * 1024;

    private const int RegionDefaultMaxWidth =
        512;

    private const int RegionDefaultMaxHeight =
        512;

    private const int RegionDefaultMaxEncodedBytes =
        1024 * 1024;

    public static ScreenCaptureSnapshot?
        CapturePrimaryDisplay()
    {
        Forms.Screen? screen =
            Forms.Screen.PrimaryScreen;

        if (screen is null)
            return null;

        ScreenRegionCaptureSnapshot? region =
            CaptureRegion(
                screen.Bounds,
                PrimaryMaxWidth,
                PrimaryMaxHeight,
                PrimaryMaxEncodedBytes);

        if (region is null)
            return null;

        return new ScreenCaptureSnapshot(
            region.MimeType,
            Convert.ToBase64String(
                region.EncodedBytes),
            region.Width,
            region.Height);
    }

    public static ScreenRegionCaptureSnapshot?
        CaptureRegion(
            Drawing.Rectangle requestedBounds,
            int maxWidth =
                RegionDefaultMaxWidth,
            int maxHeight =
                RegionDefaultMaxHeight,
            int maxEncodedBytes =
                RegionDefaultMaxEncodedBytes)
    {
        try
        {
            if (maxWidth < 1 ||
                maxHeight < 1 ||
                maxEncodedBytes < 1024)
            {
                return null;
            }

            if (requestedBounds.Width < 1 ||
                requestedBounds.Height < 1)
            {
                return null;
            }

            Drawing.Rectangle virtualScreen =
                Forms.SystemInformation
                    .VirtualScreen;

            Drawing.Rectangle bounds =
                Drawing.Rectangle.Intersect(
                    requestedBounds,
                    virtualScreen);

            if (bounds.Width < 1 ||
                bounds.Height < 1)
            {
                return null;
            }

            using var source =
                new Drawing.Bitmap(
                    bounds.Width,
                    bounds.Height,
                    Imaging.PixelFormat
                        .Format24bppRgb);

            using (
                Drawing.Graphics graphics =
                    Drawing.Graphics.FromImage(
                        source))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bounds.Size,
                    Drawing.CopyPixelOperation
                        .SourceCopy);
            }

            double scale =
                Math.Min(
                    1d,
                    Math.Min(
                        maxWidth /
                        (double)source.Width,

                        maxHeight /
                        (double)source.Height));

            int width =
                Math.Max(
                    1,
                    (int)Math.Round(
                        source.Width *
                        scale));

            int height =
                Math.Max(
                    1,
                    (int)Math.Round(
                        source.Height *
                        scale));

            using var resized =
                new Drawing.Bitmap(
                    width,
                    height,
                    Imaging.PixelFormat
                        .Format24bppRgb);

            using (
                Drawing.Graphics graphics =
                    Drawing.Graphics.FromImage(
                        resized))
            {
                graphics.DrawImage(
                    source,
                    0,
                    0,
                    width,
                    height);
            }

            using var stream =
                new MemoryStream();

            SaveJpeg(
                resized,
                stream,
                80L);

            if (stream.Length >
                maxEncodedBytes)
            {
                return null;
            }

            return new ScreenRegionCaptureSnapshot(
                "image/jpeg",
                stream.ToArray(),
                bounds,
                width,
                height);
        }
        catch (Exception ex)
            when (
                ex is Win32Exception
                or ExternalException
                or ArgumentException)
        {
            return null;
        }
    }

    private static void SaveJpeg(
        Drawing.Image image,
        Stream stream,
        long quality)
    {
        Imaging.ImageCodecInfo? codec =
            Imaging.ImageCodecInfo
                .GetImageEncoders()
                .FirstOrDefault(
                    item =>
                        item.FormatID ==
                        Imaging.ImageFormat
                            .Jpeg.Guid);

        if (codec is null)
        {
            image.Save(
                stream,
                Imaging.ImageFormat.Jpeg);

            return;
        }

        using var parameters =
            new Imaging.EncoderParameters(
                1);

        parameters.Param[0] =
            new Imaging.EncoderParameter(
                Imaging.Encoder.Quality,
                quality);

        image.Save(
            stream,
            codec,
            parameters);
    }
}
