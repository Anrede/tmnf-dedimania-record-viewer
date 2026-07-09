using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace TmnfDedimaniaScraper;

public partial class RecordsDisplaySettingsWindow : Window
{
    private RecordsDisplaySettings _workingSettings = RecordsDisplaySettings.CreateDefault();
    private bool _isLoading;
    private bool _editTable1 = true;
    private bool _editRank = true;

    public event Action<RecordsDisplaySettings>? SettingsApplied;

    public RecordsDisplaySettingsWindow()
    {
        InitializeComponent();
        TransformAnimationComboBox.ItemsSource = RecordDisplayAnimationPresets.All;
    }

    public void LoadSettings(RecordsDisplaySettings settings)
    {
        _workingSettings = RecordDisplaySettingsHelper.Sanitize(settings);
        RecordDisplaySettingsHelper.EnsureStyleSlots(_workingSettings, Math.Max(_workingSettings.ShowCount, 20));

        _isLoading = true;
        try
        {
            Width = _workingSettings.SettingsWindowWidth;
            Height = _workingSettings.SettingsWindowHeight;
            Left = _workingSettings.SettingsWindowLeft;
            Top = _workingSettings.SettingsWindowTop;

            ShowCountTextBox.Text = _workingSettings.ShowCount.ToString(CultureInfo.InvariantCulture);
            TitleSizeTextBox.Text = _workingSettings.TitleSize.ToString(CultureInfo.InvariantCulture);
            FontSizeTextBox.Text = _workingSettings.FontSize.ToString(CultureInfo.InvariantCulture);
            FontWeightComboBox.SelectedIndex = _workingSettings.UseBoldText ? 1 : 0;
            WindowLeftTextBox.Text = _workingSettings.WindowLeft.ToString(CultureInfo.InvariantCulture);
            WindowTopTextBox.Text = _workingSettings.WindowTop.ToString(CultureInfo.InvariantCulture);
            WindowWidthTextBox.Text = _workingSettings.WindowWidth.ToString(CultureInfo.InvariantCulture);
            WindowHeightTextBox.Text = _workingSettings.WindowHeight.ToString(CultureInfo.InvariantCulture);
            VerticalSpacingTextBox.Text = _workingSettings.VerticalSpacing.ToString(CultureInfo.InvariantCulture);
            RankTimeSpacingTextBox.Text = _workingSettings.RankTimeSpacing.ToString(CultureInfo.InvariantCulture);
            TimeBySpacingTextBox.Text = _workingSettings.TimeBySpacing.ToString(CultureInfo.InvariantCulture);
            BackgroundColorTextBox.Text = _workingSettings.BackgroundColor;
            BackgroundOpacityTextBox.Text = _workingSettings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture);
            TransformAnimationComboBox.SelectedItem = _workingSettings.TransformAnimation;

            _editTable1 = true;
            _editRank = true;
            RefreshSlotList();
            if (TargetSlotComboBox.Items.Count > 0)
                TargetSlotComboBox.SelectedIndex = 0;
            UpdateTabButtons();
            LoadCurrentSelectedColor();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public RecordsDisplaySettings GetSettingsForPersistence()
    {
        SaveGeometryIntoWorkingSettings();
        return RecordsDisplaySettingsCloner.Clone(_workingSettings);
    }

    private void SaveGeometryIntoWorkingSettings()
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _workingSettings.SettingsWindowLeft = SanitizeWindowValue(bounds.Left, _workingSettings.SettingsWindowLeft);
        _workingSettings.SettingsWindowTop = SanitizeWindowValue(bounds.Top, _workingSettings.SettingsWindowTop);
        _workingSettings.SettingsWindowWidth = SanitizeWindowValue(bounds.Width, _workingSettings.SettingsWindowWidth);
        _workingSettings.SettingsWindowHeight = SanitizeWindowValue(bounds.Height, _workingSettings.SettingsWindowHeight);
    }

    private static double SanitizeWindowValue(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private void RefreshSlotList()
    {
        string? previousSelection = TargetSlotComboBox.SelectedItem?.ToString();
        TargetSlotComboBox.Items.Clear();
        TargetSlotComboBox.Items.Add("all");
        int count = Math.Max(1, ParseInt(ShowCountTextBox.Text, _workingSettings.ShowCount));
        for (int i = 1; i <= count; i++)
            TargetSlotComboBox.Items.Add(i.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(previousSelection) && TargetSlotComboBox.Items.Contains(previousSelection))
            TargetSlotComboBox.SelectedItem = previousSelection;
        else
            TargetSlotComboBox.SelectedIndex = 0;
    }

    private void Table1TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            CommitSelectedColor();
        _editTable1 = true;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void Table2TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            CommitSelectedColor();
        _editTable1 = false;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void RankItemTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            CommitSelectedColor();
        _editRank = true;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void TimeItemTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            CommitSelectedColor();
        _editRank = false;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void TargetSlotComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;
        LoadCurrentSelectedColor();
    }

    private void ShowCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
            return;

        RefreshSlotList();
        LoadCurrentSelectedColor();
    }

    private void PickBackgroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        PickCssColorInto(BackgroundColorTextBox);
    }

    private void PickSelectedColorButton_Click(object sender, RoutedEventArgs e)
    {
        PickCssColorInto(SelectedColorTextBox);
        CommitSelectedColor();
    }

    private static void PickCssColorInto(TextBox target)
    {
        using var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true
        };

        if (CssColorHelper.TryParse(target.Text, out var current))
            dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            target.Text = $"rgb({dialog.Color.R}, {dialog.Color.G}, {dialog.Color.B})";
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentValues();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            ApplyCurrentValues();
            e.Handled = true;
        }
    }

    private void ApplyCurrentValues()
    {
        CommitSelectedColor();

        _workingSettings.ShowCount = Math.Max(1, ParseInt(ShowCountTextBox.Text, _workingSettings.ShowCount));
        _workingSettings.TitleSize = ParseDouble(TitleSizeTextBox.Text, _workingSettings.TitleSize);
        _workingSettings.FontSize = ParseDouble(FontSizeTextBox.Text, _workingSettings.FontSize);
        _workingSettings.UseBoldText = FontWeightComboBox.SelectedIndex == 1;
        _workingSettings.WindowLeft = ParseDouble(WindowLeftTextBox.Text, _workingSettings.WindowLeft);
        _workingSettings.WindowTop = ParseDouble(WindowTopTextBox.Text, _workingSettings.WindowTop);
        _workingSettings.WindowWidth = ParseDouble(WindowWidthTextBox.Text, _workingSettings.WindowWidth);
        _workingSettings.WindowHeight = ParseDouble(WindowHeightTextBox.Text, _workingSettings.WindowHeight);
        _workingSettings.VerticalSpacing = ParseDouble(VerticalSpacingTextBox.Text, _workingSettings.VerticalSpacing);
        _workingSettings.RankTimeSpacing = ParseDouble(RankTimeSpacingTextBox.Text, _workingSettings.RankTimeSpacing);
        _workingSettings.TimeBySpacing = ParseDouble(TimeBySpacingTextBox.Text, _workingSettings.TimeBySpacing);
        _workingSettings.BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColorTextBox.Text) ? _workingSettings.BackgroundColor : BackgroundColorTextBox.Text.Trim();
        _workingSettings.BackgroundOpacity = ParseDouble(BackgroundOpacityTextBox.Text, _workingSettings.BackgroundOpacity);
        _workingSettings.TransformAnimation = TransformAnimationComboBox.SelectedItem?.ToString() ?? _workingSettings.TransformAnimation;

        SaveGeometryIntoWorkingSettings();
        _workingSettings = RecordDisplaySettingsHelper.Sanitize(_workingSettings);

        _isLoading = true;
        try
        {
            ShowCountTextBox.Text = _workingSettings.ShowCount.ToString(CultureInfo.InvariantCulture);
            TitleSizeTextBox.Text = _workingSettings.TitleSize.ToString(CultureInfo.InvariantCulture);
            FontSizeTextBox.Text = _workingSettings.FontSize.ToString(CultureInfo.InvariantCulture);
            FontWeightComboBox.SelectedIndex = _workingSettings.UseBoldText ? 1 : 0;
            WindowLeftTextBox.Text = _workingSettings.WindowLeft.ToString(CultureInfo.InvariantCulture);
            WindowTopTextBox.Text = _workingSettings.WindowTop.ToString(CultureInfo.InvariantCulture);
            WindowWidthTextBox.Text = _workingSettings.WindowWidth.ToString(CultureInfo.InvariantCulture);
            WindowHeightTextBox.Text = _workingSettings.WindowHeight.ToString(CultureInfo.InvariantCulture);
            VerticalSpacingTextBox.Text = _workingSettings.VerticalSpacing.ToString(CultureInfo.InvariantCulture);
            RankTimeSpacingTextBox.Text = _workingSettings.RankTimeSpacing.ToString(CultureInfo.InvariantCulture);
            TimeBySpacingTextBox.Text = _workingSettings.TimeBySpacing.ToString(CultureInfo.InvariantCulture);
            BackgroundColorTextBox.Text = _workingSettings.BackgroundColor;
            BackgroundOpacityTextBox.Text = _workingSettings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture);
            TransformAnimationComboBox.SelectedItem = _workingSettings.TransformAnimation;
            RefreshSlotList();
            UpdateTabButtons();
            LoadCurrentSelectedColor();
        }
        finally
        {
            _isLoading = false;
        }

        SettingsApplied?.Invoke(RecordsDisplaySettingsCloner.Clone(_workingSettings));
    }

    private void UpdateTabButtons()
    {
        ApplyToggleVisual(Table1TabButton, _editTable1);
        ApplyToggleVisual(Table2TabButton, !_editTable1);
        ApplyToggleVisual(RankItemTabButton, _editRank);
        ApplyToggleVisual(TimeItemTabButton, !_editRank);
    }

    private static void ApplyToggleVisual(Button button, bool isSelected)
    {
        button.Background = isSelected ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
        button.Foreground = isSelected ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
        button.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isSelected ? "#1D4ED8" : "#94A3B8"));
    }

    private void LoadCurrentSelectedColor()
    {
        var slots = GetSelectedSlotStyles().ToList();
        if (slots.Count == 0)
            return;

        SelectedColorTextBox.Text = _editRank ? slots[0].RankColor : slots[0].TimeColor;
    }

    private void CommitSelectedColor()
    {
        var slots = GetSelectedSlotStyles().ToList();
        if (slots.Count == 0)
            return;

        string current = _editRank ? slots[0].RankColor : slots[0].TimeColor;
        string value = string.IsNullOrWhiteSpace(SelectedColorTextBox.Text)
            ? current
            : SelectedColorTextBox.Text.Trim();

        foreach (var slot in slots)
        {
            if (_editRank)
                slot.RankColor = value;
            else
                slot.TimeColor = value;
        }
    }

    private System.Collections.Generic.IEnumerable<RecordSlotStyle> GetSelectedSlotStyles()
    {
        var list = _editTable1 ? _workingSettings.Table1Styles : _workingSettings.Table2Styles;
        int requiredCount = Math.Max(_workingSettings.ShowCount, 20);
        RecordDisplaySettingsHelper.EnsureStyleSlots(list, requiredCount);

        if (string.Equals(TargetSlotComboBox.SelectedItem?.ToString(), "all", StringComparison.OrdinalIgnoreCase))
            return list.Where(s => s.Position >= 1 && s.Position <= _workingSettings.ShowCount).OrderBy(s => s.Position);

        if (int.TryParse(TargetSlotComboBox.SelectedItem?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int position) && position > 0)
            return list.Where(s => s.Position == position);

        return System.Linq.Enumerable.Empty<RecordSlotStyle>();
    }

    private static int ParseInt(string? raw, int fallback)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
    }

    private static double ParseDouble(string? raw, double fallback)
    {
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
    }
}
