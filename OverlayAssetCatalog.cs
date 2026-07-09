using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TmnfDedimaniaScraper;

internal static class OverlayAssetCatalog
{
    private static string OverlayRoot => Path.Combine(AppContext.BaseDirectory, "Assets", "Overlay");
    private static string BackgroundsRoot => Path.Combine(OverlayRoot, "backgrounds");
    private static string FramesRoot => Path.Combine(OverlayRoot, "frames");

    public static IReadOnlyList<string> GetBuiltInBackgroundNames()
    {
        return GetNamesFromFolder(BackgroundsRoot);
    }

    public static IReadOnlyList<string> GetBuiltInFrameNames()
    {
        return GetNamesFromFolder(FramesRoot);
    }

    public static string? ResolveBackgroundPath(RecordsDisplaySettings settings)
    {
        return ResolvePath(settings.CustomBackgroundPath, settings.BackgroundAssetName, BackgroundsRoot, settings.BackgroundSourceMode);
    }

    public static string? ResolveFramePath(RecordsDisplaySettings settings)
    {
        return ResolvePath(settings.CustomFramePath, settings.FrameAssetName, FramesRoot, settings.FrameSourceMode);
    }

    public static ImageBrush? CreateImageBrush(string? absolutePath)
    {
        var source = LoadImage(absolutePath);
        if (source is null)
            return null;

        var brush = new ImageBrush(source)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };

        if (brush.CanFreeze)
            brush.Freeze();

        return brush;
    }

    public static ImageSource? LoadImage(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return null;

        try
        {
            string fullPath = Path.GetFullPath(absolutePath);
            if (!File.Exists(fullPath))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();

            if (bitmap.CanFreeze)
                bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetNamesFromFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<string>();

        return Directory.GetFiles(folder)
            .Where(IsSupportedImage)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static string? ResolvePath(string? customPath, string? builtInName, string folder, string? sourceMode)
    {
        bool preferCustom = string.Equals(sourceMode, "Custom", StringComparison.OrdinalIgnoreCase);

        return preferCustom
            ? TryResolveCustom(customPath) ?? TryResolveBuiltIn(builtInName, folder)
            : TryResolveBuiltIn(builtInName, folder) ?? TryResolveCustom(customPath);
    }

    private static string? TryResolveCustom(string? customPath)
    {
        if (string.IsNullOrWhiteSpace(customPath))
            return null;

        try
        {
            string fullCustom = Path.GetFullPath(customPath);
            if (File.Exists(fullCustom))
                return fullCustom;
        }
        catch
        {
        }

        return null;
    }

    private static string? TryResolveBuiltIn(string? builtInName, string folder)
    {
        if (string.IsNullOrWhiteSpace(builtInName))
            return null;

        string builtIn = Path.Combine(folder, builtInName);
        return File.Exists(builtIn) ? builtIn : null;
    }

    private static bool IsSupportedImage(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }
}
