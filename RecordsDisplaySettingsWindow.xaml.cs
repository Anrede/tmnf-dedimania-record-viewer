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
    private string _activeSection = "Text";
    private bool _selectedColorDirty;

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
            BorderRadiusTextBox.Text = _workingSettings.BorderRadius.ToString(CultureInfo.InvariantCulture);
            TransformAnimationComboBox.SelectedItem = _workingSettings.TransformAnimation;

            _editTable1 = true;
            _editRank = true;
            RefreshSlotList();
            TargetSlotComboBox.SelectedItem = "select";
            UpdateTabButtons();
            UpdateSectionTabs();
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
        TargetSlotComboBox.Items.Add("select");
        TargetSlotComboBox.Items.Add("all");
        int count = Math.Max(1, ParseInt(ShowCountTextBox.Text, _workingSettings.ShowCount));
        for (int i = 1; i <= count; i++)
            TargetSlotComboBox.Items.Add(i.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(previousSelection) && TargetSlotComboBox.Items.Contains(previousSelection))
            TargetSlotComboBox.SelectedItem = previousSelection;
        else
            TargetSlotComboBox.SelectedItem = "select";
    }

    private void Table1TabButton_Click(object sender, RoutedEventArgs e)
    {
        _editTable1 = true;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void Table2TabButton_Click(object sender, RoutedEventArgs e)
    {
        _editTable1 = false;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void RankItemTabButton_Click(object sender, RoutedEventArgs e)
    {
        _editRank = true;
        UpdateTabButtons();
        LoadCurrentSelectedColor();
    }

    private void TimeItemTabButton_Click(object sender, RoutedEventArgs e)
    {
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
        CommitSelectedColor(force: true);
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


    private void SelectedColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
            return;

        _selectedColorDirty = true;
    }

    private void CommitSelectedColorIfDirty()
    {
        if (!_selectedColorDirty)
            return;

        CommitSelectedColor(force: true);
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
        switch (_activeSection)
        {
            case "Text":
                ApplyTextSection();
                break;
            case "Layout":
                ApplyLayoutSection();
                break;
            case "Background":
                ApplyBackgroundSection();
                break;
            case "Color":
                ApplyColorSection();
                break;
        }

        SaveGeometryIntoWorkingSettings();
        _workingSettings = RecordDisplaySettingsHelper.Sanitize(_workingSettings);
        ReloadControlsFromWorkingSettings();
        SettingsApplied?.Invoke(RecordsDisplaySettingsCloner.Clone(_workingSettings));
    }

    private void ApplyTextSection()
    {
        _workingSettings.ShowCount = Math.Max(1, ParseInt(ShowCountTextBox.Text, _workingSettings.ShowCount));
        _workingSettings.TitleSize = ParseDouble(TitleSizeTextBox.Text, _workingSettings.TitleSize);
        _workingSettings.FontSize = ParseDouble(FontSizeTextBox.Text, _workingSettings.FontSize);
        _workingSettings.UseBoldText = FontWeightComboBox.SelectedIndex == 1;
        _workingSettings.TransformAnimation = TransformAnimationComboBox.SelectedItem?.ToString() ?? _workingSettings.TransformAnimation;
    }

    private void ApplyLayoutSection()
    {
        _workingSettings.WindowLeft = ParseDouble(WindowLeftTextBox.Text, _workingSettings.WindowLeft);
        _workingSettings.WindowTop = ParseDouble(WindowTopTextBox.Text, _workingSettings.WindowTop);
        _workingSettings.WindowWidth = ParseDouble(WindowWidthTextBox.Text, _workingSettings.WindowWidth);
        _workingSettings.WindowHeight = ParseDouble(WindowHeightTextBox.Text, _workingSettings.WindowHeight);
        _workingSettings.VerticalSpacing = ParseDouble(VerticalSpacingTextBox.Text, _workingSettings.VerticalSpacing);
        _workingSettings.RankTimeSpacing = ParseDouble(RankTimeSpacingTextBox.Text, _workingSettings.RankTimeSpacing);
        _workingSettings.TimeBySpacing = ParseDouble(TimeBySpacingTextBox.Text, _workingSettings.TimeBySpacing);
    }

    private void ApplyBackgroundSection()
    {
        _workingSettings.BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColorTextBox.Text) ? _workingSettings.BackgroundColor : BackgroundColorTextBox.Text.Trim();
        _workingSettings.BackgroundOpacity = ParseDouble(BackgroundOpacityTextBox.Text, _workingSettings.BackgroundOpacity);
        _workingSettings.BorderRadius = ParseDouble(BorderRadiusTextBox.Text, _workingSettings.BorderRadius);
    }

    private void ApplyColorSection()
    {
        CommitSelectedColorIfDirty();
    }

    private void ReloadControlsFromWorkingSettings()
    {
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
            BorderRadiusTextBox.Text = _workingSettings.BorderRadius.ToString(CultureInfo.InvariantCulture);
            TransformAnimationComboBox.SelectedItem = _workingSettings.TransformAnimation;
            RefreshSlotList();
            UpdateTabButtons();
            UpdateSectionTabs();
            LoadCurrentSelectedColor();
        }
        finally
        {
            _isLoading = false;
        }
    }


    private void TextSectionButton_Click(object sender, RoutedEventArgs e)
    {
        _activeSection = "Text";
        UpdateSectionTabs();
    }

    private void LayoutSectionButton_Click(object sender, RoutedEventArgs e)
    {
        _activeSection = "Layout";
        UpdateSectionTabs();
    }

    private void BackgroundSectionButton_Click(object sender, RoutedEventArgs e)
    {
        _activeSection = "Background";
        UpdateSectionTabs();
    }

    private void ColorSectionButton_Click(object sender, RoutedEventArgs e)
    {
        _activeSection = "Color";
        UpdateSectionTabs();
    }

    private void UpdateSectionTabs()
    {
        ApplyToggleVisual(TextSectionButton, string.Equals(_activeSection, "Text", StringComparison.Ordinal));
        ApplyToggleVisual(LayoutSectionButton, string.Equals(_activeSection, "Layout", StringComparison.Ordinal));
        ApplyToggleVisual(BackgroundSectionButton, string.Equals(_activeSection, "Background", StringComparison.Ordinal));
        ApplyToggleVisual(ColorSectionButton, string.Equals(_activeSection, "Color", StringComparison.Ordinal));

        TextSectionBorder.Visibility = string.Equals(_activeSection, "Text", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        LayoutSectionBorder.Visibility = string.Equals(_activeSection, "Layout", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        BackgroundSectionBorder.Visibility = string.Equals(_activeSection, "Background", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        ColorSectionBorder.Visibility = string.Equals(_activeSection, "Color", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
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

        _isLoading = true;
        try
        {
            if (slots.Count == 0)
            {
                SelectedColorTextBox.Text = string.Empty;
                SelectedColorTextBox.IsEnabled = false;
                PickSelectedColorButton.IsEnabled = false;
                _selectedColorDirty = false;
                return;
            }

            SelectedColorTextBox.IsEnabled = true;
            PickSelectedColorButton.IsEnabled = true;
            SelectedColorTextBox.Text = _editRank ? slots[0].RankColor : slots[0].TimeColor;
            _selectedColorDirty = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void CommitSelectedColor(bool force = false)
    {
        if (!force && !_selectedColorDirty)
            return;

        var slots = GetSelectedSlotStyles().ToList();
        if (slots.Count == 0)
        {
            _selectedColorDirty = false;
            return;
        }

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

        _selectedColorDirty = false;
    }

    private System.Collections.Generic.IEnumerable<RecordSlotStyle> GetSelectedSlotStyles()
    {
        var list = _editTable1 ? _workingSettings.Table1Styles : _workingSettings.Table2Styles;
        int requiredCount = Math.Max(_workingSettings.ShowCount, 20);
        RecordDisplaySettingsHelper.EnsureStyleSlots(list, requiredCount);

        string? selectedSlot = TargetSlotComboBox.SelectedItem?.ToString();
        if (string.Equals(selectedSlot, "select", StringComparison.OrdinalIgnoreCase))
            return System.Linq.Enumerable.Empty<RecordSlotStyle>();

        if (string.Equals(selectedSlot, "all", StringComparison.OrdinalIgnoreCase))
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
