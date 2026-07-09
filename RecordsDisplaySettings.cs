using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TmnfDedimaniaScraper;

public sealed class RecordsDisplaySettings
{
    public int ShowCount { get; set; } = 5;
    public double TitleSize { get; set; } = 20;
    public double FontSize { get; set; } = 18;
    public bool UseBoldText { get; set; } = true;
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;
    public double WindowWidth { get; set; } = 520;
    public double WindowHeight { get; set; } = 320;
    public double TitleTextSpacing { get; set; } = 10;
    public double VerticalSpacing { get; set; } = 8;
    public double RankTimeSpacing { get; set; } = 8;
    public double TimeBySpacing { get; set; } = 10;
    public string BackgroundColor { get; set; } = "rgb(18, 18, 18)";
    public double BackgroundOpacity { get; set; } = 0.92;
    public double BorderRadius { get; set; } = 12;
    public double BackgroundColorPadding { get; set; } = 0;
    public double BackgroundColorPaddingLeft { get; set; } = 0;
    public double BackgroundColorPaddingRight { get; set; } = 0;
    public double BackgroundColorPaddingTop { get; set; } = 0;
    public double BackgroundColorPaddingBottom { get; set; } = 0;
    public bool BackgroundColorPaddingSync { get; set; } = true;
    public string BackgroundAssetName { get; set; } = string.Empty;
    public string CustomBackgroundPath { get; set; } = string.Empty;
    public double BackgroundImageOpacity { get; set; } = 0.72;
    public double BackgroundImageBorderRadius { get; set; } = 10;
    public double BackgroundInset { get; set; } = 18;
    public double ImagePadding { get; set; } = 18;
    public double ImagePaddingLeft { get; set; } = 18;
    public double ImagePaddingRight { get; set; } = 18;
    public double ImagePaddingTop { get; set; } = 18;
    public double ImagePaddingBottom { get; set; } = 18;
    public bool ImagePaddingSync { get; set; } = true;
    public string FrameAssetName { get; set; } = string.Empty;
    public string CustomFramePath { get; set; } = string.Empty;
    public double FrameOpacity { get; set; } = 1.0;
    public double FramePadding { get; set; } = 0;
    public double FramePaddingLeft { get; set; } = 0;
    public double FramePaddingRight { get; set; } = 0;
    public double FramePaddingTop { get; set; } = 0;
    public double FramePaddingBottom { get; set; } = 0;
    public bool FramePaddingSync { get; set; } = true;
    public double TextPadding { get; set; } = 18;
    public double TextPaddingLeft { get; set; } = 18;
    public double TextPaddingRight { get; set; } = 18;
    public double TextPaddingTop { get; set; } = 18;
    public double TextPaddingBottom { get; set; } = 18;
    public bool TextPaddingSync { get; set; } = true;
    public string TitleColor { get; set; } = "rgb(248, 250, 252)";
    public string MapLabelColor { get; set; } = "rgb(248, 250, 252)";
    public string MapNameColor { get; set; } = "rgb(248, 250, 252)";
    public double SettingsWindowLeft { get; set; } = 140;
    public double SettingsWindowTop { get; set; } = 120;
    public double SettingsWindowWidth { get; set; } = 520;
    public double SettingsWindowHeight { get; set; } = 560;
    public string TransformAnimation { get; set; } = RecordDisplayAnimationPresets.Slide;
    public List<RecordSlotStyle> Table1Styles { get; set; } = RecordDisplaySettingsHelper.CreateDefaultSlotStyles(20);
    public List<RecordSlotStyle> Table2Styles { get; set; } = RecordDisplaySettingsHelper.CreateDefaultSlotStyles(20);

    public static RecordsDisplaySettings CreateDefault()
    {
        return new RecordsDisplaySettings();
    }
}

public sealed class RecordSlotStyle
{
    public int Position { get; set; }
    public string RankColor { get; set; } = "rgb(238, 238, 238)";
    public string TimeColor { get; set; } = "rgb(255, 230, 102)";
}

public static class RecordDisplayAnimationPresets
{
    public const string Fade = "Fade";
    public const string Slide = "Slide";
    public const string VerticalFade = "Vertical fade";
    public const string ZoomFade = "Zoom fade";
    public const string Pop = "Pop";
    public const string LiftFade = "Lift fade";
    public const string None = "None";

    public static readonly string[] All =
    {
        Fade,
        Slide,
        VerticalFade,
        ZoomFade,
        Pop,
        LiftFade,
        None
    };
}

public static class RecordDisplaySettingsHelper
{
    public static List<RecordSlotStyle> CreateDefaultSlotStyles(int count)
    {
        var result = new List<RecordSlotStyle>();
        for (int i = 1; i <= Math.Max(1, count); i++)
        {
            result.Add(new RecordSlotStyle { Position = i });
        }

        return result;
    }

    public static void EnsureStyleSlots(RecordsDisplaySettings settings, int count)
    {
        EnsureStyleSlots(settings.Table1Styles, count);
        EnsureStyleSlots(settings.Table2Styles, count);
    }

    public static void EnsureStyleSlots(List<RecordSlotStyle> target, int count)
    {
        for (int i = 1; i <= Math.Max(1, count); i++)
        {
            if (!target.Any(s => s.Position == i))
                target.Add(new RecordSlotStyle { Position = i });
        }

        target.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    public static RecordsDisplaySettings Sanitize(RecordsDisplaySettings? input)
    {
        var settings = input is null ? RecordsDisplaySettings.CreateDefault() : RecordsDisplaySettingsCloner.Clone(input);
        settings.ShowCount = Math.Max(1, Math.Min(50, settings.ShowCount));
        settings.TitleSize = SanitizeDouble(settings.TitleSize, 20, 6, 96);
        settings.FontSize = SanitizeDouble(settings.FontSize, 18, 6, 72);
        settings.WindowLeft = SanitizeDouble(settings.WindowLeft, 100, 0, 10000);
        settings.WindowTop = SanitizeDouble(settings.WindowTop, 100, 0, 10000);
        settings.WindowWidth = SanitizeDouble(settings.WindowWidth, 520, 120, 5000);
        settings.WindowHeight = SanitizeDouble(settings.WindowHeight, 320, 120, 5000);
        settings.TitleTextSpacing = SanitizeDouble(settings.TitleTextSpacing, 10, 0, 100);
        settings.VerticalSpacing = SanitizeDouble(settings.VerticalSpacing, 8, 0, 100);
        settings.RankTimeSpacing = SanitizeDouble(settings.RankTimeSpacing, 8, 0, 100);
        settings.TimeBySpacing = SanitizeDouble(settings.TimeBySpacing, 10, 0, 100);
        settings.BackgroundOpacity = SanitizeDouble(settings.BackgroundOpacity, 0.92, 0, 1);
        settings.BorderRadius = SanitizeDouble(settings.BorderRadius, 12, 0, 120);
        settings.BackgroundColorPadding = SanitizeDouble(settings.BackgroundColorPadding, 0, 0, 120);
        settings.BackgroundImageOpacity = SanitizeDouble(settings.BackgroundImageOpacity, 0.72, 0, 1);
        settings.BackgroundImageBorderRadius = SanitizeDouble(settings.BackgroundImageBorderRadius, 10, 0, 120);
        settings.BackgroundInset = SanitizeDouble(settings.BackgroundInset, 18, 0, 120);
        settings.ImagePadding = SanitizeDouble(settings.ImagePadding, 18, 0, 120);
        settings.FrameOpacity = SanitizeDouble(settings.FrameOpacity, 1, 0, 1);
        settings.FramePadding = SanitizeDouble(settings.FramePadding, 0, -240, 240);
        settings.TextPadding = SanitizeDouble(settings.TextPadding, 18, 0, 120);

        (settings.TextPaddingLeft, settings.TextPaddingRight, settings.TextPaddingTop, settings.TextPaddingBottom) = MigrateUniformPadding(settings.TextPaddingLeft, settings.TextPaddingRight, settings.TextPaddingTop, settings.TextPaddingBottom, settings.TextPadding, 18);
        (settings.BackgroundColorPaddingLeft, settings.BackgroundColorPaddingRight, settings.BackgroundColorPaddingTop, settings.BackgroundColorPaddingBottom) = MigrateUniformPadding(settings.BackgroundColorPaddingLeft, settings.BackgroundColorPaddingRight, settings.BackgroundColorPaddingTop, settings.BackgroundColorPaddingBottom, settings.BackgroundColorPadding, 0);
        (settings.ImagePaddingLeft, settings.ImagePaddingRight, settings.ImagePaddingTop, settings.ImagePaddingBottom) = MigrateUniformPadding(settings.ImagePaddingLeft, settings.ImagePaddingRight, settings.ImagePaddingTop, settings.ImagePaddingBottom, settings.ImagePadding, 18);
        (settings.FramePaddingLeft, settings.FramePaddingRight, settings.FramePaddingTop, settings.FramePaddingBottom) = MigrateUniformPadding(settings.FramePaddingLeft, settings.FramePaddingRight, settings.FramePaddingTop, settings.FramePaddingBottom, settings.FramePadding, 0);

        settings.TextPaddingLeft = SanitizeDouble(settings.TextPaddingLeft, settings.TextPadding, 0, 120);
        settings.TextPaddingRight = SanitizeDouble(settings.TextPaddingRight, settings.TextPadding, 0, 120);
        settings.TextPaddingTop = SanitizeDouble(settings.TextPaddingTop, settings.TextPadding, 0, 120);
        settings.TextPaddingBottom = SanitizeDouble(settings.TextPaddingBottom, settings.TextPadding, 0, 120);

        settings.BackgroundColorPaddingLeft = SanitizeDouble(settings.BackgroundColorPaddingLeft, settings.BackgroundColorPadding, 0, 120);
        settings.BackgroundColorPaddingRight = SanitizeDouble(settings.BackgroundColorPaddingRight, settings.BackgroundColorPadding, 0, 120);
        settings.BackgroundColorPaddingTop = SanitizeDouble(settings.BackgroundColorPaddingTop, settings.BackgroundColorPadding, 0, 120);
        settings.BackgroundColorPaddingBottom = SanitizeDouble(settings.BackgroundColorPaddingBottom, settings.BackgroundColorPadding, 0, 120);

        settings.ImagePaddingLeft = SanitizeDouble(settings.ImagePaddingLeft, settings.ImagePadding, 0, 120);
        settings.ImagePaddingRight = SanitizeDouble(settings.ImagePaddingRight, settings.ImagePadding, 0, 120);
        settings.ImagePaddingTop = SanitizeDouble(settings.ImagePaddingTop, settings.ImagePadding, 0, 120);
        settings.ImagePaddingBottom = SanitizeDouble(settings.ImagePaddingBottom, settings.ImagePadding, 0, 120);

        settings.FramePaddingLeft = SanitizeDouble(settings.FramePaddingLeft, settings.FramePadding, -240, 240);
        settings.FramePaddingRight = SanitizeDouble(settings.FramePaddingRight, settings.FramePadding, -240, 240);
        settings.FramePaddingTop = SanitizeDouble(settings.FramePaddingTop, settings.FramePadding, -240, 240);
        settings.FramePaddingBottom = SanitizeDouble(settings.FramePaddingBottom, settings.FramePadding, -240, 240);

        settings.TextPadding = GetRepresentativeUniformPadding(settings.TextPaddingLeft, settings.TextPaddingRight, settings.TextPaddingTop, settings.TextPaddingBottom);
        settings.BackgroundColorPadding = GetRepresentativeUniformPadding(settings.BackgroundColorPaddingLeft, settings.BackgroundColorPaddingRight, settings.BackgroundColorPaddingTop, settings.BackgroundColorPaddingBottom);
        settings.ImagePadding = GetRepresentativeUniformPadding(settings.ImagePaddingLeft, settings.ImagePaddingRight, settings.ImagePaddingTop, settings.ImagePaddingBottom);
        settings.FramePadding = GetRepresentativeUniformPadding(settings.FramePaddingLeft, settings.FramePaddingRight, settings.FramePaddingTop, settings.FramePaddingBottom);
        settings.BackgroundInset = settings.ImagePadding;
        if (string.IsNullOrWhiteSpace(settings.TitleColor))
            settings.TitleColor = "rgb(248, 250, 252)";
        if (string.IsNullOrWhiteSpace(settings.MapLabelColor))
            settings.MapLabelColor = "rgb(248, 250, 252)";
        if (string.IsNullOrWhiteSpace(settings.MapNameColor))
            settings.MapNameColor = "rgb(248, 250, 252)";
        settings.SettingsWindowLeft = SanitizeDouble(settings.SettingsWindowLeft, 140, 0, 10000);
        settings.SettingsWindowTop = SanitizeDouble(settings.SettingsWindowTop, 120, 0, 10000);
        settings.SettingsWindowWidth = SanitizeDouble(settings.SettingsWindowWidth, 520, 400, 2000);
        settings.SettingsWindowHeight = SanitizeDouble(settings.SettingsWindowHeight, 560, 420, 2400);
        if (string.IsNullOrWhiteSpace(settings.BackgroundColor))
            settings.BackgroundColor = "rgb(18, 18, 18)";
        settings.BackgroundAssetName ??= string.Empty;
        settings.CustomBackgroundPath ??= string.Empty;
        settings.FrameAssetName ??= string.Empty;
        settings.CustomFramePath ??= string.Empty;
        if (string.IsNullOrWhiteSpace(settings.TransformAnimation) || !RecordDisplayAnimationPresets.All.Contains(settings.TransformAnimation))
            settings.TransformAnimation = RecordDisplayAnimationPresets.Slide;
        EnsureStyleSlots(settings, Math.Max(settings.ShowCount, 20));
        return settings;
    }

    private static double SanitizeDouble(double value, double fallback, double min, double max)
    {
        if (!double.IsFinite(value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static (double Left, double Right, double Top, double Bottom) MigrateUniformPadding(double left, double right, double top, double bottom, double legacyUniform, double defaultUniform)
    {
        if (NearlyEquals(left, defaultUniform) && NearlyEquals(right, defaultUniform) && NearlyEquals(top, defaultUniform) && NearlyEquals(bottom, defaultUniform) && !NearlyEquals(legacyUniform, defaultUniform))
        {
            return (legacyUniform, legacyUniform, legacyUniform, legacyUniform);
        }

        return (left, right, top, bottom);
    }

    private static double GetRepresentativeUniformPadding(double left, double right, double top, double bottom)
    {
        if (NearlyEquals(left, right) && NearlyEquals(left, top) && NearlyEquals(left, bottom))
            return left;

        return (left + right + top + bottom) / 4d;
    }

    private static bool NearlyEquals(double a, double b)
    {
        return Math.Abs(a - b) < 0.0001d;
    }
}

public static class RecordsDisplaySettingsCloner
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static RecordsDisplaySettings Clone(RecordsDisplaySettings source)
    {
        return JsonSerializer.Deserialize<RecordsDisplaySettings>(JsonSerializer.Serialize(source, Options), Options)
               ?? RecordsDisplaySettings.CreateDefault();
    }
}
