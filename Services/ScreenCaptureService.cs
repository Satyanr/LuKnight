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

public static class ScreenCaptureService
{
    private const int MaxWidth = 1600;
    private const int MaxHeight = 1000;
    private const int MaxEncodedBytes = 4 * 1024 * 1024;

    public static ScreenCaptureSnapshot? CapturePrimaryDisplay()
    {
        try
        {
            Forms.Screen? screen = Forms.Screen.PrimaryScreen;
            if (screen is null)
                return null;

            Drawing.Rectangle bounds = screen.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            using var source = new Drawing.Bitmap(
                bounds.Width,
                bounds.Height,
                Imaging.PixelFormat.Format24bppRgb);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(source))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bounds.Size,
                    Drawing.CopyPixelOperation.SourceCopy);
            }

            double scale = Math.Min(
                1d,
                Math.Min(MaxWidth / (double)source.Width, MaxHeight / (double)source.Height));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var resized = new Drawing.Bitmap(
                width,
                height,
                Imaging.PixelFormat.Format24bppRgb);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(resized))
            {
                graphics.DrawImage(source, 0, 0, width, height);
            }

            using var stream = new MemoryStream();
            SaveJpeg(resized, stream, 80L);
            if (stream.Length > MaxEncodedBytes)
                return null;

            return new ScreenCaptureSnapshot(
                "image/jpeg",
                Convert.ToBase64String(stream.ToArray()),
                width,
                height);
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            return null;
        }
    }

    private static void SaveJpeg(Drawing.Image image, Stream stream, long quality)
    {
        Imaging.ImageCodecInfo? codec = Imaging.ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(item => item.FormatID == Imaging.ImageFormat.Jpeg.Guid);
        if (codec is null)
        {
            image.Save(stream, Imaging.ImageFormat.Jpeg);
            return;
        }

        using var parameters = new Imaging.EncoderParameters(1);
        parameters.Param[0] = new Imaging.EncoderParameter(Imaging.Encoder.Quality, quality);
        image.Save(stream, codec, parameters);
    }
}
