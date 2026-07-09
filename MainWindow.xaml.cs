using Microsoft.Web.WebView2.Core;
using System.Collections.Specialized;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace TmnfDedimaniaScraper;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<OnlineRecordRowView> _rows = new();
    private readonly ObservableCollection<OfflineRecordRowView> _offlineRows = new();
    private readonly ObservableCollection<RankTimeByRowView> _table1Rows = new();
    private readonly ObservableCollection<RankTimeByRowView> _table2Rows = new();
    private readonly ObservableCollection<SegmentRowView> _segments = new();
    private readonly ObservableCollection<PreviewRecordRowView> _previewRows = new();
    private readonly ObservableCollection<BookmarkItem> _bookmarks = new();

    private ExtractionResult? _lastExtraction;
    private OnlineRecord? _currentSelectedRecord;
    private OfflineRecord? _currentSelectedOfflineRecord;
    private SegmentRowView? _currentSelectedSegment;
    private SelectionSource _currentSelectionSource = SelectionSource.None;

    private bool _pageReady;
    private string _currentPageTitle = string.Empty;
    private bool _isSynchronizingSelections;
    private bool _suppressSegmentChangeHandling;
    private bool _suppressStyleSelectionEvent;
    private Color? _pendingEditorColor;
    private string _currentTrackName = string.Empty;
    private bool _showOfflineRecords;
    private RecordsDisplayWindow? _recordsDisplayWindow;
    private RecordsDisplaySettingsWindow? _recordsDisplaySettingsWindow;
    private RecordsDisplaySettings _recordsDisplaySettings = RecordsDisplaySettings.CreateDefault();

    private const string AppFolderName = "TmnfDedimaniaScraper";
    private static string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
    private static string BookmarksFilePath => Path.Combine(AppDataDirectory, "bookmarks.json");
    private static string AppStateFilePath => Path.Combine(AppDataDirectory, "appstate.json");

    private readonly DispatcherTimer _saveStateTimer;
    private bool _isRestoringState;
    private AppState? _loadedAppState;
    private bool _columnStateTrackingAttached;
    private string _lastImportExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private readonly Stack<AppState> _undoStack = new();
    private readonly Stack<AppState> _redoStack = new();
    private bool _isApplyingHistoryState;
    private const int MaxHistoryStates = 100;

    private System.Windows.Interop.HwndSource? _hotkeySource;

    public MainWindow()
    {
        InitializeComponent();

        _saveStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _saveStateTimer.Tick += SaveStateTimer_Tick;

        RecordsGrid.ItemsSource = _rows;
        OfflineRecordsGrid.ItemsSource = _offlineRows;
        Table1Grid.ItemsSource = _table1Rows;
        Table2Grid.ItemsSource = _table2Rows;
        SegmentsGrid.ItemsSource = _segments;
        PreviewGrid.ItemsSource = _previewRows;
        ResetPreview();
        SetRecordsTab(false);
        UpdateMergeButtonState();
        BookmarksItemsControl.ItemsSource = _bookmarks;
        _table1Rows.CollectionChanged += CustomTableRows_CollectionChanged;
        _table2Rows.CollectionChanged += CustomTableRows_CollectionChanged;
        LoadBookmarks();
        _loadedAppState = LoadAppState();
        ApplyLoadedState(_loadedAppState);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        LocationChanged += WindowGeometryChanged;
        SizeChanged += WindowGeometryChanged;
        StateChanged += WindowGeometryChanged;
        PreviewKeyDown += Window_PreviewKeyDown;

        SourceInitialized += MainWindow_SourceInitialized;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AttachColumnStateTracking();
        ApplyDeferredLoadedState(_loadedAppState);

        await InitializeBrowserAsync();
        await NavigateAsync(UrlTextBox.Text);
    }

    private void SaveStateTimer_Tick(object? sender, EventArgs e)
    {
        _saveStateTimer.Stop();
        SaveAppStateImmediate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_recordsDisplaySettingsWindow is not null)
        {
            _recordsDisplaySettingsWindow.Close();
            _recordsDisplaySettingsWindow = null;
        }

        if (_recordsDisplayWindow is not null)
        {
            _recordsDisplayWindow.Close();
            _recordsDisplayWindow = null;
        }

        UnregisterGlobalHotkeys();

        SaveAppStateImmediate();
    }

    private void WindowGeometryChanged(object? sender, EventArgs e)
    {
        QueueSaveState();
    }

    private void QueueSaveState()
    {
        if (_isRestoringState || _isApplyingHistoryState)
            return;

        _saveStateTimer.Stop();
        _saveStateTimer.Start();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                RedoLastChange();
            else
                UndoLastChange();

            e.Handled = true;
        }
    }

    private void OnlineTabButton_Click(object sender, RoutedEventArgs e) => SetRecordsTab(false);

    private void OfflineTabButton_Click(object sender, RoutedEventArgs e) => SetRecordsTab(true);

    private void SetRecordsTab(bool showOffline)
    {
        _showOfflineRecords = showOffline;
        if (RecordsGrid is null || OfflineRecordsGrid is null)
            return;

        RecordsGrid.Visibility = showOffline ? Visibility.Collapsed : Visibility.Visible;
        OfflineRecordsGrid.Visibility = showOffline ? Visibility.Visible : Visibility.Collapsed;

        if (OnlineTabButton is not null)
        {
            OnlineTabButton.Opacity = showOffline ? 0.75 : 1.0;
            OnlineTabButton.IsEnabled = showOffline;
        }

        if (OfflineTabButton is not null)
        {
            OfflineTabButton.Opacity = showOffline ? 1.0 : 0.75;
            OfflineTabButton.IsEnabled = !showOffline;
        }

        QueueSaveState();
    }

    private void UpdateMergeButtonState()
    {
        if (MergeOfflineButton is null)
            return;

        MergeOfflineButton.IsEnabled = _offlineRows.Count > 0;
        MergeOfflineButton.Opacity = MergeOfflineButton.IsEnabled ? 1.0 : 0.55;
    }

    private void CustomTableRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRecordsDisplayWindow();
    }

    private void ShowRecordsSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recordsDisplaySettingsWindow is not null && _recordsDisplaySettingsWindow.IsVisible)
        {
            _recordsDisplaySettingsWindow.Close();
            return;
        }

        EnsureRecordsDisplaySettingsWindow();
        _recordsDisplaySettingsWindow!.Owner = this;
        _recordsDisplaySettingsWindow!.LoadSettings(_recordsDisplaySettings);
        _recordsDisplaySettingsWindow.Show();
        _recordsDisplaySettingsWindow.Activate();
    }

    private void RecordsDisplaySettingsWindow_SettingsApplied(RecordsDisplaySettings e)
    {
        _recordsDisplaySettings = RecordDisplaySettingsHelper.Sanitize(e);
        RenumberCustomTable(_table1Rows);
        RenumberCustomTable(_table2Rows);
        UpdateRecordsDisplayWindow();
        QueueSaveState();
    }

    private void EnsureRecordsDisplaySettingsWindow()
    {
        if (_recordsDisplaySettingsWindow is not null)
            return;

        _recordsDisplaySettingsWindow = new RecordsDisplaySettingsWindow();
        _recordsDisplaySettingsWindow.SettingsApplied += RecordsDisplaySettingsWindow_SettingsApplied;
        _recordsDisplaySettingsWindow.Closed += RecordsDisplaySettingsWindow_Closed;
    }

    private void RecordsDisplaySettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (_recordsDisplaySettingsWindow is not null)
        {
            _recordsDisplaySettings = RecordDisplaySettingsHelper.Sanitize(_recordsDisplaySettingsWindow.GetSettingsForPersistence());
            _recordsDisplaySettingsWindow.SettingsApplied -= RecordsDisplaySettingsWindow_SettingsApplied;
            _recordsDisplaySettingsWindow.Closed -= RecordsDisplaySettingsWindow_Closed;
            QueueSaveState();
        }

        _recordsDisplaySettingsWindow = null;
    }

    private void ShowRecordsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleRecordsOverlayVisibility();
    }

    private void EnsureRecordsDisplayWindow()
    {
        if (_recordsDisplayWindow is not null)
            return;

        _recordsDisplayWindow = new RecordsDisplayWindow();
        _recordsDisplayWindow.Closed += RecordsDisplayWindow_Closed;
    }

    private void RecordsDisplayWindow_Closed(object? sender, EventArgs e)
    {
        if (_recordsDisplayWindow is not null)
            _recordsDisplayWindow.Closed -= RecordsDisplayWindow_Closed;

        _recordsDisplayWindow = null;
    }

    private void UpdateRecordsDisplayWindow()
    {
        if (_recordsDisplayWindow is null)
            return;

        _recordsDisplayWindow.SetTables(_table1Rows, _table2Rows, _currentTrackName, _recordsDisplaySettings);
    }


    private void ToggleRecordsOverlayVisibility()
    {
        EnsureRecordsDisplayWindow();
        UpdateRecordsDisplayWindow();

        if (_recordsDisplayWindow is null)
            return;

        if (!_recordsDisplayWindow.IsVisible)
        {
            _recordsDisplayWindow.Show();
            _recordsDisplayWindow.SetOverlayMode(true);
            return;
        }

        if (_recordsDisplayWindow.IsOverlayMode)
        {
            _recordsDisplayWindow.SetOverlayMode(false);
            _recordsDisplayWindow.Hide();
            return;
        }

        _recordsDisplayWindow.SetOverlayMode(true);
    }

    private void ToggleRecordsOverlayFromHotkey()
    {
        ToggleRecordsOverlayVisibility();
    }

    private void ToggleRecordsTableFromHotkey()
    {
        if (_recordsDisplayWindow is null || !_recordsDisplayWindow.IsVisible || !_recordsDisplayWindow.IsOverlayMode)
            return;

        UpdateRecordsDisplayWindow();
        _recordsDisplayWindow.ToggleVisibleTableFromExternalHotkey();
    }


    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        RegisterGlobalHotkeys();
    }

    private void RegisterGlobalHotkeys()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        _hotkeySource ??= System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        _hotkeySource?.RemoveHook(WndProc);
        _hotkeySource?.AddHook(WndProc);

        RegisterHotKey(hwnd, HOTKEY_ID_F2, 0, VK_F2);
        RegisterHotKey(hwnd, HOTKEY_ID_F3, 0, VK_F3);
    }

    private void UnregisterGlobalHotkeys()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(hwnd, HOTKEY_ID_F2);
            UnregisterHotKey(hwnd, HOTKEY_ID_F3);
        }

        if (_hotkeySource is not null)
        {
            _hotkeySource.RemoveHook(WndProc);
            _hotkeySource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_F2)
            {
                Dispatcher.BeginInvoke(new Action(ToggleRecordsOverlayFromHotkey));
                handled = true;
            }
            else if (id == HOTKEY_ID_F3)
            {
                Dispatcher.BeginInvoke(new Action(ToggleRecordsTableFromHotkey));
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void PushUndoSnapshot()
    {
        if (_isRestoringState || _isApplyingHistoryState)
            return;

        _undoStack.Push(CaptureCurrentState(includeWindowGeometry: false, includeLayoutAndColumns: false));
        while (_undoStack.Count > MaxHistoryStates)
        {
            var items = _undoStack.Take(MaxHistoryStates).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in items)
                _undoStack.Push(item);
        }

        _redoStack.Clear();
    }

    private void UndoLastChange()
    {
        if (_undoStack.Count == 0)
            return;

        var current = CaptureCurrentState(includeWindowGeometry: false, includeLayoutAndColumns: false);
        _redoStack.Push(current);
        var previous = _undoStack.Pop();
        RestoreUndoRedoState(previous);
        StatusText.Text = "Last change undone.";
    }

    private void RedoLastChange()
    {
        if (_redoStack.Count == 0)
            return;

        var current = CaptureCurrentState(includeWindowGeometry: false, includeLayoutAndColumns: false);
        _undoStack.Push(current);
        var next = _redoStack.Pop();
        RestoreUndoRedoState(next);
        StatusText.Text = "Last undone change restored.";
    }

    private void RestoreUndoRedoState(AppState state)
    {
        _isApplyingHistoryState = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(state.LastUrl))
                UrlTextBox.Text = NormalizeUrl(state.LastUrl);

            _currentPageTitle = state.LastPageTitle ?? string.Empty;
            _lastImportExportDirectory = NormalizeExistingDirectory(state.LastImportExportDirectory);
            _showOfflineRecords = state.ShowOfflineRecordsTab;
            _currentTrackName = state.TrackName ?? string.Empty;

            _bookmarks.Clear();
            foreach (var bookmark in state.Bookmarks.Where(b => !string.IsNullOrWhiteSpace(b.Url)))
                _bookmarks.Add(new BookmarkItem { Title = bookmark.Title ?? string.Empty, Url = bookmark.Url ?? string.Empty });
            RefreshBookmarksBar();
            RestoreResultState(state);
            SetRecordsTab(_showOfflineRecords);
            UpdateMergeButtonState();

            Table1RankInput.Text = state.Table1InsertText ?? string.Empty;
            Table2RankInput.Text = state.Table2InsertText ?? string.Empty;
            UpdateTrackNameHeader(state.TrackName);
            RestoreSelections(state.Selection);
            RefreshAllViewTexts();
            RenderCurrentPreview();
        }
        finally
        {
            _isApplyingHistoryState = false;
        }

        QueueSaveState();
    }


    private void AttachColumnStateTracking()
    {
        if (_columnStateTrackingAttached)
            return;

        _columnStateTrackingAttached = true;

        foreach (var grid in EnumerateStatefulGrids())
        {
            grid.ColumnReordered += StatefulGrid_ColumnStateChanged;

            foreach (var column in grid.Columns)
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
                descriptor?.AddValueChanged(column, StatefulGrid_ColumnWidthChanged);
            }
        }
    }

    private IEnumerable<DataGrid> EnumerateStatefulGrids()
    {
        yield return RecordsGrid;
        yield return OfflineRecordsGrid;
        yield return Table1Grid;
        yield return Table2Grid;
        yield return SegmentsGrid;
        yield return PreviewGrid;
    }

    private void StatefulGrid_ColumnStateChanged(object? sender, EventArgs e)
    {
        QueueSaveState();
    }

    private void StatefulGrid_ColumnWidthChanged(object? sender, EventArgs e)
    {
        QueueSaveState();
    }

    private AppState? LoadAppState()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            if (!File.Exists(AppStateFilePath))
                return null;

            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(AppStateFilePath), JsonOptions);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load state: {ex.Message}";
            return null;
        }
    }

    private void ApplyLoadedState(AppState? state)
    {
        if (state is null)
            return;

        _isRestoringState = true;
        try
        {
            ApplyWindowState(state);
            ApplyLayoutLengths(state.LayoutLengths);

            if (!string.IsNullOrWhiteSpace(state.LastUrl))
                UrlTextBox.Text = NormalizeUrl(state.LastUrl);

            if (!string.IsNullOrWhiteSpace(state.LastPageTitle))
                _currentPageTitle = state.LastPageTitle;

            _bookmarks.Clear();
            foreach (var bookmark in state.Bookmarks.Where(b => !string.IsNullOrWhiteSpace(b.Url)))
                _bookmarks.Add(new BookmarkItem { Title = bookmark.Title ?? string.Empty, Url = bookmark.Url ?? string.Empty });
            RefreshBookmarksBar();

            _lastImportExportDirectory = NormalizeExistingDirectory(state.LastImportExportDirectory);
            _showOfflineRecords = state.ShowOfflineRecordsTab;
            _recordsDisplaySettings = RecordDisplaySettingsHelper.Sanitize(state.RecordsDisplaySettings);
            RestoreResultState(state);
            SetRecordsTab(_showOfflineRecords);
            UpdateMergeButtonState();

            Table1RankInput.Text = state.Table1InsertText ?? string.Empty;
            Table2RankInput.Text = state.Table2InsertText ?? string.Empty;
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    private void ApplyDeferredLoadedState(AppState? state)
    {
        if (state is null)
            return;

        _isRestoringState = true;
        try
        {
            ApplyColumnStates(state.ColumnWidths);
            RestoreSelections(state.Selection);
            UpdateMergeButtonState();
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    private void ApplyWindowState(AppState state)
    {
        if (state.WindowWidth > 400)
            Width = state.WindowWidth;
        if (state.WindowHeight > 300)
            Height = state.WindowHeight;
        if (!double.IsNaN(state.WindowLeft))
            Left = state.WindowLeft;
        if (!double.IsNaN(state.WindowTop))
            Top = state.WindowTop;

        WindowState = string.Equals(state.WindowState, nameof(System.Windows.WindowState.Maximized), StringComparison.OrdinalIgnoreCase)
            ? WindowState.Maximized
            : WindowState.Normal;
    }

    private void RestoreResultState(AppState state)
    {
        UnhookSegmentRows();
        _rows.Clear();
        _offlineRows.Clear();
        _table1Rows.Clear();
        _table2Rows.Clear();
        _segments.Clear();
        _currentSelectedRecord = null;
        _currentSelectedOfflineRecord = null;
        _currentSelectedSegment = null;
        _currentSelectionSource = SelectionSource.None;

        _lastExtraction = state.LastExtraction is null ? null : ExtractionResultCloner.Clone(state.LastExtraction);

        if (_lastExtraction?.Records is not null)
        {
            foreach (var record in _lastExtraction.Records)
            {
                EnsureEditableSegments(record.Rank);
                EnsureEditableSegments(record.Time);
                EnsureEditableSegments(record.Mode);
                EnsureEditableSegments(record.By);
                EnsureEditableSegments(record.Server);
                _rows.Add(new OnlineRecordRowView(record));
            }
        }

        if (_lastExtraction?.OfflineRecords is not null)
        {
            foreach (var record in _lastExtraction.OfflineRecords.Take(10))
            {
                EnsureEditableSegments(record.Rank);
                EnsureEditableSegments(record.Time);
                EnsureEditableSegments(record.By);
                EnsureEditableSegments(record.Score);
                EnsureEditableSegments(record.LB);
                _offlineRows.Add(new OfflineRecordRowView(record));
            }
        }

        foreach (var record in state.Table1Rows.Select(OnlineRecordCloner.CloneRankTimeByRecord))
        {
            EnsureEditableSegments(record.Rank);
            EnsureEditableSegments(record.Time);
            EnsureEditableSegments(record.By);
            _table1Rows.Add(new RankTimeByRowView(record, "Table 1"));
        }

        foreach (var record in state.Table2Rows.Select(OnlineRecordCloner.CloneRankTimeByRecord))
        {
            EnsureEditableSegments(record.Rank);
            EnsureEditableSegments(record.Time);
            EnsureEditableSegments(record.By);
            _table2Rows.Add(new RankTimeByRowView(record, "Table 2"));
        }

        RenumberCustomTable(_table1Rows);
        RenumberCustomTable(_table2Rows);

        UpdateTrackNameHeader(state.TrackName);
        UpdatePreviewColumnsVisibility();

        if (_rows.Count == 0 && _offlineRows.Count == 0 && _table1Rows.Count == 0 && _table2Rows.Count == 0)
            ResetPreview();
    }

    private void RestoreSelections(SelectionState? selection)
    {
        if (selection is null)
            return;

        int recordsIndex = CoerceIndex(selection.RecordsSelectedIndex, _rows.Count);
        int offlineRecordsIndex = CoerceIndex(selection.OfflineRecordsSelectedIndex, _offlineRows.Count);
        int table1Index = CoerceIndex(selection.Table1SelectedIndex, _table1Rows.Count);
        int table2Index = CoerceIndex(selection.Table2SelectedIndex, _table2Rows.Count);

        switch (selection.Source)
        {
            case SelectionSource.Table1 when table1Index >= 0:
                Table1Grid.SelectedIndex = table1Index;
                break;
            case SelectionSource.Table2 when table2Index >= 0:
                Table2Grid.SelectedIndex = table2Index;
                break;
            case SelectionSource.OfflineRecord when offlineRecordsIndex >= 0:
                OfflineRecordsGrid.SelectedIndex = offlineRecordsIndex;
                break;
            case SelectionSource.FullRecord when recordsIndex >= 0:
                RecordsGrid.SelectedIndex = recordsIndex;
                break;
            default:
                if (recordsIndex >= 0)
                    RecordsGrid.SelectedIndex = recordsIndex;
                else if (offlineRecordsIndex >= 0)
                    OfflineRecordsGrid.SelectedIndex = offlineRecordsIndex;
                break;
        }

        if (selection.SegmentsSelectedIndex >= 0 && selection.SegmentsSelectedIndex < _segments.Count)
            SegmentsGrid.SelectedIndex = selection.SegmentsSelectedIndex;
    }

    private static int CoerceIndex(int index, int count)
    {
        return index >= 0 && index < count ? index : -1;
    }

    private void ApplyColumnStates(List<GridColumnState> columnStates)
    {
        if (columnStates.Count == 0)
            return;

        foreach (var grid in EnumerateStatefulGrids())
        {
            string gridKey = GetGridStateKey(grid);
            var gridStates = columnStates.Where(c => string.Equals(c.GridKey, gridKey, StringComparison.Ordinal)).ToList();
            if (gridStates.Count == 0)
                continue;

            foreach (var column in grid.Columns)
            {
                string header = column.Header?.ToString() ?? string.Empty;
                var saved = gridStates.FirstOrDefault(c => string.Equals(c.Header, header, StringComparison.Ordinal));
                if (saved is null)
                    continue;

                if (saved.Width > 0)
                    column.Width = new DataGridLength(saved.Width);
            }

            foreach (var saved in gridStates.Where(c => c.DisplayIndex >= 0).OrderBy(c => c.DisplayIndex))
            {
                var column = grid.Columns.FirstOrDefault(c => string.Equals(c.Header?.ToString() ?? string.Empty, saved.Header, StringComparison.Ordinal));
                if (column is null)
                    continue;

                try
                {
                    column.DisplayIndex = Math.Clamp(saved.DisplayIndex, 0, grid.Columns.Count - 1);
                }
                catch
                {
                    // Ignore invalid restore orders from older state files.
                }
            }
        }
    }

    private void SaveAppStateImmediate()
    {
        if (_isRestoringState || _isApplyingHistoryState)
            return;

        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var state = CaptureCurrentState(includeWindowGeometry: true, includeLayoutAndColumns: true);
            File.WriteAllText(AppStateFilePath, JsonSerializer.Serialize(state, JsonOptions));
            File.WriteAllText(BookmarksFilePath, JsonSerializer.Serialize(_bookmarks, JsonOptions));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Durum kaydedilemedi: {ex.Message}";
        }
    }

    private AppState CaptureCurrentState(bool includeWindowGeometry, bool includeLayoutAndColumns)
    {
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var state = new AppState
        {
            WindowLeft = includeWindowGeometry ? SanitizeFiniteDouble(bounds.Left) : 0,
            WindowTop = includeWindowGeometry ? SanitizeFiniteDouble(bounds.Top) : 0,
            WindowWidth = includeWindowGeometry ? SanitizeFiniteDouble(bounds.Width) : 0,
            WindowHeight = includeWindowGeometry ? SanitizeFiniteDouble(bounds.Height) : 0,
            WindowState = WindowState == System.Windows.WindowState.Maximized ? nameof(System.Windows.WindowState.Maximized) : nameof(System.Windows.WindowState.Normal),
            LastUrl = GetCurrentUrl(),
            LastPageTitle = _currentPageTitle,
            TrackName = _currentTrackName,
            LastExtraction = _lastExtraction is null ? null : ExtractionResultCloner.Clone(_lastExtraction),
            Table1Rows = _table1Rows.Select(r => OnlineRecordCloner.CloneRankTimeByRecord(r.Source)).ToList(),
            Table2Rows = _table2Rows.Select(r => OnlineRecordCloner.CloneRankTimeByRecord(r.Source)).ToList(),
            Bookmarks = _bookmarks.Select(b => new BookmarkStateItem { Title = b.Title, Url = b.Url }).ToList(),
            Table1InsertText = Table1RankInput.Text ?? string.Empty,
            Table2InsertText = Table2RankInput.Text ?? string.Empty,
            LastImportExportDirectory = _lastImportExportDirectory,
            ShowOfflineRecordsTab = _showOfflineRecords,
            RecordsDisplaySettings = RecordsDisplaySettingsCloner.Clone(_recordsDisplaySettings),
            Selection = new SelectionState
            {
                Source = _currentSelectionSource,
                RecordsSelectedIndex = RecordsGrid.SelectedIndex,
                OfflineRecordsSelectedIndex = OfflineRecordsGrid.SelectedIndex,
                Table1SelectedIndex = Table1Grid.SelectedIndex,
                Table2SelectedIndex = Table2Grid.SelectedIndex,
                SegmentsSelectedIndex = SegmentsGrid.SelectedIndex
            },
            ColumnWidths = includeLayoutAndColumns ? CaptureColumnWidths() : new List<GridColumnState>(),
            LayoutLengths = includeLayoutAndColumns ? CaptureLayoutLengths() : new List<LayoutLengthState>()
        };

        return DeepCloneState(state);
    }

    private static AppState DeepCloneState(AppState state)
    {
        return JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, JsonOptions), JsonOptions) ?? new AppState();
    }


    private static double SanitizeFiniteDouble(double value)
    {
        return double.IsFinite(value) ? value : 0d;
    }

    private static string NormalizeExistingDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            return directory;

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private List<GridColumnState> CaptureColumnWidths()
    {
        var result = new List<GridColumnState>();

        foreach (var grid in EnumerateStatefulGrids())
        {
            string gridKey = GetGridStateKey(grid);
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                result.Add(new GridColumnState
                {
                    GridKey = gridKey,
                    ColumnIndex = i,
                    DisplayIndex = grid.Columns[i].DisplayIndex,
                    Header = grid.Columns[i].Header?.ToString() ?? string.Empty,
                    Width = SanitizeFiniteDouble(grid.Columns[i].ActualWidth > 0 ? grid.Columns[i].ActualWidth : grid.Columns[i].Width.DisplayValue)
                });
            }
        }

        return result;
    }

    private static string GetGridStateKey(DataGrid grid)
    {
        return grid.Name ?? string.Empty;
    }

    private void LayoutSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        QueueSaveState();
    }

    private List<LayoutLengthState> CaptureLayoutLengths()
    {
        return new List<LayoutLengthState>
        {
            new() { Key = nameof(LeftMainColumn), Value = SerializeGridLength(LeftMainColumn.Width) },
            new() { Key = nameof(RightMainColumn), Value = SerializeGridLength(RightMainColumn.Width) },
            new() { Key = nameof(LeftBrowserRow), Value = SerializeGridLength(LeftBrowserRow.Height) },
            new() { Key = nameof(LeftSegmentsRow), Value = SerializeGridLength(LeftSegmentsRow.Height) },
            new() { Key = nameof(RightRecordsRow), Value = SerializeGridLength(RightRecordsRow.Height) },
            new() { Key = nameof(RightTablesRow), Value = SerializeGridLength(RightTablesRow.Height) },
            new() { Key = nameof(RightPreviewRow), Value = SerializeGridLength(RightPreviewRow.Height) }
        };
    }

    private void ApplyLayoutLengths(List<LayoutLengthState>? states)
    {
        if (states is null || states.Count == 0)
            return;

        ApplyGridLength(states, nameof(LeftMainColumn), value => LeftMainColumn.Width = value);
        ApplyGridLength(states, nameof(RightMainColumn), value => RightMainColumn.Width = value);
        ApplyGridLength(states, nameof(LeftBrowserRow), value => LeftBrowserRow.Height = value);
        ApplyGridLength(states, nameof(LeftSegmentsRow), value => LeftSegmentsRow.Height = value);
        ApplyGridLength(states, nameof(RightRecordsRow), value => RightRecordsRow.Height = value);
        ApplyGridLength(states, nameof(RightTablesRow), value => RightTablesRow.Height = value);
        ApplyGridLength(states, nameof(RightPreviewRow), value => RightPreviewRow.Height = value);
    }

    private static void ApplyGridLength(IEnumerable<LayoutLengthState> states, string key, Action<GridLength> apply)
    {
        var raw = states.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal))?.Value;
        if (TryParseGridLength(raw, out var value))
            apply(value);
    }

    private static string SerializeGridLength(GridLength value)
    {
        return value.GridUnitType switch
        {
            GridUnitType.Star => $"{value.Value.ToString(CultureInfo.InvariantCulture)}*",
            GridUnitType.Auto => "Auto",
            _ => value.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static bool TryParseGridLength(string? raw, out GridLength result)
    {
        result = new GridLength(1, GridUnitType.Star);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();
        if (string.Equals(raw, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            result = GridLength.Auto;
            return true;
        }

        if (raw.EndsWith("*", StringComparison.Ordinal))
        {
            string starPart = raw[..^1];
            if (string.IsNullOrWhiteSpace(starPart))
                starPart = "1";

            if (double.TryParse(starPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double starValue) && starValue > 0)
            {
                result = new GridLength(starValue, GridUnitType.Star);
                return true;
            }

            return false;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double pixelValue) && pixelValue >= 0)
        {
            result = new GridLength(pixelValue, GridUnitType.Pixel);
            return true;
        }

        return false;
    }


    private async Task InitializeBrowserAsync()
    {
        StatusText.Text = "Initializing WebView2...";
        await Browser.EnsureCoreWebView2Async();
        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
        Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        Browser.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
        UpdateNavigationButtons();
        StatusText.Text = "WebView2 ready.";
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _pageReady = e.IsSuccess;
        UpdateNavigationButtons();
        StatusText.Text = e.IsSuccess ? "Page loaded." : "Page failed to load.";
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
            QueueSaveState();
        });
    }

    private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
    {
        Dispatcher.Invoke(() =>
        {
            _currentPageTitle = Browser.CoreWebView2?.DocumentTitle ?? string.Empty;
            QueueSaveState();
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
        StatusText.Text = "Opening page...";
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
        StatusText.Text = "Reloading page...";
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
                MessageBox.Show("WebView2 is not ready yet.");
                return;
            }

            ScrapeButton.IsEnabled = false;
            PushUndoSnapshot();
            ClearCurrentResults();
            StatusText.Text = "Searching for online/offline records (max 3 seconds)...";

            var extraction = await TryExtractAsync(TimeSpan.FromSeconds(3));
            _lastExtraction = extraction;

            UpdateTrackNameHeader(extraction.TrackName);

            if (extraction.Success)
                FillGrids(extraction);
            else
                ResetPreview();

            StatusText.Text = extraction.Success
                ? $"Fetched Online: {extraction.Records.Count}, Offline: {extraction.OfflineRecords.Count}."
                : $"No results: {extraction.Message}";

            QueueSaveState();
        }
        catch (Exception ex)
        {
            StatusText.Text = "An error occurred while fetching data.";
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var onlineResult = await ExecuteOnlineExtractionScriptAsync();
                var offlineResult = await ExecuteOfflineExtractionScriptAsync();
                var merged = MergeExtractionResults(onlineResult, offlineResult);
                if (merged.Success && (merged.Records.Count > 0 || merged.OfflineRecords.Count > 0))
                    return merged;
            }

            await Task.Delay(500);
        }

        return new ExtractionResult
        {
            Success = false,
            Message = "Could not find the online/offline records table within 3 seconds. The track may not have records, or the page structure may have changed."
        };
    }

    private async Task<ExtractionResult> ExecuteOnlineExtractionScriptAsync()
    {
        try
        {
            string rawJson = await Browser.ExecuteScriptAsync(JsExtractionScript);
            return JsonSerializer.Deserialize<ExtractionResult>(rawJson, JsonOptions)
                   ?? new ExtractionResult { Success = false, Message = "Could not parse the online JavaScript result." };
        }
        catch (Exception ex)
        {
            return new ExtractionResult { Success = false, Message = $"Online script error: {ex.Message}" };
        }
    }

    private async Task<OfflineExtractionResult> ExecuteOfflineExtractionScriptAsync()
    {
        try
        {
            string rawJson = await Browser.ExecuteScriptAsync(JsOfflineExtractionScript);
            return JsonSerializer.Deserialize<OfflineExtractionResult>(rawJson, JsonOptions)
                   ?? new OfflineExtractionResult { Success = false, Message = "Could not parse the offline JavaScript result." };
        }
        catch (Exception ex)
        {
            return new OfflineExtractionResult { Success = false, Message = $"Offline script error: {ex.Message}" };
        }
    }

    private static ExtractionResult MergeExtractionResults(ExtractionResult online, OfflineExtractionResult offline)
    {
        string trackName = !string.IsNullOrWhiteSpace(online.TrackName) ? online.TrackName : offline.TrackName;
        return new ExtractionResult
        {
            Success = (online.Success && online.Records.Count > 0) || (offline.Success && offline.Records.Count > 0),
            Message = (online.Success && online.Records.Count > 0) || (offline.Success && offline.Records.Count > 0)
                ? $"Online: {online.Records.Count} / Offline: {offline.Records.Count}"
                : (!string.IsNullOrWhiteSpace(online.Message) ? online.Message : offline.Message),
            TrackName = trackName,
            RecordCount = online.Records.Count,
            OfflineRecordCount = (offline.Records ?? new List<OfflineRecord>()).Take(10).Count(),
            Records = online.Records ?? new List<OnlineRecord>(),
            OfflineRecords = (offline.Records ?? new List<OfflineRecord>()).Take(10).ToList()
        };
    }

    private void ClearCurrentResults()
    {
        UnhookSegmentRows();
        _lastExtraction = null;
        _rows.Clear();
        _offlineRows.Clear();
        _table1Rows.Clear();
        _table2Rows.Clear();
        _segments.Clear();
        _currentSelectedRecord = null;
        _currentSelectedOfflineRecord = null;
        _currentSelectedSegment = null;
        _currentSelectionSource = SelectionSource.None;
        _pendingEditorColor = null;
        Table1RankInput.Clear();
        Table2RankInput.Clear();

        _isSynchronizingSelections = true;
        RecordsGrid.SelectedItem = null;
        OfflineRecordsGrid.SelectedItem = null;
        Table1Grid.SelectedItem = null;
        Table2Grid.SelectedItem = null;
        SegmentsGrid.SelectedItem = null;
        _isSynchronizingSelections = false;

        ResetPreview();
        ResetSelectedSegmentEditor();
        UpdateMergeButtonState();
        UpdateRecordsDisplayWindow();
    }

    private void FillGrids(ExtractionResult extraction)
    {
        _rows.Clear();
        _offlineRows.Clear();
        _table1Rows.Clear();
        _table2Rows.Clear();

        foreach (var record in extraction.Records)
        {
            EnsureEditableSegments(record.Rank);
            EnsureEditableSegments(record.Time);
            EnsureEditableSegments(record.Mode);
            EnsureEditableSegments(record.By);
            EnsureEditableSegments(record.Server);

            _rows.Add(new OnlineRecordRowView(record));
        }

        foreach (var record in extraction.OfflineRecords.Take(10))
        {
            EnsureEditableSegments(record.Rank);
            EnsureEditableSegments(record.Time);
            EnsureEditableSegments(record.By);
            EnsureEditableSegments(record.Score);
            EnsureEditableSegments(record.LB);
            _offlineRows.Add(new OfflineRecordRowView(record));
        }

        PopulateOnlineCustomTables(extraction.Records);
        UpdateMergeButtonState();

        if (_rows.Count > 0)
        {
            RecordsGrid.SelectedIndex = 0;
            SetRecordsTab(false);
        }
        else if (_offlineRows.Count > 0)
        {
            OfflineRecordsGrid.SelectedIndex = 0;
            SetRecordsTab(true);
        }
        else
        {
            ResetPreview();
        }

        UpdateRecordsDisplayWindow();
    }

    private void PopulateOnlineCustomTables(IEnumerable<OnlineRecord> onlineRecords)
    {
        _table1Rows.Clear();
        _table2Rows.Clear();

        foreach (var record in onlineRecords.Select(OnlineRecordCloner.CloneRankTimeByRecord))
        {
            _table1Rows.Add(new RankTimeByRowView(OnlineRecordCloner.CloneRankTimeByRecord(record), "Table 1"));
            _table2Rows.Add(new RankTimeByRowView(OnlineRecordCloner.CloneRankTimeByRecord(record), "Table 2"));
        }

        RenumberCustomTable(_table1Rows);
        RenumberCustomTable(_table2Rows);
    }

    private void MergeOfflineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExtraction is null)
        {
            MessageBox.Show("Fetch the data first.");
            return;
        }

        if ((_lastExtraction.OfflineRecords?.Count ?? 0) == 0)
        {
            MessageBox.Show("No offline records were found to merge.");
            return;
        }

        PushUndoSnapshot();
        ReplaceCustomTableWithMergedData(_table1Rows, "Table 1");
        ReplaceCustomTableWithMergedData(_table2Rows, "Table 2");

        if (_table1Rows.Count > 0 && Table1Grid.SelectedIndex < 0)
            Table1Grid.SelectedIndex = 0;
        if (_table2Rows.Count > 0 && Table2Grid.SelectedIndex < 0)
            Table2Grid.SelectedIndex = 0;

        StatusText.Text = $"Offline records merged into Table 1 and Table 2 ({_lastExtraction.OfflineRecords.Count} offline).";
        UpdateRecordsDisplayWindow();
        QueueSaveState();
    }

    private void ReplaceCustomTableWithMergedData(ObservableCollection<RankTimeByRowView> target, string tableName)
    {
        if (_lastExtraction is null)
            return;

        List<OnlineRecord> records = target.Count == 0
            ? BuildOfflineOnlyRankTimeByRecords(_lastExtraction.OfflineRecords)
            : BuildMergedRankTimeByRecords(_lastExtraction.Records, _lastExtraction.OfflineRecords);

        target.Clear();
        foreach (var record in records)
            target.Add(new RankTimeByRowView(OnlineRecordCloner.CloneRankTimeByRecord(record), tableName));

        RenumberCustomTable(target);
    }

    private static List<OnlineRecord> BuildOfflineOnlyRankTimeByRecords(IEnumerable<OfflineRecord> offlineRecords)
    {
        return offlineRecords
            .Take(10)
            .Select(ConvertOfflineRecordToRankTimeByRecord)
            .OrderBy(record => NormalizeComparableTime(GetComparableTimeText(record, isOfflineSource: false)) ?? int.MaxValue)
            .ToList();
    }

    private static List<OnlineRecord> BuildMergedRankTimeByRecords(IEnumerable<OnlineRecord> onlineRecords, IEnumerable<OfflineRecord> offlineRecords)
    {
        var onlineList = onlineRecords.Select(OnlineRecordCloner.CloneRankTimeByRecord).ToList();
        var offlineList = offlineRecords.Take(10).ToList();

        if (onlineList.Count == 0)
        {
            return offlineList
                .Select(ConvertOfflineRecordToRankTimeByRecord)
                .OrderBy(record => NormalizeComparableTime(GetComparableTimeText(record, isOfflineSource: false)) ?? int.MaxValue)
                .ToList();
        }

        var onlineTimes = new HashSet<int>(
            onlineList
                .Select(record => NormalizeComparableTime(GetComparableTimeText(record, isOfflineSource: false)))
                .Where(value => value.HasValue)
                .Select(value => value!.Value));

        var mergedItems = new List<MergedRankTimeByItem>();

        for (int i = 0; i < onlineList.Count; i++)
        {
            var record = onlineList[i];
            mergedItems.Add(new MergedRankTimeByItem(
                record,
                NormalizeComparableTime(GetComparableTimeText(record, isOfflineSource: false)),
                i,
                IsOffline: false));
        }

        for (int i = 0; i < offlineList.Count; i++)
        {
            var source = offlineList[i];
            string comparableTimeText = GetComparableTimeText(source, isOfflineSource: true);
            int? normalizedTime = NormalizeComparableTime(comparableTimeText);

            if (normalizedTime.HasValue && onlineTimes.Contains(normalizedTime.Value))
                continue;

            mergedItems.Add(new MergedRankTimeByItem(
                ConvertOfflineRecordToRankTimeByRecord(source),
                normalizedTime,
                i,
                IsOffline: true));
        }

        return mergedItems
            .OrderBy(item => item.NormalizedTime ?? int.MaxValue)
            .ThenBy(item => item.OriginalIndex)
            .ThenBy(item => item.IsOffline ? 1 : 0)
            .Select(item => item.Record)
            .ToList();
    }

    private static string GetComparableTimeText(OnlineRecord record, bool isOfflineSource)
    {
        return isOfflineSource
            ? BuildPrimaryOfflineTimeText(record.Time)
            : CellDataUtilities.BuildCellText(record.Time);
    }

    private static string GetComparableTimeText(OfflineRecord record, bool isOfflineSource)
    {
        return isOfflineSource
            ? BuildPrimaryOfflineTimeText(record.Time)
            : CellDataUtilities.BuildCellText(record.Time);
    }

    private static string BuildPrimaryOfflineTimeText(CellData timeCell)
    {
        if (timeCell.Segments.Count > 0)
        {
            foreach (var segment in timeCell.Segments)
            {
                string text = segment.Text?.Trim() ?? string.Empty;
                if (IsPrimaryTimeSegmentText(text))
                    return text;
            }

            string firstSegmentText = timeCell.Segments[0].Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(firstSegmentText))
                return firstSegmentText;
        }

        return CellDataUtilities.BuildCellText(timeCell).Trim();
    }

    private static bool IsPrimaryTimeSegmentText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return Regex.IsMatch(text, @"^\d{2}:\d{2}\.\d{2}$");
    }

    private static int? NormalizeComparableTime(string? rawTime)
    {
        string digits = new string((rawTime ?? string.Empty)
            .Where(char.IsDigit)
            .ToArray());

        if (string.IsNullOrWhiteSpace(digits))
            return null;

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static OnlineRecord ConvertOfflineRecordToRankTimeByRecord(OfflineRecord source)
    {
        var converted = new OnlineRecord
        {
            Rank = OnlineRecordCloner.CloneCell(source.Rank),
            Time = ClonePrimaryOfflineTimeCell(source.Time),
            By = OnlineRecordCloner.CloneCell(source.By),
            Mode = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() },
            Server = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() }
        };

        converted.Time.Text = BuildPrimaryOfflineTimeText(converted.Time);
        ApplyOfflineCustomTableStyling(converted.Time);
        ApplyOfflineCustomTableStyling(converted.By);
        return converted;
    }

    private static void ApplyOfflineCustomTableStyling(CellData cell)
    {
        const string targetColor = "rgb(238, 238, 238)";

        if (cell.Segments.Count == 0)
        {
            cell.Segments.Add(new TextSegment
            {
                Text = cell.Text ?? string.Empty,
                Color = targetColor,
                BackgroundColor = "rgba(0, 0, 0, 0)",
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
            foreach (var segment in cell.Segments)
                segment.Color = targetColor;
        }

        cell.Text = CellDataUtilities.BuildCellText(cell);
    }

    private static CellData ClonePrimaryOfflineTimeCell(CellData source)
    {
        var clone = new CellData
        {
            Text = string.Empty,
            Html = source.Html,
            Segments = new List<TextSegment>()
        };

        TextSegment? primarySegment = source.Segments
            .Select(OnlineRecordCloner.CloneSegment)
            .FirstOrDefault(segment => IsPrimaryTimeSegmentText(segment.Text?.Trim() ?? string.Empty));

        if (primarySegment is not null)
        {
            primarySegment.Text = primarySegment.Text?.Trim() ?? string.Empty;
            clone.Segments.Add(primarySegment);
        }
        else if (source.Segments.Count > 0)
        {
            TextSegment fallback = OnlineRecordCloner.CloneSegment(source.Segments[0]);
            fallback.Text = fallback.Text?.Trim() ?? string.Empty;
            clone.Segments.Add(fallback);
        }

        clone.Text = clone.Segments.Count > 0
            ? clone.Segments[0].Text
            : BuildPrimaryOfflineTimeText(source);

        return clone;
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
            SelectOnlineRecord(row.Source, SelectionSource.FullRecord);
            QueueSaveState();
        }
    }

    private void OfflineRecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelections)
            return;

        if (OfflineRecordsGrid.SelectedItem is OfflineRecordRowView row)
        {
            SynchronizeActiveGrid(OfflineRecordsGrid);
            SelectOfflineRecord(row.Source);
            QueueSaveState();
        }
    }

    private void Table1Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelections)
            return;

        if (Table1Grid.SelectedItem is RankTimeByRowView row)
        {
            SynchronizeActiveGrid(Table1Grid);
            SelectOnlineRecord(row.Source, SelectionSource.Table1);
            QueueSaveState();
        }
    }

    private void Table2Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSelections)
            return;

        if (Table2Grid.SelectedItem is RankTimeByRowView row)
        {
            SynchronizeActiveGrid(Table2Grid);
            SelectOnlineRecord(row.Source, SelectionSource.Table2);
            QueueSaveState();
        }
    }

    private void SynchronizeActiveGrid(DataGrid activeGrid)
    {
        _isSynchronizingSelections = true;
        try
        {
            if (!ReferenceEquals(activeGrid, RecordsGrid)) RecordsGrid.SelectedItem = null;
            if (!ReferenceEquals(activeGrid, OfflineRecordsGrid)) OfflineRecordsGrid.SelectedItem = null;
            if (!ReferenceEquals(activeGrid, Table1Grid)) Table1Grid.SelectedItem = null;
            if (!ReferenceEquals(activeGrid, Table2Grid)) Table2Grid.SelectedItem = null;
        }
        finally
        {
            _isSynchronizingSelections = false;
        }
    }

    private void SelectOnlineRecord(OnlineRecord? record, SelectionSource source)
    {
        _currentSelectedRecord = record;
        _currentSelectedOfflineRecord = null;
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
        UpdatePreviewColumnsVisibility();
        RenderCurrentPreview();

        if (_segments.Count > 0)
            SegmentsGrid.SelectedIndex = 0;
        else
            ResetSelectedSegmentEditor();
    }

    private void SelectOfflineRecord(OfflineRecord? record)
    {
        _currentSelectedOfflineRecord = record;
        _currentSelectedRecord = null;
        _currentSelectionSource = SelectionSource.OfflineRecord;

        _suppressSegmentChangeHandling = true;
        UnhookSegmentRows();
        _segments.Clear();

        if (record is not null)
        {
            AddSegments("#", record.Rank);
            AddSegments("Time", record.Time);
            AddSegments("By", record.By);
            AddSegments("Score", record.Score);
            AddSegments("LB", record.LB);
        }

        _suppressSegmentChangeHandling = false;
        UpdatePreviewColumnsVisibility();
        RenderCurrentPreview();

        if (_segments.Count > 0)
            SegmentsGrid.SelectedIndex = 0;
        else
            ResetSelectedSegmentEditor();
    }

    private void UpdatePreviewColumnsVisibility()
    {
        bool showOfflineColumns = _currentSelectionSource == SelectionSource.OfflineRecord;
        bool showModeAndServerColumns = _currentSelectionSource == SelectionSource.FullRecord;

        if (PreviewModeColumn is not null) PreviewModeColumn.Visibility = showModeAndServerColumns ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewServerColumn is not null) PreviewServerColumn.Visibility = showModeAndServerColumns ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewScoreColumn is not null) PreviewScoreColumn.Visibility = showOfflineColumns ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewLBColumn is not null) PreviewLBColumn.Visibility = showOfflineColumns ? Visibility.Visible : Visibility.Collapsed;
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

        if (ReferenceEquals(row, _currentSelectedSegment))
            LoadSelectedSegmentEditor(row);

        QueueSaveState();
    }

    private void RefreshAllViewTexts()
    {
        foreach (var row in _rows)
            row.RefreshFromSource();

        foreach (var row in _offlineRows)
            row.RefreshFromSource();

        foreach (var row in _table1Rows)
            row.RefreshFromSource();

        foreach (var row in _table2Rows)
            row.RefreshFromSource();

        UpdateRecordsDisplayWindow();
    }

    private void SegmentsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        PushUndoSnapshot();
    }

    private void SegmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SegmentsGrid.SelectedItem is SegmentRowView row)
        {
            _currentSelectedSegment = row;
            LoadSelectedSegmentEditor(row);
            QueueSaveState();
        }
        else
        {
            _currentSelectedSegment = null;
            ResetSelectedSegmentEditor();
        }
    }

    private void LoadSelectedSegmentEditor(SegmentRowView row)
    {
        if (CssColorHelper.TryParse(row.Color, out var color))
            SetPendingEditorColor(color, true);
        else
            ResetEditorColorInputs();

        _suppressStyleSelectionEvent = true;
        SetSegmentStyleMenuContent(row.FontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase) ? "italic" : "normal");
        _suppressStyleSelectionEvent = false;
    }


    private void ResetSelectedSegmentEditor()
    {
        ResetEditorColorInputs();

        _suppressStyleSelectionEvent = true;
        SetSegmentStyleMenuContent(null);
        _suppressStyleSelectionEvent = false;
    }

    private void SetSegmentStyleMenuContent(string? style)
    {
        string label = string.IsNullOrWhiteSpace(style) ? "Stil" : style.Trim();
        SegmentStyleMenuButton.Content = $"{label} ▾";
    }

    private void SegmentStyleMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void SegmentStyleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressStyleSelectionEvent || _currentSelectedSegment is null || sender is not MenuItem menuItem)
            return;

        string nextStyle = (menuItem.Header?.ToString() ?? "normal").Trim();
        string currentStyle = _currentSelectedSegment.FontStyle?.Trim() ?? string.Empty;
        if (string.Equals(currentStyle, nextStyle, StringComparison.OrdinalIgnoreCase))
        {
            SetSegmentStyleMenuContent(nextStyle);
            return;
        }

        PushUndoSnapshot();
        _currentSelectedSegment.FontStyle = nextStyle;
        SetSegmentStyleMenuContent(nextStyle);
    }

    private void ResetEditorColorInputs()
    {
        _pendingEditorColor = null;
        SelectedColorPreviewBorder.Background = Brushes.Transparent;
        RedTextBox.Text = string.Empty;
        GreenTextBox.Text = string.Empty;
        BlueTextBox.Text = string.Empty;

    }

    private void SetPendingEditorColor(Color color, bool syncPaletteSelection)
    {
        _pendingEditorColor = color;
        SelectedColorPreviewBorder.Background = new SolidColorBrush(color);
        RedTextBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        GreenTextBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        BlueTextBox.Text = color.B.ToString(CultureInfo.InvariantCulture);

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
            StatusText.Text = "Color selected. Click Apply RGB to apply it.";
        }
    }

    private void ApplyRgbButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSelectedSegment is null)
        {
            MessageBox.Show("Select a segment first.");
            return;
        }

        if (!TryReadRgb(out byte r, out byte g, out byte b))
        {
            MessageBox.Show("Enter RGB values between 0 and 255.");
            return;
        }

        var selectedColor = Color.FromRgb(r, g, b);
        string nextCss = CssColorHelper.ToCssRgb(selectedColor);
        if (string.Equals(_currentSelectedSegment.Color, nextCss, StringComparison.OrdinalIgnoreCase))
        {
            SetPendingEditorColor(selectedColor, true);
            return;
        }

        PushUndoSnapshot();
        SetPendingEditorColor(selectedColor, true);
        _currentSelectedSegment.Color = nextCss;
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
        _previewRows.Clear();
        UpdatePreviewColumnsVisibility();

        if (_currentSelectionSource == SelectionSource.OfflineRecord)
        {
            if (_currentSelectedOfflineRecord is null)
            {
                _previewRows.Add(PreviewRecordRowView.CreatePlaceholder(true));
                return;
            }

            _previewRows.Add(PreviewRecordRowView.FromOfflineRecord(_currentSelectedOfflineRecord));
            return;
        }

        if (_currentSelectedRecord is null)
        {
            _previewRows.Add(PreviewRecordRowView.CreatePlaceholder(false));
            return;
        }

        _previewRows.Add(PreviewRecordRowView.FromRecord(_currentSelectedRecord, _currentSelectionSource));
    }

    private void ResetPreview()
    {
        _previewRows.Clear();
        UpdatePreviewColumnsVisibility();
        _previewRows.Add(PreviewRecordRowView.CreatePlaceholder(_currentSelectionSource == SelectionSource.OfflineRecord));
    }

    private static FontWeight ToFontWeight(string? cssValue) => MainWindowFontConverters.ToFontWeight(cssValue);

    private static FontStyle ToFontStyle(string? cssValue) => MainWindowFontConverters.ToFontStyle(cssValue);

    private static FontFamily? TryCreateFontFamily(string? cssValue) => MainWindowFontConverters.TryCreateFontFamily(cssValue);

    private static double? TryParseFontSize(string? cssValue) => MainWindowFontConverters.TryParseFontSize(cssValue);

    private void RefreshJsonText()
    {
    }

    private void UpdateTrackNameHeader(string? trackName)
    {
        _currentTrackName = (trackName ?? string.Empty).Trim();
        TrackNameHeaderText.Text = string.IsNullOrWhiteSpace(_currentTrackName)
            ? "  •  Track name not found"
            : $"  •  {_currentTrackName}";

        QueueSaveState();
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
            MessageBox.Show("There is no URL to save.");
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
            StatusText.Text = "Bookmark already existed; its title was updated.";
            QueueSaveState();
            return;
        }

        _bookmarks.Add(new BookmarkItem { Title = title, Url = normalizedUrl });
        SaveBookmarks();
        StatusText.Text = $"Bookmark saved: {title}";
        QueueSaveState();
    }

    private void RemoveBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        string normalizedUrl = NormalizeUrl(GetCurrentUrl());
        var existing = _bookmarks.FirstOrDefault(b => string.Equals(b.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            MessageBox.Show("No saved bookmark was found for this URL.");
            return;
        }

        _bookmarks.Remove(existing);
        SaveBookmarks();
        StatusText.Text = $"Bookmark removed: {existing.Title}";
        QueueSaveState();
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
            StatusText.Text = $"Bookmarks could not be loaded: {ex.Message}";
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


    private void Table1MenuButton_Click(object sender, RoutedEventArgs e)
    {
        OpenButtonContextMenu(sender);
    }

    private void RecordsMenuButton_Click(object sender, RoutedEventArgs e)
    {
        OpenButtonContextMenu(sender);
    }

    private void Table2MenuButton_Click(object sender, RoutedEventArgs e)
    {
        OpenButtonContextMenu(sender);
    }

    private static void OpenButtonContextMenu(object sender)
    {
        if (sender is not Button button || button.ContextMenu is null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void ExportTable1MenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExportCustomTable(_table1Rows, 1, "Table 1");
    }

    private void ExportTable2MenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExportCustomTable(_table2Rows, 2, "Table 2");
    }

    private void ImportTable1MenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportCustomTable(_table1Rows, Table1Grid, 1, "Table 1", SelectionSource.Table1);
    }

    private void ImportTable2MenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportCustomTable(_table2Rows, Table2Grid, 2, "Table 2", SelectionSource.Table2);
    }

    private void ExportRecordsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExportRecordsTable();
    }

    private void ImportRecordsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportRecordsTable();
    }

    private void ExportRecordsTable()
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("Fetched records cannot be exported because the table is empty.");
            return;
        }

        string safeTrackName = GetSafeTrackNameForFileName();
        var dialog = new SaveFileDialog
        {
            Title = "Export fetched records",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{safeTrackName} - records.txt",
            InitialDirectory = NormalizeExistingDirectory(_lastImportExportDirectory),
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _lastImportExportDirectory = NormalizeExistingDirectory(Path.GetDirectoryName(dialog.FileName));

        var payload = new TableFilePayload
        {
            TrackName = _currentTrackName,
            TableName = "Fetched records",
            ExportedAt = DateTime.Now,
            Rows = _rows.Select(r => OnlineRecordCloner.CloneFullRecord(r.Source)).ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName)!);
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload, JsonOptions));
        StatusText.Text = $"Fetched records exported: {Path.GetFileName(dialog.FileName)}";
        QueueSaveState();
    }

    private void ImportRecordsTable()
    {
        string safeTrackName = GetSafeTrackNameForFileName();
        var dialog = new OpenFileDialog
        {
            Title = "Import fetched records",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{safeTrackName} - records.txt",
            InitialDirectory = NormalizeExistingDirectory(_lastImportExportDirectory),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _lastImportExportDirectory = NormalizeExistingDirectory(Path.GetDirectoryName(dialog.FileName));
        PushUndoSnapshot();

        try
        {
            string json = File.ReadAllText(dialog.FileName);
            List<OnlineRecord> rows;
            string importedTrackName = string.Empty;

            var payload = JsonSerializer.Deserialize<TableFilePayload>(json, JsonOptions);
            if (payload?.Rows?.Count > 0)
            {
                rows = payload.Rows;
                importedTrackName = payload.TrackName ?? string.Empty;
            }
            else
            {
                rows = JsonSerializer.Deserialize<List<OnlineRecord>>(json, JsonOptions) ?? new List<OnlineRecord>();
            }

            var extraction = new ExtractionResult
            {
                Success = rows.Count > 0,
                Message = rows.Count > 0 ? "Imported" : "No records found in file",
                TrackName = importedTrackName,
                RecordCount = rows.Count,
                Records = rows.Select(OnlineRecordCloner.CloneFullRecord).ToList()
            };

            ClearCurrentResults();
            _lastExtraction = extraction;
            UpdateTrackNameHeader(importedTrackName);

            if (extraction.Success)
                FillGrids(extraction);
            else
                ResetPreview();

            StatusText.Text = $"Fetched records imported: {Path.GetFileName(dialog.FileName)}";
            QueueSaveState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCustomTable(ObservableCollection<RankTimeByRowView> rows, int tableNumber, string tableName)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show($"{tableName} cannot be exported because it is empty.");
            return;
        }

        string safeTrackName = GetSafeTrackNameForFileName();
        var dialog = new SaveFileDialog
        {
            Title = $"Export {tableName}",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{safeTrackName} - table{tableNumber}.txt",
            InitialDirectory = NormalizeExistingDirectory(_lastImportExportDirectory),
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _lastImportExportDirectory = NormalizeExistingDirectory(Path.GetDirectoryName(dialog.FileName));

        var payload = new CompactTableFilePayload
        {
            TrackName = _currentTrackName,
            TableName = tableName,
            ExportedAt = DateTime.Now,
            Rows = rows.Select(r => CompactRankTimeByRecord.FromRecord(r.Source)).ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName)!);
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload, JsonOptions));
        StatusText.Text = $"{tableName} exported: {Path.GetFileName(dialog.FileName)}";
        QueueSaveState();
    }

    private void ImportCustomTable(ObservableCollection<RankTimeByRowView> target, DataGrid grid, int tableNumber, string tableName, SelectionSource selectionSource)
    {
        string safeTrackName = GetSafeTrackNameForFileName();
        var dialog = new OpenFileDialog
        {
            Title = $"Import {tableName}",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{safeTrackName} - table{tableNumber}.txt",
            InitialDirectory = NormalizeExistingDirectory(_lastImportExportDirectory),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _lastImportExportDirectory = NormalizeExistingDirectory(Path.GetDirectoryName(dialog.FileName));
        PushUndoSnapshot();

        try
        {
            string json = File.ReadAllText(dialog.FileName);
            List<OnlineRecord> rows;
            string importedTrackName = string.Empty;

            var compactPayload = JsonSerializer.Deserialize<CompactTableFilePayload>(json, JsonOptions);
            if (compactPayload?.Rows?.Count > 0)
            {
                rows = compactPayload.Rows.Select(r => r.ToOnlineRecord()).ToList();
                importedTrackName = compactPayload.TrackName ?? string.Empty;
            }
            else
            {
                var payload = JsonSerializer.Deserialize<TableFilePayload>(json, JsonOptions);
                if (payload?.Rows?.Count > 0)
                {
                    rows = payload.Rows;
                    importedTrackName = payload.TrackName ?? string.Empty;
                }
                else
                {
                    rows = JsonSerializer.Deserialize<List<OnlineRecord>>(json, JsonOptions) ?? new List<OnlineRecord>();
                }
            }

            target.Clear();

            foreach (var sourceRecord in rows)
            {
                var record = OnlineRecordCloner.CloneRankTimeByRecord(sourceRecord);
                EnsureEditableSegments(record.Rank);
                EnsureEditableSegments(record.Time);
                EnsureEditableSegments(record.By);
                target.Add(new RankTimeByRowView(record, tableName));
            }

            RenumberCustomTable(target);

            if (!string.IsNullOrWhiteSpace(importedTrackName))
                UpdateTrackNameHeader(importedTrackName);

            if (target.Count > 0)
            {
                grid.SelectedIndex = 0;
            }
            else if (_currentSelectionSource == selectionSource)
            {
                _currentSelectedRecord = null;
                _currentSelectionSource = SelectionSource.None;
                _segments.Clear();
                ResetPreview();
                ResetSelectedSegmentEditor();
            }

            StatusText.Text = $"{tableName} imported: {Path.GetFileName(dialog.FileName)}";
            QueueSaveState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetSafeTrackNameForFileName()
    {
        string raw = string.IsNullOrWhiteSpace(_currentTrackName) ? "track" : _currentTrackName;
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string safe = new string(raw.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "track" : safe;
    }

    private void AddTable1ItemButton_Click(object sender, RoutedEventArgs e)
    {
        InsertBlankRowIntoCustomTable(Table1RankInput.Text, _table1Rows, Table1Grid, "Table 1");
    }

    private void AddTable2ItemButton_Click(object sender, RoutedEventArgs e)
    {
        InsertBlankRowIntoCustomTable(Table2RankInput.Text, _table2Rows, Table2Grid, "Table 2");
    }

    private void InsertBlankRowIntoCustomTable(string rankInput, ObservableCollection<RankTimeByRowView> target, DataGrid targetGrid, string tableName)
    {
        int? requestedPosition = RankParsingHelper.ParseRankNumber(rankInput);
        if (requestedPosition is null)
        {
            MessageBox.Show("Enter an insert position such as 1, 2, or 3.");
            return;
        }

        int insertIndex = Math.Clamp(requestedPosition.Value - 1, 0, target.Count);
        PushUndoSnapshot();
        OnlineRecord blankRecord = OnlineRecordFactory.CreateBlankRankTimeByRecord();

        target.Insert(insertIndex, new RankTimeByRowView(blankRecord, tableName));
        RenumberCustomTable(target);

        targetGrid.SelectedIndex = insertIndex;
        StatusText.Text = $"A blank record was added to {tableName} at position {insertIndex + 1}.";
        QueueSaveState();
    }

    private void RemoveTable1ItemButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedCustomRow(_table1Rows, Table1Grid, SelectionSource.Table1, "Table 1");
    }

    private void RemoveTable2ItemButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedCustomRow(_table2Rows, Table2Grid, SelectionSource.Table2, "Table 2");
    }

    private void RemoveSelectedCustomRow(ObservableCollection<RankTimeByRowView> target, DataGrid grid, SelectionSource source, string tableName)
    {
        if (grid.SelectedItem is not RankTimeByRowView selected)
        {
            MessageBox.Show("Select a row in the table first.");
            return;
        }

        int selectedIndex = grid.SelectedIndex;
        PushUndoSnapshot();
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

        StatusText.Text = $"A row was removed from {tableName}.";
        QueueSaveState();
    }

    private void RenumberCustomTable(IEnumerable<RankTimeByRowView> rows)
    {
        int index = 1;
        foreach (var row in rows)
        {
            RankFormattingHelper.ApplyOrdinalRank(row.Source.Rank, index, _recordsDisplaySettings.ShowCount >= 10);
            row.RefreshFromSource();
            index++;
        }

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

    function normalizeSegmentText(value) {
        return String(value || '')
            .replace(/\u00A0/g, ' ')
            .replace(/[\t\r\n]+/g, ' ');
    }

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
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const rawSegments = [];

        let current;
        while ((current = walker.nextNode())) {
            const parent = current.parentElement || root;
            const text = normalizeSegmentText(current.textContent);
            if (!text)
                continue;

            rawSegments.push({
                text,
                ...getStyle(parent)
            });
        }

        for (let i = 0; i < rawSegments.length; i++) {
            const currentSegment = rawSegments[i];
            const hasVisibleChars = /\S/.test(currentSegment.text);

            if (!hasVisibleChars) {
                const hasVisibleBefore = rawSegments.slice(0, i).some(segment => /\S/.test(segment.text));
                const hasVisibleAfter = rawSegments.slice(i + 1).some(segment => /\S/.test(segment.text));
                if (!hasVisibleBefore || !hasVisibleAfter)
                    continue;

                if (segments.length > 0 && /^\s+$/.test(segments[segments.length - 1].text))
                    continue;

                segments.push({
                    ...currentSegment,
                    text: ' '
                });
                continue;
            }

            segments.push(currentSegment);
        }

        while (segments.length > 0 && /^\s+$/.test(segments[0].text))
            segments.shift();
        while (segments.length > 0 && /^\s+$/.test(segments[segments.length - 1].text))
            segments.pop();

        return segments;
    }

    function buildCellTextFromSegments(segments) {
        return segments.map(segment => segment.text || '').join('');
    }

    function extractCell(cell) {
        const segments = getTextSegments(cell);
        return {
            text: buildCellTextFromSegments(segments),
            html: cell.innerHTML || '',
            segments
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

    function isMeaningfulTrackName(text) {
        text = normalize(text);
        if (!text) return false;

        const lowered = text.toLowerCase();
        const blocked = new Set([
            'tmnf-x', 'home', 'tracks', 'trackpacks', 'videos', 'leaderboards',
            'account', 'forums', 'beta area', 'users', 'about', 'track information',
            'show stats', 'log in', 'upload'
        ]);

        if (blocked.has(lowered)) return false;
        if (text.length > 80) return false;
        return true;
    }

    function findTrackName() {
        const breadcrumbContainers = Array.from(document.querySelectorAll('[class*="breadcrumb"], .breadcrumb, nav'));
        for (const container of breadcrumbContainers) {
            const parts = Array.from(container.querySelectorAll('*'))
                .map(el => normalize(el.textContent))
                .filter(isMeaningfulTrackName);

            const tracksIndex = parts.map(p => p.toLowerCase()).lastIndexOf('tracks');
            if (tracksIndex >= 0 && tracksIndex + 1 < parts.length) {
                return parts[tracksIndex + 1];
            }
        }

        const headingSelectors = ['h1', 'h2', 'h3', 'h4', '.card-title', '[class*="title"]', '[class*="trackname"]'];
        for (const selector of headingSelectors) {
            const candidates = Array.from(document.querySelectorAll(selector))
                .map(el => normalize(el.textContent))
                .filter(isMeaningfulTrackName);

            if (candidates.length)
                return candidates[0];
        }

        const metaTitle = document.querySelector('meta[property="og:title"]')?.content || '';
        if (isMeaningfulTrackName(metaTitle))
            return normalize(metaTitle);

        return '';
    }

    return {
        success: records.length > 0,
        message: records.length > 0 ? `Found (${source})` : 'Table not found',
        trackName: findTrackName(),
        recordCount: records.length,
        records
    };
})();
""";

    private const string JsOfflineExtractionScript = """
(() => {
    const expectedHeaders = ['#', 'Time', 'By', 'Score', 'LB'];

    const normalize = (value) => (value || '').replace(/\s+/g, ' ').trim();
    const isRank = (value) => /^\d+(st|nd|rd|th)$/i.test(value) || /^\d+$/.test(value);

    function normalizeSegmentText(value) {
        return String(value || '')
            .replace(/\u00A0/g, ' ')
            .replace(/[\t\r\n]+/g, ' ');
    }

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
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const rawSegments = [];

        let current;
        while ((current = walker.nextNode())) {
            const parent = current.parentElement || root;
            const text = normalizeSegmentText(current.textContent);
            if (!text)
                continue;

            rawSegments.push({ text, ...getStyle(parent) });
        }

        for (let i = 0; i < rawSegments.length; i++) {
            const currentSegment = rawSegments[i];
            const hasVisibleChars = /\S/.test(currentSegment.text);

            if (!hasVisibleChars) {
                const hasVisibleBefore = rawSegments.slice(0, i).some(segment => /\S/.test(segment.text));
                const hasVisibleAfter = rawSegments.slice(i + 1).some(segment => /\S/.test(segment.text));
                if (!hasVisibleBefore || !hasVisibleAfter)
                    continue;

                if (segments.length > 0 && /^\s+$/.test(segments[segments.length - 1].text))
                    continue;

                segments.push({ ...currentSegment, text: ' ' });
                continue;
            }

            segments.push(currentSegment);
        }

        while (segments.length > 0 && /^\s+$/.test(segments[0].text))
            segments.shift();
        while (segments.length > 0 && /^\s+$/.test(segments[segments.length - 1].text))
            segments.pop();

        return segments;
    }

    function buildCellTextFromSegments(segments) {
        return segments.map(segment => segment.text || '').join('');
    }

    function extractCell(cell) {
        const segments = getTextSegments(cell);
        return {
            text: buildCellTextFromSegments(segments),
            html: cell.innerHTML || '',
            segments
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
                by: extractCell(cells[2]),
                score: extractCell(cells[3]),
                lb: extractCell(cells[4])
            });
        }

        return dataRows;
    }

    function findTableWithHeaders() {
        const tables = Array.from(document.querySelectorAll('table'));
        for (const table of tables) {
            const headerCells = Array.from(table.querySelectorAll('th, thead td')).map(cell => normalize(cell.textContent));
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
                by: extractCell(cells[2]),
                score: extractCell(cells[3]),
                lb: extractCell(cells[4])
            });
        }

        return rows;
    }

    function isMeaningfulTrackName(text) {
        text = normalize(text);
        if (!text) return false;

        const lowered = text.toLowerCase();
        const blocked = new Set([
            'tmnf-x', 'home', 'tracks', 'trackpacks', 'videos', 'leaderboards',
            'account', 'forums', 'beta area', 'users', 'about', 'track information',
            'show stats', 'log in', 'upload', 'offline records'
        ]);

        if (blocked.has(lowered)) return false;
        if (text.length > 80) return false;
        return true;
    }

    function findTrackName() {
        const breadcrumbContainers = Array.from(document.querySelectorAll('[class*="breadcrumb"], .breadcrumb, nav'));
        for (const container of breadcrumbContainers) {
            const parts = Array.from(container.querySelectorAll('*'))
                .map(el => normalize(el.textContent))
                .filter(isMeaningfulTrackName);

            const tracksIndex = parts.map(p => p.toLowerCase()).lastIndexOf('tracks');
            if (tracksIndex >= 0 && tracksIndex + 1 < parts.length)
                return parts[tracksIndex + 1];
        }

        const headingSelectors = ['h1', 'h2', 'h3', 'h4', '.card-title', '[class*="title"]', '[class*="trackname"]'];
        for (const selector of headingSelectors) {
            const candidates = Array.from(document.querySelectorAll(selector))
                .map(el => normalize(el.textContent))
                .filter(isMeaningfulTrackName);
            if (candidates.length)
                return candidates[0];
        }

        const metaTitle = document.querySelector('meta[property="og:title"]')?.content || '';
        if (isMeaningfulTrackName(metaTitle))
            return normalize(metaTitle);

        return '';
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

    records = records.slice(0, 10);

    return {
        success: records.length > 0,
        message: records.length > 0 ? `Found (${source})` : 'Offline table not found',
        trackName: findTrackName(),
        recordCount: records.length,
        records
    };
})();
""";

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_F2 = 9001;
    private const int HOTKEY_ID_F3 = 9002;
    private const uint VK_F2 = 0x71;
    private const uint VK_F3 = 0x72;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public enum SelectionSource
{
    None,
    FullRecord,
    OfflineRecord,
    Table1,
    Table2
}

public sealed class ExtractionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int OfflineRecordCount { get; set; }
    public List<OnlineRecord> Records { get; set; } = new();
    public List<OfflineRecord> OfflineRecords { get; set; } = new();
}

public sealed class OfflineExtractionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public List<OfflineRecord> Records { get; set; } = new();
}

public sealed class OnlineRecord
{
    public CellData Rank { get; set; } = new();
    public CellData Time { get; set; } = new();
    public CellData Mode { get; set; } = new();
    public CellData By { get; set; } = new();
    public CellData Server { get; set; } = new();
}

public sealed class OfflineRecord
{
    public CellData Rank { get; set; } = new();
    public CellData Time { get; set; } = new();
    public CellData By { get; set; } = new();
    public CellData Score { get; set; } = new();
    public CellData LB { get; set; } = new();
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

public static class OnlineRecordFactory
{
    public static TextSegment CreatePreviewSegment(string text)
    {
        return new TextSegment
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
        };
    }

    public static OnlineRecord CreateBlankRankTimeByRecord()
    {
        return new OnlineRecord
        {
            Rank = CreateStyledCell(string.Empty),
            Time = CreateStyledCell(string.Empty),
            By = CreateStyledCell(string.Empty),
            Mode = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() },
            Server = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() }
        };
    }

    public static CellData CreateStyledCell(string text)
    {
        return new CellData
        {
            Text = text,
            Html = string.Empty,
            Segments = new List<TextSegment>
            {
                CreatePreviewSegment(text)
            }
        };
    }
}

public static class OnlineRecordCloner
{
    public static OnlineRecord CloneFullRecord(OnlineRecord source)
    {
        return new OnlineRecord
        {
            Rank = CloneCell(source.Rank),
            Time = CloneCell(source.Time),
            Mode = CloneCell(source.Mode),
            By = CloneCell(source.By),
            Server = CloneCell(source.Server)
        };
    }

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

public static class OfflineRecordCloner
{
    public static OfflineRecord Clone(OfflineRecord source)
    {
        return new OfflineRecord
        {
            Rank = OnlineRecordCloner.CloneCell(source.Rank),
            Time = OnlineRecordCloner.CloneCell(source.Time),
            By = OnlineRecordCloner.CloneCell(source.By),
            Score = OnlineRecordCloner.CloneCell(source.Score),
            LB = OnlineRecordCloner.CloneCell(source.LB)
        };
    }
}

public static class ExtractionResultCloner
{
    public static ExtractionResult Clone(ExtractionResult source)
    {
        return new ExtractionResult
        {
            Success = source.Success,
            Message = source.Message,
            TrackName = source.TrackName,
            RecordCount = source.RecordCount,
            OfflineRecordCount = source.OfflineRecordCount,
            Records = source.Records.Select(OnlineRecordCloner.CloneFullRecord).ToList(),
            OfflineRecords = source.OfflineRecords.Select(OfflineRecordCloner.Clone).ToList()
        };
    }
}

public static class RankFormattingHelper
{
    public static void ApplyOrdinalRank(CellData cell, int index, bool padSingleDigit)
    {
        string text = FixedWidthRank(index, padSingleDigit);

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

    public static string FixedWidthRank(int number, bool padSingleDigit)
    {
        if (number < 10)
            return padSingleDigit ? $"  {number}." : $"{number}.";

        return $"{number}.";
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

        return string.Concat(cell.Segments.Select(s => s.Text ?? string.Empty));
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

public sealed class OfflineRecordRowView : ObservableObject
{
    public OfflineRecordRowView(OfflineRecord source)
    {
        Source = source;
        RefreshFromSource();
    }

    public OfflineRecord Source { get; }

    private string _rank = string.Empty;
    private string _time = string.Empty;
    private string _by = string.Empty;
    private string _score = string.Empty;
    private string _lb = string.Empty;

    public string Rank { get => _rank; private set => SetProperty(ref _rank, value); }
    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string By { get => _by; private set => SetProperty(ref _by, value); }
    public string Score { get => _score; private set => SetProperty(ref _score, value); }
    public string LB { get => _lb; private set => SetProperty(ref _lb, value); }

    public void RefreshFromSource()
    {
        Rank = CellDataUtilities.BuildCellText(Source.Rank);
        Time = CellDataUtilities.BuildCellText(Source.Time);
        By = CellDataUtilities.BuildCellText(Source.By);
        Score = CellDataUtilities.BuildCellText(Source.Score);
        LB = CellDataUtilities.BuildCellText(Source.LB);
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

public sealed record MergedRankTimeByItem(OnlineRecord Record, int? NormalizedTime, int OriginalIndex, bool IsOffline);

public sealed class PreviewRecordRowView
{
    public List<TextSegment> RankSegments { get; init; } = new();
    public List<TextSegment> TimeSegments { get; init; } = new();
    public List<TextSegment> ModeSegments { get; init; } = new();
    public List<TextSegment> BySegments { get; init; } = new();
    public List<TextSegment> ServerSegments { get; init; } = new();
    public List<TextSegment> ScoreSegments { get; init; } = new();
    public List<TextSegment> LBSegments { get; init; } = new();

    public static PreviewRecordRowView CreatePlaceholder(bool isOffline)
    {
        var placeholder = new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") };
        return new PreviewRecordRowView
        {
            RankSegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList(),
            TimeSegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList(),
            ModeSegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList(),
            BySegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList(),
            ServerSegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList(),
            ScoreSegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList(),
            LBSegments = placeholder.Select(OnlineRecordCloner.CloneSegment).ToList()
        };
    }

    public static PreviewRecordRowView FromRecord(OnlineRecord record, SelectionSource source)
    {
        static List<TextSegment> CloneOrPlaceholder(CellData cell) => cell.Segments.Count > 0
            ? cell.Segments.Select(OnlineRecordCloner.CloneSegment).ToList()
            : new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") };

        return new PreviewRecordRowView
        {
            RankSegments = CloneOrPlaceholder(record.Rank),
            TimeSegments = CloneOrPlaceholder(record.Time),
            ModeSegments = source == SelectionSource.FullRecord ? CloneOrPlaceholder(record.Mode) : new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") },
            BySegments = CloneOrPlaceholder(record.By),
            ServerSegments = source == SelectionSource.FullRecord ? CloneOrPlaceholder(record.Server) : new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") },
            ScoreSegments = new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") },
            LBSegments = new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") }
        };
    }

    public static PreviewRecordRowView FromOfflineRecord(OfflineRecord record)
    {
        static List<TextSegment> CloneOrPlaceholder(CellData cell) => cell.Segments.Count > 0
            ? cell.Segments.Select(OnlineRecordCloner.CloneSegment).ToList()
            : new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") };

        return new PreviewRecordRowView
        {
            RankSegments = CloneOrPlaceholder(record.Rank),
            TimeSegments = CloneOrPlaceholder(record.Time),
            ModeSegments = new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") },
            BySegments = CloneOrPlaceholder(record.By),
            ServerSegments = new List<TextSegment> { OnlineRecordFactory.CreatePreviewSegment("-") },
            ScoreSegments = CloneOrPlaceholder(record.Score),
            LBSegments = CloneOrPlaceholder(record.LB)
        };
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

public sealed class TableFilePayload
{
    public string TrackName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public DateTime ExportedAt { get; set; }
    public List<OnlineRecord> Rows { get; set; } = new();
}

public sealed class CompactTableFilePayload
{
    public string TrackName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public DateTime ExportedAt { get; set; }
    public List<CompactRankTimeByRecord> Rows { get; set; } = new();
}

public sealed class CompactRankTimeByRecord
{
    public CellData Rank { get; set; } = new();
    public CellData Time { get; set; } = new();
    public CellData By { get; set; } = new();

    public static CompactRankTimeByRecord FromRecord(OnlineRecord source)
    {
        return new CompactRankTimeByRecord
        {
            Rank = OnlineRecordCloner.CloneCell(source.Rank),
            Time = OnlineRecordCloner.CloneCell(source.Time),
            By = OnlineRecordCloner.CloneCell(source.By)
        };
    }

    public OnlineRecord ToOnlineRecord()
    {
        return new OnlineRecord
        {
            Rank = OnlineRecordCloner.CloneCell(Rank),
            Time = OnlineRecordCloner.CloneCell(Time),
            By = OnlineRecordCloner.CloneCell(By),
            Mode = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() },
            Server = new CellData { Text = string.Empty, Html = string.Empty, Segments = new List<TextSegment>() }
        };
    }
}

public sealed class AppState
{
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public string WindowState { get; set; } = nameof(System.Windows.WindowState.Normal);
    public string LastUrl { get; set; } = string.Empty;
    public string LastPageTitle { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public string Table1InsertText { get; set; } = string.Empty;
    public string Table2InsertText { get; set; } = string.Empty;
    public ExtractionResult? LastExtraction { get; set; }
    public List<OnlineRecord> Table1Rows { get; set; } = new();
    public List<OnlineRecord> Table2Rows { get; set; } = new();
    public List<BookmarkStateItem> Bookmarks { get; set; } = new();
    public string LastImportExportDirectory { get; set; } = string.Empty;
    public bool ShowOfflineRecordsTab { get; set; }
    public RecordsDisplaySettings RecordsDisplaySettings { get; set; } = RecordsDisplaySettings.CreateDefault();
    public SelectionState? Selection { get; set; }
    public List<GridColumnState> ColumnWidths { get; set; } = new();
    public List<LayoutLengthState> LayoutLengths { get; set; } = new();
}

public sealed class LayoutLengthState
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class BookmarkStateItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class SelectionState
{
    public SelectionSource Source { get; set; } = SelectionSource.None;
    public int RecordsSelectedIndex { get; set; } = -1;
    public int OfflineRecordsSelectedIndex { get; set; } = -1;
    public int Table1SelectedIndex { get; set; } = -1;
    public int Table2SelectedIndex { get; set; } = -1;
    public int SegmentsSelectedIndex { get; set; } = -1;
}

public sealed class GridColumnState
{
    public string GridKey { get; set; } = string.Empty;
    public int ColumnIndex { get; set; }
    public int DisplayIndex { get; set; } = -1;
    public string Header { get; set; } = string.Empty;
    public double Width { get; set; }
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

public sealed class CssFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => MainWindowFontConverters.ToFontWeight(value?.ToString());
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CssFontStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => MainWindowFontConverters.ToFontStyle(value?.ToString());
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CssFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => MainWindowFontConverters.TryParseFontSize(value?.ToString()) ?? 14d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CssFontFamilyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => MainWindowFontConverters.TryCreateFontFamily(value?.ToString()) ?? new FontFamily("Segoe UI");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal static class MainWindowFontConverters
{
    public static FontWeight ToFontWeight(string? cssValue)
    {
        if (int.TryParse(cssValue, out int weight))
        {
            if (weight >= 700) return FontWeights.Bold;
            if (weight >= 600) return FontWeights.SemiBold;
            if (weight <= 300) return FontWeights.Light;
        }

        return cssValue?.Contains("bold", StringComparison.OrdinalIgnoreCase) == true ? FontWeights.Bold : FontWeights.Normal;
    }

    public static FontStyle ToFontStyle(string? cssValue)
    {
        return cssValue?.Contains("italic", StringComparison.OrdinalIgnoreCase) == true ? FontStyles.Italic : FontStyles.Normal;
    }

    public static FontFamily? TryCreateFontFamily(string? cssValue)
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

    public static double? TryParseFontSize(string? cssValue)
    {
        if (string.IsNullOrWhiteSpace(cssValue))
            return null;

        cssValue = cssValue.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(cssValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : null;
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
