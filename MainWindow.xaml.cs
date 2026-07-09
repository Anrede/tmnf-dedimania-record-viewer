using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;

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
            StatusText.Text = "Online records bölümü bekleniyor...";

            var extraction = await TryExtractAsync();
            _lastExtraction = extraction;

            JsonTextBox.Text = JsonSerializer.Serialize(extraction, JsonOptions);
            FillGrid(extraction);

            StatusText.Text = extraction.Success
                ? $"{extraction.Records.Count} kayıt çekildi."
                : $"Hata: {extraction.Message}";
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

    private async Task<ExtractionResult> TryExtractAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            if (_pageReady)
            {
                var result = await ExecuteExtractionScriptAsync();
                if (result.Success && result.Records.Count > 0)
                    return result;
            }

            await Task.Delay(1000);
        }

        return new ExtractionResult
        {
            Success = false,
            Message = "Online records tablosu bulunamadı. Sayfa yapısı değişmiş olabilir."
        };
    }

    private async Task<ExtractionResult> ExecuteExtractionScriptAsync()
    {
        string rawJson = await Browser.ExecuteScriptAsync(JsExtractionScript);
        return JsonSerializer.Deserialize<ExtractionResult>(rawJson, JsonOptions)
               ?? new ExtractionResult { Success = false, Message = "JavaScript sonucu çözümlenemedi." };
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
    }

    private void RecordsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _segments.Clear();

        if (RecordsGrid.SelectedItem is not OnlineRecordRowView selected || selected.Source is null)
            return;

        AddSegments("#", selected.Source.Rank);
        AddSegments("Time", selected.Source.Time);
        AddSegments("Mode", selected.Source.Mode);
        AddSegments("By", selected.Source.By);
        AddSegments("Server", selected.Source.Server);
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
