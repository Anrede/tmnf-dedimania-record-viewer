using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TmnfDedimaniaScraper;

public partial class RecordsDisplayWindow : Window
{
    private readonly ObservableCollection<RecordsDisplayRowView> _displayRows = new();
    private List<RankTimeByRowView> _table1Rows = new();
    private List<RankTimeByRowView> _table2Rows = new();
    private RecordsDisplaySettings _settings = RecordsDisplaySettings.CreateDefault();
    private bool _showTable1 = true;
    private string _trackName = string.Empty;
    private bool _isAnimating;

    public RecordsDisplayWindow()
    {
        InitializeComponent();
        RowsItemsControl.ItemsSource = _displayRows;
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

    private void ApplyWindowSettings()
    {
        Left = _settings.WindowLeft;
        Top = _settings.WindowTop;
        Width = Math.Max(220, _settings.WindowWidth);
        Height = Math.Max(160, _settings.WindowHeight);

        FontWeight fontWeight = _settings.UseBoldText ? FontWeights.Bold : FontWeights.Normal;
        TitleTextBlock.FontSize = _settings.TitleSize;
        TitleTextBlock.FontWeight = fontWeight;
        TrackNameTextBlock.FontSize = _settings.FontSize;
        TrackNameTextBlock.FontWeight = fontWeight;

        if (CssColorHelper.TryParse(_settings.BackgroundColor, out var color))
        {
            double opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 1);
            ContainerBorder.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255d), color.R, color.G, color.B));
        }
        else
        {
            ContainerBorder.Background = new SolidColorBrush(Color.FromArgb(220, 17, 17, 17));
        }

        ResetAnimationState();
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
        if (_isAnimating)
            return;

        string preset = _settings.TransformAnimation ?? RecordDisplayAnimationPresets.Slide;
        if (string.Equals(preset, RecordDisplayAnimationPresets.None, StringComparison.OrdinalIgnoreCase) || !IsLoaded)
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();
            return;
        }

        _isAnimating = true;
        PlayTransition(preset);
    }

    private void ResetAnimationState()
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
        ResetAnimationState();
        RowsItemsControl.Opacity = 1d;

        var fadeOut = new DoubleAnimation
        {
            From = 1d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(180)
        };

        fadeOut.Completed += (_, _) =>
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();
            ResetAnimationState();
            RowsItemsControl.Opacity = 0d;

            var fadeIn = new DoubleAnimation
            {
                From = 0d,
                To = 1d,
                Duration = TimeSpan.FromMilliseconds(180)
            };

            fadeIn.Completed += (_, _) =>
            {
                ResetAnimationState();
                _isAnimating = false;
            };

            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void PlaySlideTransition()
    {
        ResetAnimationState();

        double slideOutTo = _showTable1 ? 20d : -20d;
        var slideOut = new DoubleAnimation
        {
            From = 0d,
            To = slideOutTo,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(180)
        };

        fadeOut.Completed += (_, _) =>
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();

            double slideInFrom = _showTable1 ? -20d : 20d;
            ResetAnimationState();
            ListTransform.X = slideInFrom;
            RowsItemsControl.Opacity = 0d;

            var slideIn = new DoubleAnimation
            {
                From = slideInFrom,
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
                ResetAnimationState();
                _isAnimating = false;
            };

            ListTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void PlayVerticalFadeTransition()
    {
        ResetAnimationState();

        var slideOut = new DoubleAnimation
        {
            From = 0d,
            To = 20d,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        fadeOut.Completed += (_, _) =>
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();

            ResetAnimationState();
            ListTransform.Y = -20d;
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
                ResetAnimationState();
                _isAnimating = false;
            };

            ListTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
            RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        ListTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        RowsItemsControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }


    private void PlayZoomFadeTransition()
    {
        ResetAnimationState();

        var scaleOut = new DoubleAnimation
        {
            From = 1d,
            To = 0.94d,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(170)
        };

        fadeOut.Completed += (_, _) =>
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();

            ResetAnimationState();
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
                ResetAnimationState();
                _isAnimating = false;
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
        ResetAnimationState();

        var scaleOut = new DoubleAnimation
        {
            From = 1d,
            To = 0.90d,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.35 }
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(140)
        };

        fadeOut.Completed += (_, _) =>
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();

            ResetAnimationState();
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
                ResetAnimationState();
                _isAnimating = false;
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
        ResetAnimationState();

        var slideOut = new DoubleAnimation
        {
            From = 0d,
            To = -14d,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(160)
        };

        fadeOut.Completed += (_, _) =>
        {
            _showTable1 = !_showTable1;
            RefreshVisibleTable();

            ResetAnimationState();
            ListTransform.Y = 18d;
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
                ResetAnimationState();
                _isAnimating = false;
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

        TrackNameTextBlock.Text = string.IsNullOrWhiteSpace(_trackName) ? string.Empty : $"Map: {_trackName}";
    }
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
