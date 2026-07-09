using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace TmnfDedimaniaScraper;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<OnlineRecordRowView> _rows = new();
    private readonly ObservableCollection<SegmentRowView> _segments = new();
    private ExtractionResult? _lastExtraction;
    private bool _pageReady;

    public MainWindow()
    {
        InitializeComponent();

        RecordsGrid.ItemsSource = _rows;
        SegmentsGrid.ItemsSource = _segments;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeBrowserAsync();
        await NavigateAsync(UrlTextBox.Text);
    }

    private async Task InitializeBrowserAsync()
    {
        StatusText.Text = "WebView2 başlatılıyor...";
        await Browser.EnsureCoreWebView2Async();
        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
        StatusText.Text = "WebView2 hazır.";
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _pageReady = e.IsSuccess;
        StatusText.Text = e.IsSuccess ? "Sayfa yüklendi." : "Sayfa yüklenemedi.";
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync(UrlTextBox.Text);
    }

    private async Task NavigateAsync(string url)
    {
        if (Browser.CoreWebView2 is null)
            return;

        _pageReady = false;
        StatusText.Text = "Sayfa açılıyor...";
        Browser.CoreWebView2.Navigate(url);
        await Task.CompletedTask;
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

            JsonTextBox.Text = JsonSerializer.Serialize(extraction, JsonOptions);

            if (extraction.Success)
                FillGrid(extraction);
            else
                ResetPreview();

            StatusText.Text = extraction.Success
                ? $"{extraction.Records.Count} kayıt çekildi."
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
        _lastExtraction = null;
        _rows.Clear();
        _segments.Clear();
        JsonTextBox.Clear();
        RecordsGrid.SelectedItem = null;
        ResetPreview();
    }

    private void FillGrid(ExtractionResult extraction)
    {
        _rows.Clear();
        _segments.Clear();

        foreach (var record in extraction.Records)
        {
            _rows.Add(new OnlineRecordRowView
            {
                Source = record,
                Rank = record.Rank.Text,
                Time = record.Time.Text,
                Mode = record.Mode.Text,
                By = record.By.Text,
                Server = record.Server.Text
            });
        }

        if (_rows.Count > 0)
            RecordsGrid.SelectedIndex = 0;
        else
            ResetPreview();
    }

    private void RecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _segments.Clear();

        if (RecordsGrid.SelectedItem is not OnlineRecordRowView selected || selected.Source is null)
        {
            ResetPreview();
            return;
        }

        AddSegments("#", selected.Source.Rank);
        AddSegments("Time", selected.Source.Time);
        AddSegments("Mode", selected.Source.Mode);
        AddSegments("By", selected.Source.By);
        AddSegments("Server", selected.Source.Server);

        RenderCellPreview(PreviewRankText, selected.Source.Rank);
        RenderCellPreview(PreviewTimeText, selected.Source.Time);
        RenderCellPreview(PreviewModeText, selected.Source.Mode);
        RenderCellPreview(PreviewByText, selected.Source.By);
        RenderCellPreview(PreviewServerText, selected.Source.Server);
    }

    private void ResetPreview()
    {
        PreviewRankText.Inlines.Clear();
        PreviewTimeText.Inlines.Clear();
        PreviewModeText.Inlines.Clear();
        PreviewByText.Inlines.Clear();
        PreviewServerText.Inlines.Clear();

        PreviewRankText.Text = "-";
        PreviewTimeText.Text = "-";
        PreviewModeText.Text = "-";
        PreviewByText.Text = "-";
        PreviewServerText.Text = "-";
    }

    private void RenderCellPreview(TextBlock target, CellData cell)
    {
        target.Text = string.Empty;
        target.Inlines.Clear();

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

        var match = Regex.Match(cssValue, @"[\d.]+");
        if (!match.Success)
            return null;

        if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return null;

        return px;
    }

    private void AddSegments(string cellName, CellData cell)
    {
        foreach (var segment in cell.Segments)
        {
            _segments.Add(new SegmentRowView
            {
                CellName = cellName,
                Text = segment.Text,
                Color = segment.Color,
                FontWeight = segment.FontWeight,
                FontStyle = segment.FontStyle,
                TextDecoration = segment.TextDecoration,
                ClassName = segment.ClassName
            });
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExtraction is null)
        {
            MessageBox.Show("Önce veri çekmelisin.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "dedimania-online-records.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_lastExtraction, JsonOptions));
        StatusText.Text = $"JSON kaydedildi: {dialog.FileName}";
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

public sealed class OnlineRecordRowView
{
    public string Rank { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string By { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public OnlineRecord? Source { get; set; }
}

public sealed class SegmentRowView
{
    public string CellName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string FontWeight { get; set; } = string.Empty;
    public string FontStyle { get; set; } = string.Empty;
    public string TextDecoration { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
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

    public static bool TryParse(string? cssColor, out Color color)
    {
        color = Colors.Transparent;

        if (string.IsNullOrWhiteSpace(cssColor))
            return false;

        cssColor = cssColor.Trim();

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
