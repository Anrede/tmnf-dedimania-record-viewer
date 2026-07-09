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
    public double VerticalSpacing { get; set; } = 8;
    public double RankTimeSpacing { get; set; } = 8;
    public double TimeBySpacing { get; set; } = 10;
    public string BackgroundColor { get; set; } = "rgb(18, 18, 18)";
    public double BackgroundOpacity { get; set; } = 0.92;
    public double BorderRadius { get; set; } = 12;
    public double SettingsWindowLeft { get; set; } = 140;
    public double SettingsWindowTop { get; set; } = 120;
    public double SettingsWindowWidth { get; set; } = 420;
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
        settings.VerticalSpacing = SanitizeDouble(settings.VerticalSpacing, 8, 0, 100);
        settings.RankTimeSpacing = SanitizeDouble(settings.RankTimeSpacing, 8, 0, 100);
        settings.TimeBySpacing = SanitizeDouble(settings.TimeBySpacing, 10, 0, 100);
        settings.BackgroundOpacity = SanitizeDouble(settings.BackgroundOpacity, 0.92, 0, 1);
        settings.BorderRadius = SanitizeDouble(settings.BorderRadius, 12, 0, 120);
        settings.SettingsWindowLeft = SanitizeDouble(settings.SettingsWindowLeft, 140, 0, 10000);
        settings.SettingsWindowTop = SanitizeDouble(settings.SettingsWindowTop, 120, 0, 10000);
        settings.SettingsWindowWidth = SanitizeDouble(settings.SettingsWindowWidth, 420, 320, 2000);
        settings.SettingsWindowHeight = SanitizeDouble(settings.SettingsWindowHeight, 560, 360, 2400);
        if (string.IsNullOrWhiteSpace(settings.BackgroundColor))
            settings.BackgroundColor = "rgb(18, 18, 18)";
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
