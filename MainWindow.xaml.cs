using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace TmnfDedimaniaScraper;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<OnlineRecordRowView> _rows = new();
    private readonly ObservableCollection<RankTimeByRowView> _table1Rows = new();
    private readonly ObservableCollection<RankTimeByRowView> _table2Rows = new();
    private readonly ObservableCollection<SegmentRowView> _segments = new();
    private readonly ObservableCollection<BookmarkItem> _bookmarks = new();
    private readonly ObservableCollection<ColorPaletteItem> _colorPalette = new();

    private ExtractionResult? _lastExtraction;
    private OnlineRecord? _currentSelectedRecord;
    private SegmentRowView? _currentSelectedSegment;
    private SelectionSource _currentSelectionSource = SelectionSource.None;

    private bool _pageReady;
    private string _currentPageTitle = string.Empty;
    private bool _isSynchronizingSelections;
    private bool _suppressSegmentChangeHandling;
    private bool _suppressStyleSelectionEvent;
    private bool _suppressPaletteApply;
    private Color? _pendingEditorColor;

    private const string AppFolderName = "TmnfDedimaniaScraper";
    private static string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
    private static string BookmarksFilePath => Path.Combine(AppDataDirectory, "bookmarks.json");

    public MainWindow()
    {
        InitializeComponent();

        RecordsGrid.ItemsSource = _rows;
        Table1Grid.ItemsSource = _table1Rows;
        Table2Grid.ItemsSource = _table2Rows;
        SegmentsGrid.ItemsSource = _segments;
        BookmarksItemsControl.ItemsSource = _bookmarks;
        ColorPaletteGrid.ItemsSource = _colorPalette;

        InitializeColorPalette();
        LoadBookmarks();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeBrowserAsync();
        await NavigateAsync(UrlTextBox.Text);
    }

    private void InitializeColorPalette()
    {
        _colorPalette.Clear();

        AddPalette("White", 255, 255, 255);
        AddPalette("Gold", 255, 215, 0);
        AddPalette("Orange", 255, 165, 0);
        AddPalette("Tomato", 255, 99, 71);
        AddPalette("Crimson", 220, 20, 60);
        AddPalette("Hot Pink", 255, 105, 180);
        AddPalette("Purple", 147, 51, 234);
        AddPalette("Dodger Blue", 30, 144, 255);
        AddPalette("Deep Sky", 0, 191, 255);
        AddPalette("Cyan", 0, 255, 255);
        AddPalette("Lime", 50, 205, 50);
        AddPalette("Chartreuse", 127, 255, 0);
        AddPalette("Yellow", 255, 255, 0);
        AddPalette("Silver", 192, 192, 192);
        AddPalette("Gray", 128, 128, 128);
    }

    private void AddPalette(string name, byte r, byte g, byte b)
    {
        _colorPalette.Add(new ColorPaletteItem
        {
            Name = name,
            R = r,
            G = g,
            B = b,
            CssColor = CssColorHelper.ToCssRgb(Color.FromRgb(r, g, b))
        });
    }

    private async Task InitializeBrowserAsync()
    {
        StatusText.Text = "WebView2 başlatılıyor...";
        await Browser.EnsureCoreWebView2Async();
        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
        Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        Browser.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
        UpdateNavigationButtons();
        StatusText.Text = "WebView2 hazır.";
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _pageReady = e.IsSuccess;
        UpdateNavigationButtons();
        StatusText.Text = e.IsSuccess ? "Sayfa yüklendi." : "Sayfa yüklenemedi.";
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        Dispatcher.Invoke(UpdateNavigationButtons);
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UrlTextBox.Text = Browser.Source?.ToString() ?? Browser.CoreWebView2?.Source ?? UrlTextBox.Text;
            UpdateNavigationButtons();
        });
    }

    private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
    {
        Dispatcher.Invoke(() =>
        {
            _currentPageTitle = Browser.CoreWebView2?.DocumentTitle ?? string.Empty;
        });
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync(UrlTextBox.Text);
    }

    private async void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await NavigateAsync(UrlTextBox.Text);
    }

    private async Task NavigateAsync(string url)
    {
        if (Browser.CoreWebView2 is null)
            return;

        string normalizedUrl = NormalizeUrl(url);
        _pageReady = false;
        UrlTextBox.Text = normalizedUrl;
        StatusText.Text = "Sayfa açılıyor...";
        Browser.CoreWebView2.Navigate(normalizedUrl);
        await Task.CompletedTask;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true)
            Browser.CoreWebView2.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoForward == true)
            Browser.CoreWebView2.GoForward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 is null)
            return;

        _pageReady = false;
        StatusText.Text = "Sayfa yenileniyor...";
        Browser.CoreWebView2.Reload();
    }

    private void UpdateNavigationButtons()
    {
        bool browserReady = Browser.CoreWebView2 is not null;
        BackButton.IsEnabled = browserReady && Browser.CoreWebView2!.CanGoBack;
        ForwardButton.IsEnabled = browserReady && Browser.CoreWebView2!.CanGoForward;
        ReloadButton.IsEnabled = browserReady;
    }

    private async void ScrapeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Browser.CoreWebView2 is null)
            {
                MessageBox.Show("WebView2 henüz hazır değil.");
                return;
            }

            ScrapeButton.IsEnabled = false;
            ClearCurrentResults();
            StatusText.Text = "Online records bölümü aranıyor (maks. 3 sn)...";

            var extraction = await TryExtractAsync(TimeSpan.FromSeconds(3));
            _lastExtraction = extraction;
            RefreshJsonText();

            if (extraction.Success)
                FillGrids(extraction);
            else
                ResetPreview();

            StatusText.Text = extraction.Success
                ? $"{extraction.Records.Count} kayıt çekildi. Özel listeler için rank girip ekleyebilirsin."
                : $"Sonuç yok: {extraction.Message}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Veri çekme sırasında hata oluştu.";
            MessageBox.Show(ex.ToString(), "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScrapeButton.IsEnabled = true;
        }
    }

    private async Task<ExtractionResult> TryExtractAsync(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (_pageReady)
            {
                var result = await ExecuteExtractionScriptAsync();
                if (result.Success && result.Records.Count > 0)
                    return result;
            }

            await Task.Delay(500);
        }

        return new ExtractionResult
        {
            Success = false,
            Message = "3 saniye içinde online records tablosu bulunamadı. Track için online record olmayabilir veya sayfa yapısı değişmiş olabilir."
        };
    }

    private async Task<ExtractionResult> ExecuteExtractionScriptAsync()
    {
        string rawJson = await Browser.ExecuteScriptAsync(JsExtractionScript);
        return JsonSerializer.Deserialize<ExtractionResult>(rawJson, JsonOptions)
               ?? new ExtractionResult { Success = false, Message = "JavaScript sonucu çözümlenemedi." };
    }

    private void ClearCurrentResults()
    {
        UnhookSegmentRows();
        _lastExtraction = null;
        _rows.Clear();
        _table1Rows.Clear();
        _table2Rows.Clear();
        _segments.Clear();
        _currentSelectedRecord = null;
        _currentSelectedSegment = null;
        _currentSelectionSource = SelectionSource.None;
        _pendingEditorColor = null;
        JsonTextBox.Clear();
        Table1RankInput.Clear();
        Table2RankInput.Clear();

        _isSynchronizingSelections = true;
        RecordsGrid.SelectedItem = null;
        Table1Grid.SelectedItem = null;
        Table2Grid.SelectedItem = null;
        SegmentsGrid.SelectedItem = null;
        ColorPaletteGrid.SelectedItem = null;
        _isSynchronizingSelections = false;

        ResetPreview();
        ResetSelectedSegmentEditor();
    }

    private void FillGrids(ExtractionResult extraction)
    {
        _rows.Clear();

        foreach (var record in extraction.Records)
        {
            EnsureEditableSegments(record.Rank);
            EnsureEditableSegments(record.Time);
            EnsureEditableSegments(record.Mode);
            EnsureEditableSegments(record.By);
            EnsureEditableSegments(record.Server);

            _rows.Add(new OnlineRecordRowView(record));
        }

        if (_rows.Count > 0)
            RecordsGrid.SelectedIndex = 0;
        else
            ResetPreview();
    }

    private static void EnsureEditableSegments(CellData cell)
    {
        if (cell.Segments.Count > 0)
        {
            cell.Text = CellDataUtilities.BuildCellText(cell);
            return;
        }

        cell.Segments.Add(new TextSegment
        {
            Text = string.IsNullOrWhiteSpace(cell.Text) ? string.Empty : cell.Text,
            Color = "rgb(255, 255, 255)",
            BackgroundColor = "transparent",
            FontWeight = "400",
            FontStyle = "normal",
            TextDecoration = "none",
            FontFamily = "Segoe UI",
            FontSize = "14px",
            ClassName = string.Empty,
            Tag = "span"
        });

        cell.Text = CellDataUtilities.BuildCellText(cell);
    }

    private void RecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelections)
            return;

        if (RecordsGrid.SelectedItem is OnlineRecordRowView row)
        {
            SynchronizeActiveGrid(RecordsGrid);
            SelectRecord(row.Source, SelectionSource.FullRecord);
        }
    }

    private void Table1Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelections)
            return;

        if (Table1Grid.SelectedItem is RankTimeByRowView row)
        {
            SynchronizeActiveGrid(Table1Grid);
            SelectRecord(row.Source, SelectionSource.Table1);
        }
    }

    private void Table2Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelections)
            return;

        if (Table2Grid.SelectedItem is RankTimeByRowView row)
        {
            SynchronizeActiveGrid(Table2Grid);
            SelectRecord(row.Source, SelectionSource.Table2);
        }
    }

    private void SynchronizeActiveGrid(DataGrid activeGrid)
    {
        _isSynchronizingSelections = true;
        try
        {
            if (!ReferenceEquals(activeGrid, RecordsGrid)) RecordsGrid.SelectedItem = null;
            if (!ReferenceEquals(activeGrid, Table1Grid)) Table1Grid.SelectedItem = null;
            if (!ReferenceEquals(activeGrid, Table2Grid)) Table2Grid.SelectedItem = null;
        }
        finally
        {
            _isSynchronizingSelections = false;
        }
    }

    private void SelectRecord(OnlineRecord? record, SelectionSource source)
    {
        _currentSelectedRecord = record;
        _currentSelectionSource = source;

        _suppressSegmentChangeHandling = true;
        UnhookSegmentRows();
        _segments.Clear();

        if (record is not null)
        {
            AddSegments("#", record.Rank);
            AddSegments("Time", record.Time);

            if (source == SelectionSource.FullRecord)
                AddSegments("Mode", record.Mode);

            AddSegments("By", record.By);

            if (source == SelectionSource.FullRecord)
                AddSegments("Server", record.Server);
        }

        _suppressSegmentChangeHandling = false;

        RenderCurrentPreview();

        if (_segments.Count > 0)
            SegmentsGrid.SelectedIndex = 0;
        else
            ResetSelectedSegmentEditor();
    }

    private void AddSegments(string cellName, CellData cell)
    {
        EnsureEditableSegments(cell);

        foreach (var segment in cell.Segments)
        {
            var row = new SegmentRowView(cellName, cell, segment);
            row.PropertyChanged += SegmentRowView_PropertyChanged;
            _segments.Add(row);
        }
    }

    private void UnhookSegmentRows()
    {
        foreach (var segment in _segments)
            segment.PropertyChanged -= SegmentRowView_PropertyChanged;
    }

    private void SegmentRowView_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSegmentChangeHandling || sender is not SegmentRowView row)
            return;

        row.ApplyToSource();
        RefreshAllViewTexts();
        RenderCurrentPreview();
        RefreshJsonText();

        if (ReferenceEquals(row, _currentSelectedSegment))
            LoadSelectedSegmentEditor(row);
    }

    private void RefreshAllViewTexts()
    {
        foreach (var row in _rows)
            row.RefreshFromSource();

        foreach (var row in _table1Rows)
            row.RefreshFromSource();

        foreach (var row in _table2Rows)
            row.RefreshFromSource();
    }

    private void SegmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SegmentsGrid.SelectedItem is SegmentRowView row)
        {
            _currentSelectedSegment = row;
            LoadSelectedSegmentEditor(row);
        }
        else
        {
            _currentSelectedSegment = null;
            ResetSelectedSegmentEditor();
        }
    }

    private void LoadSelectedSegmentEditor(SegmentRowView row)
    {
        SelectedSegmentInfoText.Text = $"Alan: {row.CellName}  •  Tag: {Safe(row.Tag, "-")}  •  Class: {Safe(row.ClassName, "-")}";

        if (CssColorHelper.TryParse(row.Color, out var color))
            SetPendingEditorColor(color, true);
        else
            ResetEditorColorInputs();

        _suppressStyleSelectionEvent = true;
        SegmentStyleComboBox.SelectedIndex = row.FontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _suppressStyleSelectionEvent = false;
    }

    private static string Safe(string? text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private ColorPaletteItem? FindMatchingPalette(string? cssColor)
    {
        if (!CssColorHelper.TryParse(cssColor, out var selected))
            return null;

        return _colorPalette.FirstOrDefault(item => item.R == selected.R && item.G == selected.G && item.B == selected.B);
    }

    private void ResetSelectedSegmentEditor()
    {
        SelectedSegmentInfoText.Text = "Bir segment seç";
        ResetEditorColorInputs();

        _suppressStyleSelectionEvent = true;
        SegmentStyleComboBox.SelectedIndex = -1;
        _suppressStyleSelectionEvent = false;
    }

    private void ResetEditorColorInputs()
    {
        _pendingEditorColor = null;
        SelectedColorPreviewBorder.Background = Brushes.Transparent;
        RedTextBox.Text = string.Empty;
        GreenTextBox.Text = string.Empty;
        BlueTextBox.Text = string.Empty;

        _suppressPaletteApply = true;
        ColorPaletteGrid.SelectedItem = null;
        _suppressPaletteApply = false;
    }

    private void SetPendingEditorColor(Color color, bool syncPaletteSelection)
    {
        _pendingEditorColor = color;
        SelectedColorPreviewBorder.Background = new SolidColorBrush(color);
        RedTextBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        GreenTextBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        BlueTextBox.Text = color.B.ToString(CultureInfo.InvariantCulture);

        if (!syncPaletteSelection)
            return;

        _suppressPaletteApply = true;
        ColorPaletteGrid.SelectedItem = FindMatchingPalette(CssColorHelper.ToCssRgb(color));
        _suppressPaletteApply = false;
    }

    private void SegmentStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStyleSelectionEvent || _currentSelectedSegment is null)
            return;

        if (SegmentStyleComboBox.SelectedItem is ComboBoxItem comboItem)
            _currentSelectedSegment.FontStyle = comboItem.Content?.ToString() ?? "normal";
    }

    private void SelectedColorPreviewBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Color initialColor = _pendingEditorColor ?? Colors.White;

        if (_currentSelectedSegment is not null && CssColorHelper.TryParse(_currentSelectedSegment.Color, out var currentColor))
            initialColor = currentColor;

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = System.Drawing.Color.FromArgb(initialColor.R, initialColor.G, initialColor.B)
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            var chosen = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            SetPendingEditorColor(chosen, true);
            StatusText.Text = "Renk seçildi. Uygulamak için RGB Uygula butonuna bas.";
        }
    }

    private void ApplyRgbButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSelectedSegment is null)
        {
            MessageBox.Show("Önce bir segment seç.");
            return;
        }

        if (!TryReadRgb(out byte r, out byte g, out byte b))
        {
            MessageBox.Show("RGB alanlarına 0 ile 255 arasında sayılar gir.");
            return;
        }

        var selectedColor = Color.FromRgb(r, g, b);
        SetPendingEditorColor(selectedColor, true);
        _currentSelectedSegment.Color = CssColorHelper.ToCssRgb(selectedColor);
    }

    private void ColorPaletteGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPaletteApply)
            return;

        if (ColorPaletteGrid.SelectedItem is ColorPaletteItem item)
        {
            var color = Color.FromRgb(item.R, item.G, item.B);
            SetPendingEditorColor(color, false);
            StatusText.Text = "Renk tablosundan seçim yapıldı. Uygulamak için RGB Uygula butonuna bas.";
        }
    }

    private bool TryReadRgb(out byte r, out byte g, out byte b)
    {
        r = g = b = 0;

        return byte.TryParse(RedTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(GreenTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(BlueTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
    }

    private void RenderCurrentPreview()
    {
        if (_currentSelectedRecord is null)
        {
            ResetPreview();
            return;
        }

        RenderCellPreview(PreviewRankText, _currentSelectedRecord.Rank);
        RenderCellPreview(PreviewTimeText, _currentSelectedRecord.Time);
        RenderCellPreview(PreviewByText, _currentSelectedRecord.By);

        if (_currentSelectionSource == SelectionSource.FullRecord)
        {
            RenderCellPreview(PreviewModeText, _currentSelectedRecord.Mode);
            RenderCellPreview(PreviewServerText, _currentSelectedRecord.Server);
        }
        else
        {
            SetPlainPreviewText(PreviewModeText, "-");
            SetPlainPreviewText(PreviewServerText, "-");
        }
    }

    private void ResetPreview()
    {
        SetPlainPreviewText(PreviewRankText, "-");
        SetPlainPreviewText(PreviewTimeText, "-");
        SetPlainPreviewText(PreviewModeText, "-");
        SetPlainPreviewText(PreviewByText, "-");
        SetPlainPreviewText(PreviewServerText, "-");
    }

    private static void SetPlainPreviewText(TextBlock target, string text)
    {
        target.Inlines.Clear();
        target.Foreground = Brushes.White;
        target.Text = text;
    }

    private void RenderCellPreview(TextBlock target, CellData cell)
    {
        target.Text = string.Empty;
        target.Inlines.Clear();

        EnsureEditableSegments(cell);

        if (cell.Segments.Count == 0)
        {
            target.Text = string.IsNullOrWhiteSpace(cell.Text) ? "-" : cell.Text;
            target.Foreground = Brushes.White;
            return;
        }

        bool first = true;
        foreach (var segment in cell.Segments)
        {
            if (!first)
                target.Inlines.Add(new Run(" "));

            first = false;

            var run = new Run(segment.Text)
            {
                Foreground = CssColorHelper.ToBrush(segment.Color, Brushes.White),
                FontWeight = ToFontWeight(segment.FontWeight),
                FontStyle = ToFontStyle(segment.FontStyle),
                FontFamily = TryCreateFontFamily(segment.FontFamily) ?? target.FontFamily,
                FontSize = TryParseFontSize(segment.FontSize) ?? target.FontSize
            };

            if (!string.IsNullOrWhiteSpace(segment.TextDecoration) &&
                segment.TextDecoration.Contains("underline", StringComparison.OrdinalIgnoreCase))
            {
                run.TextDecorations = TextDecorations.Underline;
            }

            target.Inlines.Add(run);
        }
    }

    private static FontWeight ToFontWeight(string? cssValue)
    {
        if (int.TryParse(cssValue, out int weight))
        {
            if (weight >= 700) return FontWeights.Bold;
            if (weight >= 600) return FontWeights.SemiBold;
            if (weight <= 300) return FontWeights.Light;
        }

        return cssValue?.Contains("bold", StringComparison.OrdinalIgnoreCase) == true
            ? FontWeights.Bold
            : FontWeights.Normal;
    }

    private static FontStyle ToFontStyle(string? cssValue)
    {
        return cssValue?.Contains("italic", StringComparison.OrdinalIgnoreCase) == true
            ? FontStyles.Italic
            : FontStyles.Normal;
    }

    private static FontFamily? TryCreateFontFamily(string? cssValue)
    {
        if (string.IsNullOrWhiteSpace(cssValue))
            return null;

        try
        {
            var first = cssValue.Split(',')[0].Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(first) ? null : new FontFamily(first);
        }
        catch
        {
            return null;
        }
    }

    private static double? TryParseFontSize(string? cssValue)
    {
        if (string.IsNullOrWhiteSpace(cssValue))
            return null;

        cssValue = cssValue.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(cssValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? size
            : null;
    }

    private void RefreshJsonText()
    {
        JsonTextBox.Text = JsonSerializer.Serialize(_lastExtraction, JsonOptions);
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not BookmarkItem bookmark)
            return;

        _ = NavigateAsync(bookmark.Url);
    }

    private void AddBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        string url = GetCurrentUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Kaydedilecek bir URL bulunamadı.");
            return;
        }

        string normalizedUrl = NormalizeUrl(url);
        string title = BuildBookmarkTitle(normalizedUrl);

        var existing = _bookmarks.FirstOrDefault(b => string.Equals(b.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Title = title;
            SaveBookmarks();
            RefreshBookmarksBar();
            StatusText.Text = "Yer imi zaten vardı, başlığı güncellendi.";
            return;
        }

        _bookmarks.Add(new BookmarkItem { Title = title, Url = normalizedUrl });
        SaveBookmarks();
        StatusText.Text = $"Yer imi kaydedildi: {title}";
    }

    private void RemoveBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        string normalizedUrl = NormalizeUrl(GetCurrentUrl());
        var existing = _bookmarks.FirstOrDefault(b => string.Equals(b.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            MessageBox.Show("Bu URL için kayıtlı bir yer imi bulunamadı.");
            return;
        }

        _bookmarks.Remove(existing);
        SaveBookmarks();
        StatusText.Text = $"Yer imi silindi: {existing.Title}";
    }

    private string GetCurrentUrl()
    {
        return Browser.Source?.ToString()
               ?? Browser.CoreWebView2?.Source
               ?? UrlTextBox.Text
               ?? string.Empty;
    }

    private string BuildBookmarkTitle(string url)
    {
        if (!string.IsNullOrWhiteSpace(_currentPageTitle))
            return TrimForDisplay(_currentPageTitle, 42);

        try
        {
            var uri = new Uri(url);
            string title = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(title))
                title = uri.Host;
            else
                title = $"{uri.Host}{uri.AbsolutePath}";

            return TrimForDisplay(title, 42);
        }
        catch
        {
            return TrimForDisplay(url, 42);
        }
    }

    private static string TrimForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;

        return text[..(maxLength - 1)] + "…";
    }

    private void LoadBookmarks()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);

            if (!File.Exists(BookmarksFilePath))
                return;

            var loaded = JsonSerializer.Deserialize<List<BookmarkItem>>(File.ReadAllText(BookmarksFilePath), JsonOptions) ?? new List<BookmarkItem>();
            _bookmarks.Clear();

            foreach (var bookmark in loaded.Where(b => !string.IsNullOrWhiteSpace(b.Url)))
                _bookmarks.Add(bookmark);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Yer imleri yüklenemedi: {ex.Message}";
        }
    }

    private void SaveBookmarks()
    {
        Directory.CreateDirectory(AppDataDirectory);
        File.WriteAllText(BookmarksFilePath, JsonSerializer.Serialize(_bookmarks, JsonOptions));
        RefreshBookmarksBar();
    }

    private void RefreshBookmarksBar()
    {
        BookmarksItemsControl.ItemsSource = null;
        BookmarksItemsControl.ItemsSource = _bookmarks;
    }

    private async void AddTable1ItemButton_Click(object sender, RoutedEventArgs e)
    {
        await AddRowToCustomTableAsync(Table1RankInput.Text, _table1Rows, Table1Grid, "Tablo 1");
    }

    private async void AddTable2ItemButton_Click(object sender, RoutedEventArgs e)
    {
        await AddRowToCustomTableAsync(Table2RankInput.Text, _table2Rows, Table2Grid, "Tablo 2");
    }

    private async Task AddRowToCustomTableAsync(string rankInput, ObservableCollection<RankTimeByRowView> target, DataGrid targetGrid, string tableName)
    {
        await Task.Yield();

        if (_rows.Count == 0)
        {
            MessageBox.Show("Önce veriyi çekmelisin.");
            return;
        }

        int? requestedRank = RankParsingHelper.ParseRankNumber(rankInput);
        if (requestedRank is null)
        {
            MessageBox.Show("Kaynak rank için 1, 1st, 2nd gibi bir değer gir.");
            return;
        }

        var source = _rows
            .Select(r => r.Source)
            .FirstOrDefault(r => RankParsingHelper.ParseRankNumber(CellDataUtilities.BuildCellText(r.Rank)) == requestedRank.Value);

        if (source is null)
        {
            MessageBox.Show($"{requestedRank}. sıraya ait kayıt bulunamadı.");
            return;
        }

        var clone = OnlineRecordCloner.CloneRankTimeByRecord(source);
        target.Add(new RankTimeByRowView(clone, tableName));
        RenumberCustomTable(target);

        targetGrid.SelectedItem = target.LastOrDefault();
        StatusText.Text = $"{tableName} listesine kaynak rank {requestedRank} eklendi.";
    }

    private void RemoveTable1ItemButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedCustomRow(_table1Rows, Table1Grid, SelectionSource.Table1, "Tablo 1");
    }

    private void RemoveTable2ItemButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedCustomRow(_table2Rows, Table2Grid, SelectionSource.Table2, "Tablo 2");
    }

    private void RemoveSelectedCustomRow(ObservableCollection<RankTimeByRowView> target, DataGrid grid, SelectionSource source, string tableName)
    {
        if (grid.SelectedItem is not RankTimeByRowView selected)
        {
            MessageBox.Show("Silmek için önce tablodan bir satır seç.");
            return;
        }

        int selectedIndex = grid.SelectedIndex;
        target.Remove(selected);
        RenumberCustomTable(target);

        if (_currentSelectionSource == source && ReferenceEquals(_currentSelectedRecord, selected.Source))
        {
            if (target.Count > 0)
            {
                int nextIndex = Math.Clamp(selectedIndex, 0, target.Count - 1);
                grid.SelectedIndex = nextIndex;
            }
            else
            {
                _isSynchronizingSelections = true;
                grid.SelectedItem = null;
                _isSynchronizingSelections = false;
                _currentSelectedRecord = null;
                _currentSelectionSource = SelectionSource.None;
                _segments.Clear();
                ResetPreview();
                ResetSelectedSegmentEditor();
            }
        }

        StatusText.Text = $"{tableName} listesinden satır silindi.";
    }

    private void RenumberCustomTable(IEnumerable<RankTimeByRowView> rows)
    {
        int index = 1;
        foreach (var row in rows)
        {
            RankFormattingHelper.ApplyOrdinalRank(row.Source.Rank, index);
            row.RefreshFromSource();
            index++;
        }

        RefreshJsonText();
        RenderCurrentPreview();
    }

    private static string NormalizeUrl(string? rawUrl)
    {
        string url = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(url))
            return "https://tmnf.exchange/trackshow/9684537";

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        return url;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private const string JsExtractionScript = """
(() => {
    const expectedHeaders = ['#', 'Time', 'Mode', 'By', 'Server'];

    const normalize = (value) => (value || '').replace(/\s+/g, ' ').trim();
    const isRank = (value) => /^\d+(st|nd|rd|th)$/i.test(value) || /^\d+$/.test(value);

    function getStyle(element) {
        const style = window.getComputedStyle(element);
        return {
            color: style.color,
            backgroundColor: style.backgroundColor,
            fontWeight: style.fontWeight,
            fontStyle: style.fontStyle,
            textDecoration: style.textDecorationLine,
            fontFamily: style.fontFamily,
            fontSize: style.fontSize,
            className: element.className ? String(element.className) : '',
            tag: element.tagName ? element.tagName.toLowerCase() : ''
        };
    }

    function getTextSegments(root) {
        const segments = [];
        const walker = document.createTreeWalker(
            root,
            NodeFilter.SHOW_TEXT,
            {
                acceptNode(node) {
                    return normalize(node.textContent).length > 0
                        ? NodeFilter.FILTER_ACCEPT
                        : NodeFilter.FILTER_REJECT;
                }
            }
        );

        let current;
        while ((current = walker.nextNode())) {
            const parent = current.parentElement || root;
            const text = current.textContent.replace(/\s+/g, ' ').trim();
            if (!text) continue;

            segments.push({
                text,
                ...getStyle(parent)
            });
        }

        return segments;
    }

    function extractCell(cell) {
        return {
            text: normalize(cell.innerText || cell.textContent || ''),
            html: cell.innerHTML || '',
            segments: getTextSegments(cell)
        };
    }

    function extractRowsFromTable(table) {
        const rows = Array.from(table.querySelectorAll('tr'));
        const dataRows = [];

        for (const row of rows) {
            const cells = Array.from(row.querySelectorAll(':scope > th, :scope > td'));
            if (cells.length < 5)
                continue;

            const rowTexts = cells.map(cell => normalize(cell.textContent));
            const isHeader = expectedHeaders.every((header, index) => rowTexts[index] === header);
            if (isHeader)
                continue;

            if (!isRank(rowTexts[0]))
                continue;

            dataRows.push({
                rank: extractCell(cells[0]),
                time: extractCell(cells[1]),
                mode: extractCell(cells[2]),
                by: extractCell(cells[3]),
                server: extractCell(cells[4])
            });
        }

        return dataRows;
    }

    function findTableWithHeaders() {
        const tables = Array.from(document.querySelectorAll('table'));
        for (const table of tables) {
            const headerCells = Array.from(table.querySelectorAll('th, thead td'))
                .map(cell => normalize(cell.textContent));

            if (expectedHeaders.every(header => headerCells.includes(header)))
                return table;
        }

        return null;
    }

    function findHeaderRowElement() {
        const candidates = Array.from(document.querySelectorAll('tr, div'));
        for (const candidate of candidates) {
            const children = Array.from(candidate.children);
            if (children.length < 5)
                continue;

            const texts = children.slice(0, 5).map(child => normalize(child.textContent));
            const same = expectedHeaders.every((header, index) => texts[index] === header);
            if (same)
                return candidate;
        }

        return null;
    }

    function extractRowsFromGenericContainer(headerRow) {
        const parent = headerRow.parentElement;
        if (!parent)
            return [];

        const siblings = Array.from(parent.children);
        const startIndex = siblings.indexOf(headerRow);
        const rows = [];

        for (let i = startIndex + 1; i < siblings.length; i++) {
            const row = siblings[i];
            const cells = Array.from(row.children).filter(child => normalize(child.textContent).length > 0);
            if (cells.length < 5)
                continue;

            const firstCell = normalize(cells[0].textContent);
            if (!isRank(firstCell))
                continue;

            rows.push({
                rank: extractCell(cells[0]),
                time: extractCell(cells[1]),
                mode: extractCell(cells[2]),
                by: extractCell(cells[3]),
                server: extractCell(cells[4])
            });
        }

        return rows;
    }

    let records = [];
    let source = '';

    const table = findTableWithHeaders();
    if (table) {
        records = extractRowsFromTable(table);
        source = 'table';
    }

    if (!records.length) {
        const headerRow = findHeaderRowElement();
        if (headerRow) {
            records = extractRowsFromGenericContainer(headerRow);
            source = 'generic';
        }
    }

    return {
        success: records.length > 0,
        message: records.length > 0 ? `Bulundu (${source})` : 'Tablo bulunamadı',
        recordCount: records.length,
        records
    };
})();
""";
}

public enum SelectionSource
{
    None,
    FullRecord,
    Table1,
    Table2
}

public sealed class ExtractionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public List<OnlineRecord> Records { get; set; } = new();
}

public sealed class OnlineRecord
{
    public CellData Rank { get; set; } = new();
    public CellData Time { get; set; } = new();
    public CellData Mode { get; set; } = new();
    public CellData By { get; set; } = new();
    public CellData Server { get; set; } = new();
}

public sealed class CellData
{
    public string Text { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public List<TextSegment> Segments { get; set; } = new();
}

public sealed class TextSegment
{
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public string FontWeight { get; set; } = string.Empty;
    public string FontStyle { get; set; } = string.Empty;
    public string TextDecoration { get; set; } = string.Empty;
    public string FontFamily { get; set; } = string.Empty;
    public string FontSize { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}

public static class OnlineRecordCloner
{
    public static OnlineRecord CloneRankTimeByRecord(OnlineRecord source)
    {
        return new OnlineRecord
        {
            Rank = CloneCell(source.Rank),
            Time = CloneCell(source.Time),
            By = CloneCell(source.By),
            Mode = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() },
            Server = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() }
        };
    }

    public static CellData CloneCell(CellData source)
    {
        return new CellData
        {
            Text = source.Text,
            Html = source.Html,
            Segments = source.Segments.Select(CloneSegment).ToList()
        };
    }

    public static TextSegment CloneSegment(TextSegment source)
    {
        return new TextSegment
        {
            Text = source.Text,
            Color = source.Color,
            BackgroundColor = source.BackgroundColor,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            TextDecoration = source.TextDecoration,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            ClassName = source.ClassName,
            Tag = source.Tag
        };
    }
}

public static class RankFormattingHelper
{
    public static void ApplyOrdinalRank(CellData cell, int index)
    {
        string text = Ordinal(index);

        if (cell.Segments.Count == 0)
        {
            cell.Segments.Add(new TextSegment
            {
                Text = text,
                Color = "rgb(255, 255, 255)",
                BackgroundColor = "transparent",
                FontWeight = "400",
                FontStyle = "normal",
                TextDecoration = "none",
                FontFamily = "Segoe UI",
                FontSize = "14px",
                ClassName = string.Empty,
                Tag = "span"
            });
        }
        else
        {
            TextSegment template = OnlineRecordCloner.CloneSegment(cell.Segments[0]);
            template.Text = text;
            cell.Segments.Clear();
            cell.Segments.Add(template);
        }

        cell.Text = text;
    }

    public static string Ordinal(int number)
    {
        int abs = Math.Abs(number);
        int lastTwo = abs % 100;
        string suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : (abs % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };

        return $"{number}{suffix}";
    }
}

public static class RankParsingHelper
{
    private static readonly Regex RankRegex = new(@"^(\d+)", RegexOptions.Compiled);

    public static int? ParseRankNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = RankRegex.Match(text.Trim());
        if (!match.Success)
            return null;

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }
}

public static class CellDataUtilities
{
    public static string BuildCellText(CellData? cell)
    {
        if (cell is null)
            return string.Empty;

        if (cell.Segments.Count == 0)
            return cell.Text ?? string.Empty;

        return string.Join(" ", cell.Segments.Select(s => s.Text).Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
    }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class OnlineRecordRowView : ObservableObject
{
    public OnlineRecordRowView(OnlineRecord source)
    {
        Source = source;
        RefreshFromSource();
    }

    public OnlineRecord Source { get; }

    private string _rank = string.Empty;
    private string _time = string.Empty;
    private string _mode = string.Empty;
    private string _by = string.Empty;
    private string _server = string.Empty;

    public string Rank { get => _rank; private set => SetProperty(ref _rank, value); }
    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string Mode { get => _mode; private set => SetProperty(ref _mode, value); }
    public string By { get => _by; private set => SetProperty(ref _by, value); }
    public string Server { get => _server; private set => SetProperty(ref _server, value); }

    public void RefreshFromSource()
    {
        Rank = CellDataUtilities.BuildCellText(Source.Rank);
        Time = CellDataUtilities.BuildCellText(Source.Time);
        Mode = CellDataUtilities.BuildCellText(Source.Mode);
        By = CellDataUtilities.BuildCellText(Source.By);
        Server = CellDataUtilities.BuildCellText(Source.Server);
    }
}

public sealed class RankTimeByRowView : ObservableObject
{
    public RankTimeByRowView(OnlineRecord source, string tableName)
    {
        Source = source;
        TableName = tableName;
        RefreshFromSource();
    }

    public OnlineRecord Source { get; }
    public string TableName { get; }

    private string _rank = string.Empty;
    private string _time = string.Empty;
    private string _by = string.Empty;

    public string Rank { get => _rank; private set => SetProperty(ref _rank, value); }
    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string By { get => _by; private set => SetProperty(ref _by, value); }

    public void RefreshFromSource()
    {
        Rank = CellDataUtilities.BuildCellText(Source.Rank);
        Time = CellDataUtilities.BuildCellText(Source.Time);
        By = CellDataUtilities.BuildCellText(Source.By);
    }
}

public sealed class SegmentRowView : ObservableObject
{
    public SegmentRowView(string cellName, CellData sourceCell, TextSegment sourceSegment)
    {
        CellName = cellName;
        SourceCell = sourceCell;
        SourceSegment = sourceSegment;
        LoadFromSource();
    }

    public string CellName { get; }
    public CellData SourceCell { get; }
    public TextSegment SourceSegment { get; }

    private string _text = string.Empty;
    private string _color = string.Empty;
    private string _backgroundColor = string.Empty;
    private string _fontWeight = string.Empty;
    private string _fontStyle = string.Empty;
    private string _textDecoration = string.Empty;
    private string _fontFamily = string.Empty;
    private string _fontSize = string.Empty;
    private string _className = string.Empty;
    private string _tag = string.Empty;

    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public string Color { get => _color; set => SetProperty(ref _color, value); }
    public string BackgroundColor { get => _backgroundColor; set => SetProperty(ref _backgroundColor, value); }
    public string FontWeight { get => _fontWeight; set => SetProperty(ref _fontWeight, value); }
    public string FontStyle { get => _fontStyle; set => SetProperty(ref _fontStyle, value); }
    public string TextDecoration { get => _textDecoration; set => SetProperty(ref _textDecoration, value); }
    public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
    public string FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }
    public string ClassName { get => _className; set => SetProperty(ref _className, value); }
    public string Tag { get => _tag; set => SetProperty(ref _tag, value); }

    public void LoadFromSource()
    {
        Text = SourceSegment.Text;
        Color = SourceSegment.Color;
        BackgroundColor = SourceSegment.BackgroundColor;
        FontWeight = SourceSegment.FontWeight;
        FontStyle = SourceSegment.FontStyle;
        TextDecoration = SourceSegment.TextDecoration;
        FontFamily = SourceSegment.FontFamily;
        FontSize = SourceSegment.FontSize;
        ClassName = SourceSegment.ClassName;
        Tag = SourceSegment.Tag;
    }

    public void ApplyToSource()
    {
        SourceSegment.Text = Text ?? string.Empty;
        SourceSegment.Color = Color ?? string.Empty;
        SourceSegment.BackgroundColor = BackgroundColor ?? string.Empty;
        SourceSegment.FontWeight = FontWeight ?? string.Empty;
        SourceSegment.FontStyle = FontStyle ?? string.Empty;
        SourceSegment.TextDecoration = TextDecoration ?? string.Empty;
        SourceSegment.FontFamily = FontFamily ?? string.Empty;
        SourceSegment.FontSize = FontSize ?? string.Empty;
        SourceSegment.ClassName = ClassName ?? string.Empty;
        SourceSegment.Tag = Tag ?? string.Empty;
        SourceCell.Text = CellDataUtilities.BuildCellText(SourceCell);
    }
}

public sealed class BookmarkItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class ColorPaletteItem
{
    public string Name { get; set; } = string.Empty;
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public string CssColor { get; set; } = string.Empty;
    public string RgbText => $"{R}, {G}, {B}";
}

public sealed class CssColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return CssColorHelper.ToBrush(value?.ToString(), Brushes.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public static class CssColorHelper
{
    private static readonly Regex RgbRegex = new(@"rgba?\(([^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Brush ToBrush(string? cssColor, Brush fallback)
    {
        if (TryParse(cssColor, out var color))
            return new SolidColorBrush(color);

        return fallback;
    }

    public static string ToCssRgb(Color color)
    {
        return $"rgb({color.R}, {color.G}, {color.B})";
    }

    public static bool TryParse(string? cssColor, out Color color)
    {
        color = Colors.Transparent;

        if (string.IsNullOrWhiteSpace(cssColor))
            return false;

        cssColor = cssColor.Trim();

        if (cssColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Colors.Transparent;
            return true;
        }

        var rgbMatch = RgbRegex.Match(cssColor);
        if (rgbMatch.Success)
        {
            var parts = rgbMatch.Groups[1].Value.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length >= 3 &&
                byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
            {
                byte a = 255;
                if (parts.Length >= 4 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha))
                    a = (byte)Math.Clamp(alpha * 255.0, 0, 255);

                color = Color.FromArgb(a, r, g, b);
                return true;
            }
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(cssColor);
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
