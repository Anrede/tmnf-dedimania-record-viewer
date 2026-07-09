using System;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
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
    private string _activeDisplayElementColorTarget = "Title";
    private bool _displayElementColorDirty;
    private bool _isSynchronizingPaddingInputs;
    private const string NoneItem = "(none)";

    public event Action<RecordsDisplaySettings>? SettingsApplied;

    public RecordsDisplaySettingsWindow()
    {
        _isLoading = true;
        InitializeComponent();
        TransformAnimationComboBox.ItemsSource = RecordDisplayAnimationPresets.All;
        _isLoading = false;
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
            TitleTextSpacingTextBox.Text = _workingSettings.TitleTextSpacing.ToString(CultureInfo.InvariantCulture);
            VerticalSpacingTextBox.Text = _workingSettings.VerticalSpacing.ToString(CultureInfo.InvariantCulture);
            RankTimeSpacingTextBox.Text = _workingSettings.RankTimeSpacing.ToString(CultureInfo.InvariantCulture);
            TimeBySpacingTextBox.Text = _workingSettings.TimeBySpacing.ToString(CultureInfo.InvariantCulture);
            TextPaddingSyncCheckBox.IsChecked = _workingSettings.TextPaddingSync;
            BackgroundColorPaddingSyncCheckBox.IsChecked = _workingSettings.BackgroundColorPaddingSync;
            FramePaddingSyncCheckBox.IsChecked = _workingSettings.FramePaddingSync;
            ImagePaddingSyncCheckBox.IsChecked = _workingSettings.ImagePaddingSync;
            TextPaddingLeftTextBox.Text = _workingSettings.TextPaddingLeft.ToString(CultureInfo.InvariantCulture);
            TextPaddingRightTextBox.Text = _workingSettings.TextPaddingRight.ToString(CultureInfo.InvariantCulture);
            TextPaddingTopTextBox.Text = _workingSettings.TextPaddingTop.ToString(CultureInfo.InvariantCulture);
            TextPaddingBottomTextBox.Text = _workingSettings.TextPaddingBottom.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingLeftTextBox.Text = _workingSettings.BackgroundColorPaddingLeft.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingRightTextBox.Text = _workingSettings.BackgroundColorPaddingRight.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingTopTextBox.Text = _workingSettings.BackgroundColorPaddingTop.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingBottomTextBox.Text = _workingSettings.BackgroundColorPaddingBottom.ToString(CultureInfo.InvariantCulture);
            FramePaddingLeftTextBox.Text = _workingSettings.FramePaddingLeft.ToString(CultureInfo.InvariantCulture);
            FramePaddingRightTextBox.Text = _workingSettings.FramePaddingRight.ToString(CultureInfo.InvariantCulture);
            FramePaddingTopTextBox.Text = _workingSettings.FramePaddingTop.ToString(CultureInfo.InvariantCulture);
            FramePaddingBottomTextBox.Text = _workingSettings.FramePaddingBottom.ToString(CultureInfo.InvariantCulture);
            ImagePaddingLeftTextBox.Text = _workingSettings.ImagePaddingLeft.ToString(CultureInfo.InvariantCulture);
            ImagePaddingRightTextBox.Text = _workingSettings.ImagePaddingRight.ToString(CultureInfo.InvariantCulture);
            ImagePaddingTopTextBox.Text = _workingSettings.ImagePaddingTop.ToString(CultureInfo.InvariantCulture);
            ImagePaddingBottomTextBox.Text = _workingSettings.ImagePaddingBottom.ToString(CultureInfo.InvariantCulture);
            BackgroundColorTextBox.Text = _workingSettings.BackgroundColor;
            BackgroundOpacityTextBox.Text = _workingSettings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture);
            BorderRadiusTextBox.Text = _workingSettings.BorderRadius.ToString(CultureInfo.InvariantCulture);
            BackgroundImageOpacityTextBox.Text = _workingSettings.BackgroundImageOpacity.ToString(CultureInfo.InvariantCulture);
            BackgroundImageBorderRadiusTextBox.Text = _workingSettings.BackgroundImageBorderRadius.ToString(CultureInfo.InvariantCulture);
            CustomBackgroundPathTextBox.Text = _workingSettings.CustomBackgroundPath;
            CustomFramePathTextBox.Text = _workingSettings.CustomFramePath;
            FrameOpacityTextBox.Text = _workingSettings.FrameOpacity.ToString(CultureInfo.InvariantCulture);
            RefreshOverlayAssetComboBoxes();
            TransformAnimationComboBox.SelectedItem = _workingSettings.TransformAnimation;

            _editTable1 = true;
            _editRank = true;
            _activeDisplayElementColorTarget = "Title";
            RefreshSlotList();
            UpdateTabButtons();
            UpdateSectionTabs();
            LoadCurrentSelectedColor();
            LoadCurrentDisplayElementColor();
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
        bool previousLoading = _isLoading;
        _isLoading = true;
        try
        {
            string? previousStart = StartSlotComboBox.SelectedItem?.ToString();
            string? previousEnd = EndSlotComboBox.SelectedItem?.ToString();
            int count = Math.Max(1, ParseInt(ShowCountTextBox.Text, _workingSettings.ShowCount));

            StartSlotComboBox.Items.Clear();
            EndSlotComboBox.Items.Clear();
            for (int i = 1; i <= count; i++)
            {
                string value = i.ToString(CultureInfo.InvariantCulture);
                StartSlotComboBox.Items.Add(value);
                EndSlotComboBox.Items.Add(value);
            }

            string startSelection = (!string.IsNullOrWhiteSpace(previousStart) && StartSlotComboBox.Items.Contains(previousStart))
                ? previousStart
                : "1";
            string endSelection = (!string.IsNullOrWhiteSpace(previousEnd) && EndSlotComboBox.Items.Contains(previousEnd))
                ? previousEnd
                : startSelection;

            StartSlotComboBox.SelectedItem = startSelection;
            EndSlotComboBox.SelectedItem = endSelection;
        }
        finally
        {
            _isLoading = previousLoading;
        }
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

    private void StartSlotComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        bool previousLoading = _isLoading;
        _isLoading = true;
        try
        {
            if (StartSlotComboBox.SelectedItem is not null)
                EndSlotComboBox.SelectedItem = StartSlotComboBox.SelectedItem;
        }
        finally
        {
            _isLoading = previousLoading;
        }

        LoadCurrentSelectedColor();
    }

    private void EndSlotComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

    private void TitleColorTabButton_Click(object sender, RoutedEventArgs e)
    {
        _activeDisplayElementColorTarget = "Title";
        UpdateTabButtons();
        LoadCurrentDisplayElementColor();
    }

    private void MapLabelColorTabButton_Click(object sender, RoutedEventArgs e)
    {
        _activeDisplayElementColorTarget = "MapLabel";
        UpdateTabButtons();
        LoadCurrentDisplayElementColor();
    }

    private void MapNameColorTabButton_Click(object sender, RoutedEventArgs e)
    {
        _activeDisplayElementColorTarget = "MapName";
        UpdateTabButtons();
        LoadCurrentDisplayElementColor();
    }

    private void PickDisplayElementColorButton_Click(object sender, RoutedEventArgs e)
    {
        PickCssColorInto(DisplayElementColorTextBox);
        CommitDisplayElementColor(force: true);
    }

    private void BrowseBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseImagePathInto(CustomBackgroundPathTextBox);
    }

    private void BrowseFrameButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseImagePathInto(CustomFramePathTextBox);
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


    private void RefreshOverlayAssetComboBoxes()
    {
        bool previousLoading = _isLoading;
        _isLoading = true;
        try
        {
            RebuildAssetComboBox(BuiltInBackgroundComboBox, OverlayAssetCatalog.GetBuiltInBackgroundNames(), _workingSettings.BackgroundAssetName);
            RebuildAssetComboBox(BuiltInFrameComboBox, OverlayAssetCatalog.GetBuiltInFrameNames(), _workingSettings.FrameAssetName);
        }
        finally
        {
            _isLoading = previousLoading;
        }
    }

    private static void RebuildAssetComboBox(ComboBox comboBox, System.Collections.Generic.IEnumerable<string> items, string? selected)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(NoneItem);
        foreach (string item in items)
            comboBox.Items.Add(item);

        string normalized = NormalizeAssetSelection(selected) ?? NoneItem;
        comboBox.SelectedItem = comboBox.Items.Contains(normalized) ? normalized : NoneItem;
    }

    private static string? NormalizeAssetSelection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, NoneItem, StringComparison.Ordinal))
            return null;

        return value.Trim();
    }

    private static void BrowseImagePathInto(TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            target.Text = dialog.FileName;
    }

    private void SelectedColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
            return;

        _selectedColorDirty = true;
    }

    private void DisplayElementColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
            return;

        _displayElementColorDirty = true;
    }

    private void CommitSelectedColorIfDirty()
    {
        if (!_selectedColorDirty)
            return;

        CommitSelectedColor(force: true);
    }


    private void CommitDisplayElementColorIfDirty()
    {
        if (!_displayElementColorDirty)
            return;

        CommitDisplayElementColor(force: true);
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
        ApplyTextSection();
        ApplyLayoutSection();
        ApplyPaddingSection();
        ApplyBackgroundSection();
        ApplyColorSection();

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
        _workingSettings.TitleTextSpacing = ParseDouble(TitleTextSpacingTextBox.Text, _workingSettings.TitleTextSpacing);
        _workingSettings.VerticalSpacing = ParseDouble(VerticalSpacingTextBox.Text, _workingSettings.VerticalSpacing);
        _workingSettings.RankTimeSpacing = ParseDouble(RankTimeSpacingTextBox.Text, _workingSettings.RankTimeSpacing);
        _workingSettings.TimeBySpacing = ParseDouble(TimeBySpacingTextBox.Text, _workingSettings.TimeBySpacing);
    }

    private void ApplyPaddingSection()
    {
        _workingSettings.TextPaddingSync = TextPaddingSyncCheckBox.IsChecked == true;
        _workingSettings.BackgroundColorPaddingSync = BackgroundColorPaddingSyncCheckBox.IsChecked == true;
        _workingSettings.FramePaddingSync = FramePaddingSyncCheckBox.IsChecked == true;
        _workingSettings.ImagePaddingSync = ImagePaddingSyncCheckBox.IsChecked == true;

        ApplyPaddingRow(
            _workingSettings.TextPaddingSync,
            TextPaddingLeftTextBox,
            TextPaddingRightTextBox,
            TextPaddingTopTextBox,
            TextPaddingBottomTextBox,
            _workingSettings.TextPaddingLeft,
            _workingSettings.TextPaddingRight,
            _workingSettings.TextPaddingTop,
            _workingSettings.TextPaddingBottom,
            out double textLeft,
            out double textRight,
            out double textTop,
            out double textBottom);
        _workingSettings.TextPaddingLeft = textLeft;
        _workingSettings.TextPaddingRight = textRight;
        _workingSettings.TextPaddingTop = textTop;
        _workingSettings.TextPaddingBottom = textBottom;
        _workingSettings.TextPadding = GetRepresentativePaddingValue(textLeft, textRight, textTop, textBottom);

        ApplyPaddingRow(
            _workingSettings.BackgroundColorPaddingSync,
            BackgroundColorPaddingLeftTextBox,
            BackgroundColorPaddingRightTextBox,
            BackgroundColorPaddingTopTextBox,
            BackgroundColorPaddingBottomTextBox,
            _workingSettings.BackgroundColorPaddingLeft,
            _workingSettings.BackgroundColorPaddingRight,
            _workingSettings.BackgroundColorPaddingTop,
            _workingSettings.BackgroundColorPaddingBottom,
            out double colorLeft,
            out double colorRight,
            out double colorTop,
            out double colorBottom);
        _workingSettings.BackgroundColorPaddingLeft = colorLeft;
        _workingSettings.BackgroundColorPaddingRight = colorRight;
        _workingSettings.BackgroundColorPaddingTop = colorTop;
        _workingSettings.BackgroundColorPaddingBottom = colorBottom;
        _workingSettings.BackgroundColorPadding = GetRepresentativePaddingValue(colorLeft, colorRight, colorTop, colorBottom);

        ApplyPaddingRow(
            _workingSettings.FramePaddingSync,
            FramePaddingLeftTextBox,
            FramePaddingRightTextBox,
            FramePaddingTopTextBox,
            FramePaddingBottomTextBox,
            _workingSettings.FramePaddingLeft,
            _workingSettings.FramePaddingRight,
            _workingSettings.FramePaddingTop,
            _workingSettings.FramePaddingBottom,
            out double frameLeft,
            out double frameRight,
            out double frameTop,
            out double frameBottom);
        _workingSettings.FramePaddingLeft = frameLeft;
        _workingSettings.FramePaddingRight = frameRight;
        _workingSettings.FramePaddingTop = frameTop;
        _workingSettings.FramePaddingBottom = frameBottom;
        _workingSettings.FramePadding = GetRepresentativePaddingValue(frameLeft, frameRight, frameTop, frameBottom);

        ApplyPaddingRow(
            _workingSettings.ImagePaddingSync,
            ImagePaddingLeftTextBox,
            ImagePaddingRightTextBox,
            ImagePaddingTopTextBox,
            ImagePaddingBottomTextBox,
            _workingSettings.ImagePaddingLeft,
            _workingSettings.ImagePaddingRight,
            _workingSettings.ImagePaddingTop,
            _workingSettings.ImagePaddingBottom,
            out double imageLeft,
            out double imageRight,
            out double imageTop,
            out double imageBottom);
        _workingSettings.ImagePaddingLeft = imageLeft;
        _workingSettings.ImagePaddingRight = imageRight;
        _workingSettings.ImagePaddingTop = imageTop;
        _workingSettings.ImagePaddingBottom = imageBottom;
        _workingSettings.ImagePadding = GetRepresentativePaddingValue(imageLeft, imageRight, imageTop, imageBottom);
        _workingSettings.BackgroundInset = _workingSettings.ImagePadding;
    }

    private void ApplyBackgroundSection()
    {
        _workingSettings.BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColorTextBox.Text) ? _workingSettings.BackgroundColor : BackgroundColorTextBox.Text.Trim();
        _workingSettings.BackgroundOpacity = ParseDouble(BackgroundOpacityTextBox.Text, _workingSettings.BackgroundOpacity);
        _workingSettings.BorderRadius = ParseDouble(BorderRadiusTextBox.Text, _workingSettings.BorderRadius);
        _workingSettings.BackgroundAssetName = NormalizeAssetSelection(BuiltInBackgroundComboBox.SelectedItem?.ToString()) ?? string.Empty;
        _workingSettings.CustomBackgroundPath = CustomBackgroundPathTextBox.Text?.Trim() ?? string.Empty;
        _workingSettings.BackgroundImageOpacity = ParseDouble(BackgroundImageOpacityTextBox.Text, _workingSettings.BackgroundImageOpacity);
        _workingSettings.BackgroundImageBorderRadius = ParseDouble(BackgroundImageBorderRadiusTextBox.Text, _workingSettings.BackgroundImageBorderRadius);
        _workingSettings.FrameAssetName = NormalizeAssetSelection(BuiltInFrameComboBox.SelectedItem?.ToString()) ?? string.Empty;
        _workingSettings.CustomFramePath = CustomFramePathTextBox.Text?.Trim() ?? string.Empty;
        _workingSettings.FrameOpacity = ParseDouble(FrameOpacityTextBox.Text, _workingSettings.FrameOpacity);
    }

    private void ApplyColorSection()
    {
        CommitSelectedColorIfDirty();
        CommitDisplayElementColorIfDirty();
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
            TitleTextSpacingTextBox.Text = _workingSettings.TitleTextSpacing.ToString(CultureInfo.InvariantCulture);
            VerticalSpacingTextBox.Text = _workingSettings.VerticalSpacing.ToString(CultureInfo.InvariantCulture);
            RankTimeSpacingTextBox.Text = _workingSettings.RankTimeSpacing.ToString(CultureInfo.InvariantCulture);
            TimeBySpacingTextBox.Text = _workingSettings.TimeBySpacing.ToString(CultureInfo.InvariantCulture);
            TextPaddingSyncCheckBox.IsChecked = _workingSettings.TextPaddingSync;
            BackgroundColorPaddingSyncCheckBox.IsChecked = _workingSettings.BackgroundColorPaddingSync;
            FramePaddingSyncCheckBox.IsChecked = _workingSettings.FramePaddingSync;
            ImagePaddingSyncCheckBox.IsChecked = _workingSettings.ImagePaddingSync;
            TextPaddingLeftTextBox.Text = _workingSettings.TextPaddingLeft.ToString(CultureInfo.InvariantCulture);
            TextPaddingRightTextBox.Text = _workingSettings.TextPaddingRight.ToString(CultureInfo.InvariantCulture);
            TextPaddingTopTextBox.Text = _workingSettings.TextPaddingTop.ToString(CultureInfo.InvariantCulture);
            TextPaddingBottomTextBox.Text = _workingSettings.TextPaddingBottom.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingLeftTextBox.Text = _workingSettings.BackgroundColorPaddingLeft.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingRightTextBox.Text = _workingSettings.BackgroundColorPaddingRight.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingTopTextBox.Text = _workingSettings.BackgroundColorPaddingTop.ToString(CultureInfo.InvariantCulture);
            BackgroundColorPaddingBottomTextBox.Text = _workingSettings.BackgroundColorPaddingBottom.ToString(CultureInfo.InvariantCulture);
            FramePaddingLeftTextBox.Text = _workingSettings.FramePaddingLeft.ToString(CultureInfo.InvariantCulture);
            FramePaddingRightTextBox.Text = _workingSettings.FramePaddingRight.ToString(CultureInfo.InvariantCulture);
            FramePaddingTopTextBox.Text = _workingSettings.FramePaddingTop.ToString(CultureInfo.InvariantCulture);
            FramePaddingBottomTextBox.Text = _workingSettings.FramePaddingBottom.ToString(CultureInfo.InvariantCulture);
            ImagePaddingLeftTextBox.Text = _workingSettings.ImagePaddingLeft.ToString(CultureInfo.InvariantCulture);
            ImagePaddingRightTextBox.Text = _workingSettings.ImagePaddingRight.ToString(CultureInfo.InvariantCulture);
            ImagePaddingTopTextBox.Text = _workingSettings.ImagePaddingTop.ToString(CultureInfo.InvariantCulture);
            ImagePaddingBottomTextBox.Text = _workingSettings.ImagePaddingBottom.ToString(CultureInfo.InvariantCulture);
            BackgroundColorTextBox.Text = _workingSettings.BackgroundColor;
            BackgroundOpacityTextBox.Text = _workingSettings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture);
            BorderRadiusTextBox.Text = _workingSettings.BorderRadius.ToString(CultureInfo.InvariantCulture);
            BackgroundImageOpacityTextBox.Text = _workingSettings.BackgroundImageOpacity.ToString(CultureInfo.InvariantCulture);
            BackgroundImageBorderRadiusTextBox.Text = _workingSettings.BackgroundImageBorderRadius.ToString(CultureInfo.InvariantCulture);
            CustomBackgroundPathTextBox.Text = _workingSettings.CustomBackgroundPath;
            CustomFramePathTextBox.Text = _workingSettings.CustomFramePath;
            FrameOpacityTextBox.Text = _workingSettings.FrameOpacity.ToString(CultureInfo.InvariantCulture);
            RefreshOverlayAssetComboBoxes();
            TransformAnimationComboBox.SelectedItem = _workingSettings.TransformAnimation;
            RefreshSlotList();
            UpdateTabButtons();
            UpdateSectionTabs();
            LoadCurrentSelectedColor();
            LoadCurrentDisplayElementColor();
        }
        finally
        {
            _isLoading = false;
        }
    }


    private void PaddingSideTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isSynchronizingPaddingInputs || sender is not TextBox textBox)
            return;

        string? rowKey = GetPaddingRowKey(textBox.Name);
        if (rowKey is null || !IsPaddingSyncEnabled(rowKey))
            return;

        SyncPaddingRow(rowKey, textBox.Text);
    }

    private void PaddingSyncCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isSynchronizingPaddingInputs || sender is not CheckBox)
            return;

        // Intentionally no immediate value propagation here.
        // When sync is enabled, values are mirrored only while the user edits a padding textbox.
    }

    private void SyncPaddingRow(string rowKey, string value)
    {
        _isSynchronizingPaddingInputs = true;
        try
        {
            SetTextIfDifferent(GetPaddingLeftTextBox(rowKey), value);
            SetTextIfDifferent(GetPaddingRightTextBox(rowKey), value);
            SetTextIfDifferent(GetPaddingTopTextBox(rowKey), value);
            SetTextIfDifferent(GetPaddingBottomTextBox(rowKey), value);
        }
        finally
        {
            _isSynchronizingPaddingInputs = false;
        }
    }

    private static void SetTextIfDifferent(TextBox? textBox, string value)
    {
        if (textBox is null)
            return;

        if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
            textBox.Text = value;
    }

    private static string? GetPaddingRowKey(string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            return null;

        if (controlName.StartsWith("TextPadding", StringComparison.Ordinal))
            return "TextPadding";
        if (controlName.StartsWith("BackgroundColorPadding", StringComparison.Ordinal))
            return "BackgroundColorPadding";
        if (controlName.StartsWith("FramePadding", StringComparison.Ordinal))
            return "FramePadding";
        if (controlName.StartsWith("ImagePadding", StringComparison.Ordinal))
            return "ImagePadding";

        return null;
    }

    private bool IsPaddingSyncEnabled(string rowKey)
    {
        return GetPaddingSyncCheckBox(rowKey)?.IsChecked == true;
    }

    private CheckBox? GetPaddingSyncCheckBox(string rowKey)
    {
        return rowKey switch
        {
            "TextPadding" => TextPaddingSyncCheckBox,
            "BackgroundColorPadding" => BackgroundColorPaddingSyncCheckBox,
            "FramePadding" => FramePaddingSyncCheckBox,
            "ImagePadding" => ImagePaddingSyncCheckBox,
            _ => null
        };
    }

    private TextBox? GetPaddingLeftTextBox(string rowKey)
    {
        return rowKey switch
        {
            "TextPadding" => TextPaddingLeftTextBox,
            "BackgroundColorPadding" => BackgroundColorPaddingLeftTextBox,
            "FramePadding" => FramePaddingLeftTextBox,
            "ImagePadding" => ImagePaddingLeftTextBox,
            _ => null
        };
    }

    private TextBox? GetPaddingRightTextBox(string rowKey)
    {
        return rowKey switch
        {
            "TextPadding" => TextPaddingRightTextBox,
            "BackgroundColorPadding" => BackgroundColorPaddingRightTextBox,
            "FramePadding" => FramePaddingRightTextBox,
            "ImagePadding" => ImagePaddingRightTextBox,
            _ => null
        };
    }

    private TextBox? GetPaddingTopTextBox(string rowKey)
    {
        return rowKey switch
        {
            "TextPadding" => TextPaddingTopTextBox,
            "BackgroundColorPadding" => BackgroundColorPaddingTopTextBox,
            "FramePadding" => FramePaddingTopTextBox,
            "ImagePadding" => ImagePaddingTopTextBox,
            _ => null
        };
    }

    private TextBox? GetPaddingBottomTextBox(string rowKey)
    {
        return rowKey switch
        {
            "TextPadding" => TextPaddingBottomTextBox,
            "BackgroundColorPadding" => BackgroundColorPaddingBottomTextBox,
            "FramePadding" => FramePaddingBottomTextBox,
            "ImagePadding" => ImagePaddingBottomTextBox,
            _ => null
        };
    }

    private void ApplyPaddingRow(bool syncEnabled, TextBox? leftTextBox, TextBox? rightTextBox, TextBox? topTextBox, TextBox? bottomTextBox, double fallbackLeft, double fallbackRight, double fallbackTop, double fallbackBottom, out double left, out double right, out double top, out double bottom)
    {
        left = ParseDouble(leftTextBox?.Text, fallbackLeft);
        if (syncEnabled)
        {
            right = left;
            top = left;
            bottom = left;
            return;
        }

        right = ParseDouble(rightTextBox?.Text, fallbackRight);
        top = ParseDouble(topTextBox?.Text, fallbackTop);
        bottom = ParseDouble(bottomTextBox?.Text, fallbackBottom);
    }

    private static double GetRepresentativePaddingValue(double left, double right, double top, double bottom)
    {
        if (Math.Abs(left - right) < 0.0001d && Math.Abs(left - top) < 0.0001d && Math.Abs(left - bottom) < 0.0001d)
            return left;

        return (left + right + top + bottom) / 4d;
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

    private void PaddingSectionButton_Click(object sender, RoutedEventArgs e)
    {
        _activeSection = "Padding";
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
        ApplyToggleVisual(PaddingSectionButton, string.Equals(_activeSection, "Padding", StringComparison.Ordinal));
        ApplyToggleVisual(BackgroundSectionButton, string.Equals(_activeSection, "Background", StringComparison.Ordinal));
        ApplyToggleVisual(ColorSectionButton, string.Equals(_activeSection, "Color", StringComparison.Ordinal));

        TextSectionBorder.Visibility = string.Equals(_activeSection, "Text", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        LayoutSectionBorder.Visibility = string.Equals(_activeSection, "Layout", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        PaddingSectionBorder.Visibility = string.Equals(_activeSection, "Padding", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        BackgroundSectionBorder.Visibility = string.Equals(_activeSection, "Background", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        ColorSectionBorder.Visibility = string.Equals(_activeSection, "Color", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTabButtons()
    {
        ApplyToggleVisual(Table1TabButton, _editTable1);
        ApplyToggleVisual(Table2TabButton, !_editTable1);
        ApplyToggleVisual(RankItemTabButton, _editRank);
        ApplyToggleVisual(TimeItemTabButton, !_editRank);
        ApplyToggleVisual(TitleColorTabButton, string.Equals(_activeDisplayElementColorTarget, "Title", StringComparison.Ordinal));
        ApplyToggleVisual(MapLabelColorTabButton, string.Equals(_activeDisplayElementColorTarget, "MapLabel", StringComparison.Ordinal));
        ApplyToggleVisual(MapNameColorTabButton, string.Equals(_activeDisplayElementColorTarget, "MapName", StringComparison.Ordinal));
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


    private void LoadCurrentDisplayElementColor()
    {
        _isLoading = true;
        try
        {
            DisplayElementColorTextBox.IsEnabled = true;
            PickDisplayElementColorButton.IsEnabled = true;
            DisplayElementColorTextBox.Text = GetCurrentDisplayElementColor();
            _displayElementColorDirty = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void CommitDisplayElementColor(bool force = false)
    {
        if (!force && !_displayElementColorDirty)
            return;

        string current = GetCurrentDisplayElementColor();
        string value = string.IsNullOrWhiteSpace(DisplayElementColorTextBox.Text)
            ? current
            : DisplayElementColorTextBox.Text.Trim();

        SetCurrentDisplayElementColor(value);
        _displayElementColorDirty = false;
    }

    private string GetCurrentDisplayElementColor()
    {
        return _activeDisplayElementColorTarget switch
        {
            "MapLabel" => _workingSettings.MapLabelColor,
            "MapName" => _workingSettings.MapNameColor,
            _ => _workingSettings.TitleColor
        };
    }

    private void SetCurrentDisplayElementColor(string value)
    {
        switch (_activeDisplayElementColorTarget)
        {
            case "MapLabel":
                _workingSettings.MapLabelColor = value;
                break;
            case "MapName":
                _workingSettings.MapNameColor = value;
                break;
            default:
                _workingSettings.TitleColor = value;
                break;
        }
    }

    private System.Collections.Generic.IEnumerable<RecordSlotStyle> GetSelectedSlotStyles()
    {
        var list = _editTable1 ? _workingSettings.Table1Styles : _workingSettings.Table2Styles;
        int requiredCount = Math.Max(_workingSettings.ShowCount, 20);
        RecordDisplaySettingsHelper.EnsureStyleSlots(list, requiredCount);

        int? start = ParseSelectedSlot(StartSlotComboBox);
        int? end = ParseSelectedSlot(EndSlotComboBox);
        if (!start.HasValue || !end.HasValue)
            return System.Linq.Enumerable.Empty<RecordSlotStyle>();

        int min = Math.Min(start.Value, end.Value);
        int max = Math.Max(start.Value, end.Value);
        return list.Where(s => s.Position >= min && s.Position <= max).OrderBy(s => s.Position);
    }

    private static int? ParseSelectedSlot(ComboBox comboBox)
    {
        return int.TryParse(comboBox.SelectedItem?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : null;
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
