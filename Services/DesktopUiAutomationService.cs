using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace LuKnight.Services;

public interface IDesktopUiAutomationReader
{
    Task<DesktopUiSnapshot> CaptureAsync(
        DesktopWindowTarget window,
        DesktopUiReadOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopUiAutomationReader : IDesktopUiAutomationReader
{
    public async Task<DesktopUiSnapshot> CaptureAsync(
        DesktopWindowTarget window,
        DesktopUiReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DesktopUiReadOptions();
        ValidateOptions(options);

        if (window.Handle == nint.Zero)
            return DesktopUiSnapshot.Failure(window, "Window target tidak valid.");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Task<DesktopUiSnapshot> capture = Task.Run(
                () => CaptureCore(window, options),
                CancellationToken.None);
            return await capture.WaitAsync(options.EffectiveTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return DesktopUiSnapshot.Failure(
                window,
                "UI Automation tidak merespons tepat waktu.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return DesktopUiSnapshot.Failure(
                window,
                "UI window sudah berubah atau tidak mendukung Automation.");
        }
    }

    private static DesktopUiSnapshot CaptureCore(
        DesktopWindowTarget window,
        DesktopUiReadOptions options)
    {
        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(window.Handle);
        }
        catch (Exception ex) when (
            ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return DesktopUiSnapshot.Failure(
                window,
                "Tidak dapat membaca root UI Automation.");
        }

        if (root is null)
            return DesktopUiSnapshot.Failure(window, "UI Automation root tidak tersedia.");

        var nodes = new List<DesktopUiNodeSnapshot>();
        var queue = new Queue<QueueItem>();
        queue.Enqueue(new QueueItem(root, Depth: 0, Path: "0"));
        bool truncated = false;
        int visitedCount = 0;
        TreeWalker walker = TreeWalker.ControlViewWalker;

        while (queue.Count > 0)
        {
            if (visitedCount >= options.MaxNodes)
            {
                truncated = true;
                break;
            }

            QueueItem item = queue.Dequeue();
            visitedCount++;
            DesktopUiNodeSnapshot? snapshot = TryReadNode(
                item.Element,
                item.Path,
                item.Depth,
                options.MaxTextLength);
            if (snapshot is not null)
                nodes.Add(snapshot);

            if (item.Depth >= options.MaxDepth)
                continue;

            AutomationElement? child;
            try
            {
                child = walker.GetFirstChild(item.Element);
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }
            catch (COMException)
            {
                continue;
            }

            int childIndex = 0;
            while (child is not null)
            {
                if (visitedCount + queue.Count >= options.MaxNodes)
                {
                    truncated = true;
                    break;
                }

                queue.Enqueue(new QueueItem(
                    child,
                    item.Depth + 1,
                    $"{item.Path}/{childIndex}"));
                childIndex++;

                try
                {
                    child = walker.GetNextSibling(child);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
                catch (COMException)
                {
                    break;
                }
            }
        }

        return new DesktopUiSnapshot(window, nodes.ToArray(), truncated);
    }

    private static DesktopUiNodeSnapshot? TryReadNode(
        AutomationElement element,
        string path,
        int depth,
        int maxTextLength)
    {
        try
        {
            AutomationElement.AutomationElementInformation info = element.Current;
            bool isPassword = info.IsPassword;
            string name = isPassword ? "[protected]" : Limit(info.Name, maxTextLength);

            return new DesktopUiNodeSnapshot(
                path,
                depth,
                NormalizeControlType(info.ControlType),
                name,
                Limit(info.AutomationId, maxTextLength),
                Limit(info.ClassName, maxTextLength),
                NormalizeBounds(info.BoundingRectangle),
                info.IsEnabled,
                info.IsOffscreen,
                info.HasKeyboardFocus,
                isPassword);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Rect NormalizeBounds(Rect bounds)
    {
        if (bounds.IsEmpty ||
            double.IsNaN(bounds.X) ||
            double.IsNaN(bounds.Y) ||
            double.IsNaN(bounds.Width) ||
            double.IsNaN(bounds.Height) ||
            double.IsInfinity(bounds.X) ||
            double.IsInfinity(bounds.Y) ||
            double.IsInfinity(bounds.Width) ||
            double.IsInfinity(bounds.Height))
        {
            return Rect.Empty;
        }

        return bounds;
    }

    private static string NormalizeControlType(ControlType? controlType)
    {
        string name = controlType?.ProgrammaticName ?? "ControlType.Unknown";
        const string prefix = "ControlType.";
        return name.StartsWith(prefix, StringComparison.Ordinal)
            ? name[prefix.Length..]
            : name;
    }

    private static string Limit(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static void ValidateOptions(DesktopUiReadOptions options)
    {
        if (options.MaxDepth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDepth));
        if (options.MaxNodes is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options.MaxNodes));
        if (options.MaxTextLength is < 16 or > 512)
            throw new ArgumentOutOfRangeException(nameof(options.MaxTextLength));
        if (options.EffectiveTimeout < TimeSpan.FromMilliseconds(250) ||
            options.EffectiveTimeout > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Timeout));
        }
    }

    private sealed record QueueItem(
        AutomationElement Element,
        int Depth,
        string Path);
}
