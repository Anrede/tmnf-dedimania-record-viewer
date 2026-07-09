using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TmnfDedimaniaScraper;

public partial class RecordsDisplayWindow : Window
{
    private readonly ObservableCollection<RecordsDisplayRowView> _displayRows = new();
    private List<RankTimeByRowView> _table1Rows = new();
    private List<RankTimeByRowView> _table2Rows = new();
    private RecordsDisplaySettings _settings = RecordsDisplaySettings.CreateDefault();
    private bool _showTable1 = true;
    private string _trackName = string.Empty;
    private bool _overlayMode;
    private DispatcherTimer? _topmostTimer;

    public RecordsDisplayWindow()
    {
        InitializeComponent();
        RowsItemsControl.ItemsSource = _displayRows;
        Closed += (_, _) => StopTopmostTimer();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyOverlayModeToHandle();
    }

    public void SetTables(IEnumerable<RankTimeByRowView> table1Rows, IEnumerable<RankTimeByRowView> table2Rows, string trackName, RecordsDisplaySettings settings)
    {
        _table1Rows = table1Rows.ToList();
        _table2Rows = table2Rows.ToList();
        _trackName = trackName ?? string.Empty;
        _settings = RecordsDisplaySettingsCloner.Clone(settings ?? RecordsDisplaySettings.CreateDefault());
        ApplyWindowSettings();
        RefreshVisibleTable();
    }

    public void ApplySettings(RecordsDisplaySettings settings, string trackName)
    {
        _settings = RecordsDisplaySettingsCloner.Clone(settings ?? RecordsDisplaySettings.CreateDefault());
        _trackName = trackName ?? string.Empty;
        ApplyWindowSettings();
        RefreshVisibleTable();
    }


    public bool IsOverlayMode => _overlayMode;

    public void SetOverlayMode(bool enabled)
    {
        if (_overlayMode == enabled)
        {
            if (_overlayMode)
                ReassertTopmost();
            return;
        }

        _overlayMode = enabled;
        ApplyOverlayModeToHandle();
    }

    public void ToggleOverlayMode()
    {
        SetOverlayMode(!_overlayMode);
    }

    public void ToggleVisibleTableFromExternalHotkey()
    {
        ToggleVisibleTableWithAnimation();
        ReassertTopmost();
    }

    private void ApplyOverlayModeToHandle()
    {
        Topmost = _overlayMode;
        ShowInTaskbar = !_overlayMode;

        if (!HasSourceHandle())
        {
            if (_overlayMode)
                StartTopmostTimer();
            else
                StopTopmostTimer();
            return;
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (_overlayMode)
            style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        else
            style &= ~(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        SetWindowLong(hwnd, GWL_EXSTYLE, style);
        SetWindowPos(
            hwnd,
            _overlayMode ? HWND_TOPMOST : HWND_NOTOPMOST,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        if (_overlayMode)
            StartTopmostTimer();
        else
            StopTopmostTimer();
    }

    private void StartTopmostTimer()
    {
        _topmostTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _topmostTimer.Tick -= TopmostTimer_Tick;
        _topmostTimer.Tick += TopmostTimer_Tick;
        _topmostTimer.Start();
    }

    private void StopTopmostTimer()
    {
        if (_topmostTimer is not null)
            _topmostTimer.Stop();
    }

    private void TopmostTimer_Tick(object? sender, EventArgs e)
    {
        ReassertTopmost();
    }

    private void ReassertTopmost()
    {
        if (!_overlayMode || !HasSourceHandle() || !IsVisible || WindowState == WindowState.Minimized)
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }


    private bool HasSourceHandle()
    {
        return System.Windows.PresentationSource.FromVisual(this) is not null;
    }

    private void ApplyWindowSettings()
    {
        Thickness framePadding = GetClampedFramePaddingThickness();
        bool hasVisibleFrame = OverlayAssetCatalog.ResolveFramePath(_settings) is not null && Math.Clamp(_settings.FrameOpacity, 0, 1) > 0;
        Thickness frameBleed = hasVisibleFrame ? GetFrameBleedThickness(framePadding) : new Thickness(0);

        Left = _settings.WindowLeft - frameBleed.Left;
        Top = _settings.WindowTop - frameBleed.Top;
        Width = Math.Max(220, _settings.WindowWidth + frameBleed.Left + frameBleed.Right);
        Height = Math.Max(160, _settings.WindowHeight + frameBleed.Top + frameBleed.Bottom);
        ContainerBorder.Margin = frameBleed;

        FontWeight fontWeight = _settings.UseBoldText ? FontWeights.Bold : FontWeights.Normal;
        TitleTextBlock.FontSize = _settings.TitleSize;
        TitleTextBlock.FontWeight = fontWeight;
        TitleTextBlock.Foreground = CssColorHelper.ToBrush(_settings.TitleColor, Brushes.White);
        LeaderboardList.Margin = new Thickness(0, Math.Clamp(_settings.TitleTextSpacing, 0, 100), 0, 0);
        MapLabelTextBlock.FontSize = _settings.FontSize;
        MapLabelTextBlock.FontWeight = fontWeight;
        MapLabelTextBlock.Foreground = CssColorHelper.ToBrush(_settings.MapLabelColor, Brushes.White);
        MapNameTextBlock.FontSize = _settings.FontSize;
        MapNameTextBlock.FontWeight = fontWeight;
        MapNameTextBlock.Foreground = CssColorHelper.ToBrush(_settings.MapNameColor, Brushes.White);

        double colorCornerRadius = Math.Clamp(_settings.BorderRadius, 0, 120);
        ContainerBorder.CornerRadius = new CornerRadius(Math.Max(
            colorCornerRadius,
            Math.Clamp(_settings.BackgroundImageBorderRadius, 0, 120)));

        Thickness textPadding = GetClampedPositiveThickness(_settings.TextPaddingLeft, _settings.TextPaddingTop, _settings.TextPaddingRight, _settings.TextPaddingBottom, 0, 120);
        ContentGrid.Margin = textPadding;

        Thickness colorPadding = GetClampedPositiveThickness(_settings.BackgroundColorPaddingLeft, _settings.BackgroundColorPaddingTop, _settings.BackgroundColorPaddingRight, _settings.BackgroundColorPaddingBottom, 0, 120);
        ColorBackgroundBorder.Margin = colorPadding;
        ColorBackgroundBorder.CornerRadius = new CornerRadius(GetAdjustedCornerRadius(colorCornerRadius, colorPadding));

        if (CssColorHelper.TryParse(_settings.BackgroundColor, out var color))
        {
            double opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 1);
            ColorBackgroundBorder.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255d), color.R, color.G, color.B));
        }
        else
        {
            ColorBackgroundBorder.Background = new SolidColorBrush(Color.FromArgb(220, 17, 17, 17));
        }

        ApplyBackgroundImageLayer();
        ApplyFrameLayer(framePadding);

        NormalizeAnimationState();
    }


    private void ApplyBackgroundImageLayer()
    {
        string? backgroundPath = OverlayAssetCatalog.ResolveBackgroundPath(_settings);
        Thickness imagePadding = GetClampedPositiveThickness(_settings.ImagePaddingLeft, _settings.ImagePaddingTop, _settings.ImagePaddingRight, _settings.ImagePaddingBottom, 0, 120);
        BackgroundImageBorder.Margin = imagePadding;
        BackgroundImageBorder.CornerRadius = new CornerRadius(GetAdjustedCornerRadius(
            Math.Clamp(_settings.BackgroundImageBorderRadius, 0, 120),
            imagePadding));

        var brush = OverlayAssetCatalog.CreateImageBrush(backgroundPath);
        if (brush is null)
        {
            BackgroundImageBorder.Background = null;
            BackgroundImageBorder.Opacity = 0;
            return;
        }

        BackgroundImageBorder.Background = brush;
        BackgroundImageBorder.Opacity = Math.Clamp(_settings.BackgroundImageOpacity, 0, 1);
    }

    private void ApplyFrameLayer(Thickness framePadding)
    {
        string? framePath = OverlayAssetCatalog.ResolveFramePath(_settings);
        FrameOverlayImage.Margin = new Thickness(
            Math.Max(0, framePadding.Left),
            Math.Max(0, framePadding.Top),
            Math.Max(0, framePadding.Right),
            Math.Max(0, framePadding.Bottom));

        var source = OverlayAssetCatalog.LoadImage(framePath);
        if (source is null)
        {
            FrameOverlayImage.Source = null;
            FrameOverlayImage.Opacity = 0;
            return;
        }

        FrameOverlayImage.Source = source;
        FrameOverlayImage.Opacity = Math.Clamp(_settings.FrameOpacity, 0, 1);
    }

    private static double GetAdjustedCornerRadius(double radius, Thickness padding)
    {
        double maxPadding = Math.Max(Math.Max(padding.Left, padding.Right), Math.Max(padding.Top, padding.Bottom));
        return Math.Max(0, radius - Math.Max(0, maxPadding * 0.65));
    }

    private Thickness GetClampedFramePaddingThickness()
    {
        return new Thickness(
            ClampFramePadding(_settings.FramePaddingLeft),
            ClampFramePadding(_settings.FramePaddingTop),
            ClampFramePadding(_settings.FramePaddingRight),
            ClampFramePadding(_settings.FramePaddingBottom));
    }

    private static Thickness GetFrameBleedThickness(Thickness framePadding)
    {
        return new Thickness(
            Math.Max(0, -framePadding.Left),
            Math.Max(0, -framePadding.Top),
            Math.Max(0, -framePadding.Right),
            Math.Max(0, -framePadding.Bottom));
    }

    private static Thickness GetClampedPositiveThickness(double left, double top, double right, double bottom, double min, double max)
    {
        return new Thickness(
            ClampPadding(left, min, max),
            ClampPadding(top, min, max),
            ClampPadding(right, min, max),
            ClampPadding(bottom, min, max));
    }

    private static double ClampPadding(double value, double min, double max)
    {
        if (!double.IsFinite(value))
            return min;

        return Math.Clamp(value, min, max);
    }

    private static double ClampFramePadding(double value)
    {
        if (!double.IsFinite(value))
            return 0;

        return Math.Clamp(value, -240, 240);
    }


    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            ToggleVisibleTableWithAnimation();
            e.Handled = true;
        }
    }

    private void ToggleVisibleTableWithAnimation()
    {
        string preset = _settings.TransformAnimation ?? RecordDisplayAnimationPresets.Slide;
        if (string.Equals(preset, RecordDisplayAnimationPresets.None, StringComparison.OrdinalIgnoreCase) || !IsLoaded)
        {
            SwitchToTargetTable(!_showTable1);
            NormalizeAnimationState();
            return;
        }

        PlayTransition(preset);
    }

    private void NormalizeAnimationState()
    {
        ListTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ListTransform.BeginAnimation(TranslateTransform.YProperty, null);
        ListScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ListScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, null);

        RowsItemsControl.Opacity = 1d;
        ListTransform.X = 0d;
        ListTransform.Y = 0d;
        ListScaleTransform.ScaleX = 1d;
        ListScaleTransform.ScaleY = 1d;
    }

    private TransitionSnapshot CaptureCurrentVisualState()
    {
        return new TransitionSnapshot(
            RowsItemsControl.Opacity,
            ListTransform.X,
            ListTransform.Y,
            ListScaleTransform.ScaleX,
            ListScaleTransform.ScaleY);
    }

    private void SwitchToTargetTable(bool showTable1)
    {
        _showTable1 = showTable1;
        RefreshVisibleTable();
    }

    private void PlayTransition(string preset)
    {
        if (string.Equals(preset, RecordDisplayAnimationPresets.Fade, StringComparison.OrdinalIgnoreCase))
        {
            PlayFadeTransition();
            return;
        }

        if (string.Equals(preset, RecordDisplayAnimationPresets.VerticalFade, StringComparison.OrdinalIgnoreCase))
        {
            PlayVerticalFadeTransition();
            return;
        }

        if (string.Equals(preset, RecordDisplayAnimationPresets.ZoomFade, StringComparison.OrdinalIgnoreCase))
        {
            PlayZoomFadeTransition();
            return;
        }

        if (string.Equals(preset, RecordDisplayAnimationPresets.Pop, StringComparison.OrdinalIgnoreCase))
        {
            PlayPopTransition();
            return;
        }

        if (string.Equals(preset, RecordDisplayAnimationPresets.LiftFade, StringComparison.OrdinalIgnoreCase))
        {
            PlayLiftFadeTransition();
            return;
        }

        PlaySlideTransition();
    }

    private void PlayFadeTransition()
    {
        var state = CaptureCurrentVisualState();

        var fadeOut = new DoubleAnimation
        {
            From = state.Opacity,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(180)
        };

        fadeOut.Completed += (_, _) =>
        {
            SwitchToTargetTable(!_showTable1);
            RowsItemsControl.Opacity = 0d;
            ListTransform.X = 0d;
            ListTransform.Y = 0d;
            ListScaleTransform.ScaleX = 1d;
            ListScaleTransform.ScaleY = 1d;

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(180)
            };

            fadeIn.Completed += (_, _) =>
            {
                NormalizeAnimationState();
            };

            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void PlaySlideTransition()
    {
        var state = CaptureCurrentVisualState();
        double direction = !_showTable1 ? -1d : 1d;

        var slideOut = new DoubleAnimation
        {
            From = state.X,
            To = 20d * direction,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = state.Opacity,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(180)
        };

        fadeOut.Completed += (_, _) =>
        {
            SwitchToTargetTable(!_showTable1);
            ListTransform.X = -20d * direction;
            ListTransform.Y = 0d;
            ListScaleTransform.ScaleX = 1d;
            ListScaleTransform.ScaleY = 1d;
            RowsItemsControl.Opacity = 0d;

            var slideIn = new DoubleAnimation
            {
                From = -20d * direction,
                To = 0d,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(220)
            };

            fadeIn.Completed += (_, _) =>
            {
                NormalizeAnimationState();
            };

            ListTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void PlayVerticalFadeTransition()
    {
        var state = CaptureCurrentVisualState();

        var slideOut = new DoubleAnimation
        {
            From = state.Y,
            To = 20d,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = state.Opacity,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        fadeOut.Completed += (_, _) =>
        {
            SwitchToTargetTable(!_showTable1);
            ListTransform.X = 0d;
            ListTransform.Y = -20d;
            ListScaleTransform.ScaleX = 1d;
            ListScaleTransform.ScaleY = 1d;
            RowsItemsControl.Opacity = 0d;

            var slideIn = new DoubleAnimation
            {
                From = -20d,
                To = 0d,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(250)
            };

            fadeIn.Completed += (_, _) =>
            {
                NormalizeAnimationState();
            };

            ListTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }


    private void PlayZoomFadeTransition()
    {
        var state = CaptureCurrentVisualState();

        var scaleOut = new DoubleAnimation
        {
            From = state.ScaleX,
            To = 0.94d,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = state.Opacity,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(170)
        };

        fadeOut.Completed += (_, _) =>
        {
            SwitchToTargetTable(!_showTable1);
            ListTransform.X = 0d;
            ListTransform.Y = 0d;
            ListScaleTransform.ScaleX = 1.06d;
            ListScaleTransform.ScaleY = 1.06d;
            RowsItemsControl.Opacity = 0d;

            var scaleIn = new DoubleAnimation
            {
                From = 1.06d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(220)
            };

            fadeIn.Completed += (_, _) =>
            {
                NormalizeAnimationState();
            };

            ListScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            ListScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
        ListScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void PlayPopTransition()
    {
        var state = CaptureCurrentVisualState();

        var scaleOut = new DoubleAnimation
        {
            From = state.ScaleX,
            To = 0.90d,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.35 }
        };

        var fadeOut = new DoubleAnimation
        {
            From = state.Opacity,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(140)
        };

        fadeOut.Completed += (_, _) =>
        {
            SwitchToTargetTable(!_showTable1);
            ListTransform.X = 0d;
            ListTransform.Y = 0d;
            ListScaleTransform.ScaleX = 0.86d;
            ListScaleTransform.ScaleY = 0.86d;
            RowsItemsControl.Opacity = 0d;

            var scaleIn = new DoubleAnimationUsingKeyFrames();
            scaleIn.KeyFrames.Add(new EasingDoubleKeyFrame(0.86d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleIn.KeyFrames.Add(new EasingDoubleKeyFrame(1.05d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }));
            scaleIn.KeyFrames.Add(new EasingDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(220)
            };

            fadeIn.Completed += (_, _) =>
            {
                NormalizeAnimationState();
            };

            ListScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            ListScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
        ListScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void PlayLiftFadeTransition()
    {
        var state = CaptureCurrentVisualState();

        var slideOut = new DoubleAnimation
        {
            From = state.Y,
            To = -14d,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = state.Opacity,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(160)
        };

        fadeOut.Completed += (_, _) =>
        {
            SwitchToTargetTable(!_showTable1);
            ListTransform.X = 0d;
            ListTransform.Y = 18d;
            ListScaleTransform.ScaleX = 1d;
            ListScaleTransform.ScaleY = 1d;
            RowsItemsControl.Opacity = 0d;

            var slideIn = new DoubleAnimation
            {
                From = 18d,
                To = 0d,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(220)
            };

            fadeIn.Completed += (_, _) =>
            {
                NormalizeAnimationState();
            };

            ListTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void RefreshVisibleTable()
    {
        _displayRows.Clear();

        var activeRows = _showTable1 ? _table1Rows : _table2Rows;
        var slotStyles = _showTable1 ? _settings.Table1Styles : _settings.Table2Styles;
        RecordDisplaySettingsHelper.EnsureStyleSlots(_settings, Math.Max(_settings.ShowCount, 20));
        slotStyles = _showTable1 ? _settings.Table1Styles : _settings.Table2Styles;

        int takeCount = Math.Max(0, _settings.ShowCount);
        int index = 0;
        foreach (var row in activeRows.Take(takeCount))
        {
            var slot = slotStyles.FirstOrDefault(s => s.Position == index + 1) ?? new RecordSlotStyle { Position = index + 1 };
            _displayRows.Add(RecordsDisplayRowView.FromRowView(row, slot, _settings));
            index++;
        }

        bool hasTrackName = !string.IsNullOrWhiteSpace(_trackName);
        TrackLinePanel.Visibility = hasTrackName ? Visibility.Visible : Visibility.Collapsed;
        MapLabelTextBlock.Text = hasTrackName ? "Map: " : string.Empty;
        MapNameTextBlock.Text = hasTrackName ? _trackName : string.Empty;
    }


    private readonly record struct TransitionSnapshot(double Opacity, double X, double Y, double ScaleX, double ScaleY);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

public sealed class RecordsDisplayRowView
{
    public string RankText { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public Brush RankBrush { get; init; } = Brushes.White;
    public Brush TimeBrush { get; init; } = Brushes.White;
    public double FontSize { get; init; }
    public FontWeight FontWeight { get; init; }
    public Thickness RowMargin { get; init; }
    public Thickness RankMargin { get; init; }
    public Thickness TimeMargin { get; init; }
    public List<DisplayTextRun> BySegments { get; init; } = new();

    public static RecordsDisplayRowView FromRowView(RankTimeByRowView row, RecordSlotStyle style, RecordsDisplaySettings settings)
    {
        var bySegments = row.Source.By.Segments.Count > 0
            ? row.Source.By.Segments.Select(s => DisplayTextRun.FromTextSegment(s, settings)).ToList()
            : new List<DisplayTextRun> { new DisplayTextRun { Text = "-", Foreground = Brushes.White, FontSize = settings.FontSize, FontWeight = settings.UseBoldText ? FontWeights.Bold : FontWeights.Normal, FontStyle = FontStyles.Normal } };

        return new RecordsDisplayRowView
        {
            RankText = CellDataUtilities.BuildCellText(row.Source.Rank),
            TimeText = CellDataUtilities.BuildCellText(row.Source.Time),
            RankBrush = CssColorHelper.ToBrush(style.RankColor, Brushes.White),
            TimeBrush = CssColorHelper.ToBrush(style.TimeColor, Brushes.White),
            FontSize = settings.FontSize,
            FontWeight = settings.UseBoldText ? FontWeights.Bold : FontWeights.Normal,
            RowMargin = new Thickness(0, 0, 0, settings.VerticalSpacing),
            RankMargin = new Thickness(0, 0, settings.RankTimeSpacing, 0),
            TimeMargin = new Thickness(0, 0, settings.TimeBySpacing, 0),
            BySegments = bySegments
        };
    }
}

public sealed class DisplayTextRun
{
    public string Text { get; init; } = string.Empty;
    public Brush Foreground { get; init; } = Brushes.White;
    public FontStyle FontStyle { get; init; } = FontStyles.Normal;
    public double FontSize { get; init; }
    public FontWeight FontWeight { get; init; }

    public static DisplayTextRun FromTextSegment(TextSegment segment, RecordsDisplaySettings settings)
    {
        return new DisplayTextRun
        {
            Text = segment.Text ?? string.Empty,
            Foreground = CssColorHelper.ToBrush(segment.Color, Brushes.White),
            FontStyle = MainWindowFontConverters.ToFontStyle(segment.FontStyle),
            FontSize = settings.FontSize,
            FontWeight = settings.UseBoldText ? FontWeights.Bold : FontWeights.Normal
        };
    }
}
