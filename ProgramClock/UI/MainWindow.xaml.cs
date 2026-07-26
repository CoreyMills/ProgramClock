using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using ProgramClock.Data;
using ProgramClock.Startup;
using ProgramClock.Update;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace ProgramClock.UI;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _vm;

    // Secondary key applied under every sort so equal values fall back to a stable A→Z order.
    private const string TieBreakerPath = "DisplayName";

    public MainWindow()
    {
        InitializeComponent();
        _vm = new DashboardViewModel(App.Current.Usage, App.Current.Tracker, App.Current.Settings,
            App.Current.Categories);
        DataContext = _vm;
        EnableLiveSorting();
        RestoreWindowBounds();
        LoadHotkeys();
        Closing += OnWindowClosing;
        Closed += (_, _) => _vm.Dispose();
        ApplySort("FocusMs", ListSortDirection.Descending, FocusedColumn);
    }

    // ── Configurable Delete / Block shortcuts ─────────────────────────────────────────────────────
    // The keys bound to delete/block the current selection, read from settings (null = unbound). Cached
    // here so the grid's key handler is a couple of comparisons rather than a DB read per keystroke.
    private Key? _deleteHotkey;
    private Key? _blockHotkey;

    private void LoadHotkeys()
    {
        _deleteHotkey = ParseKey(App.Current.Settings.GetDeleteHotkey());
        _blockHotkey = ParseKey(App.Current.Settings.GetBlockHotkey());
    }

    private static Key? ParseKey(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Enum.TryParse<Key>(name, out var k) && k != Key.None
            ? k : null;

    // Handled at the window level (not the grid) so it fires no matter where keyboard focus sits:
    // selecting rows through the custom mouse handlers never moves focus into the grid, so a
    // grid-scoped PreviewKeyDown would never see the keystroke. Guarded to the dashboard view and
    // skipped while a text box has focus so it can't fire while the user types in Settings.
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.Page != DashboardPage.Dashboard) return;
        if (Keyboard.FocusedElement is TextBox) return;

        // Alt routes the real key through SystemKey; normalise so e.g. Alt-bound keys still match.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_deleteHotkey is { } dk && key == dk)
        {
            DeleteCurrentSelection();
            e.Handled = true;
        }
        else if (_blockHotkey is { } bk && key == bk)
        {
            BlockCurrentSelection();
            e.Handled = true;
        }
    }

    /// <summary>Set by the app on quit so the next close really tears the window down (disposing the
    /// view model). Until then, a user close just hides the window — see <see cref="OnWindowClosing"/>.</summary>
    public bool AllowClose { get; set; }

    // The dashboard is a tray-app window: a user pressing the close button shouldn't destroy it, or the
    // view model (and the websites attached to each browser row) would be rebuilt on the next open — a
    // rebuild that doesn't re-render the row-details breakdown. So cancel the close and hide instead,
    // keeping the live view intact; the app sets AllowClose on quit to close it for real.
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowBounds();
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    // Reload updates rows in place (see DashboardViewModel.Reload), so make the grid's view re-sort
    // live as those values change — otherwise the order would only reflect the sort when a row is
    // added/removed. The listed properties cover every sortable column's member path.
    private void EnableLiveSorting()
    {
        var view = (ICollectionViewLiveShaping)CollectionViewSource.GetDefaultView(_vm.Rows);
        view.IsLiveSorting = true;
        foreach (var p in new[] { "FocusMs", "RunMs", "DisplayName", "ExeName", "CategoryName", "TagName" })
            view.LiveSortingProperties.Add(p);
    }

    // ── Drag selection ──────────────────────────────────────────────────────────────────────────
    // WPF's DataGrid has two gaps here: it won't rubber-band a drag that starts on empty space, and it
    // won't extend a drag that starts on an already-selected row (native drag-extend only fires from an
    // unselected row). Mixing custom and native selection also desyncs the shift/drag anchor. So we own
    // mouse selection for the grid body outright — one path for click / Ctrl-click / Shift-click / drag,
    // tracking our own anchor — and freeze the auto-refresh for the gesture so a tick can't move rows
    // out from under it (see DashboardViewModel.SuspendRefresh). Header and scrollbar are left to WPF.
    private bool _dragActive;
    private int _anchorIndex = -1;

    private enum HitKind { Chrome, Row, Empty }

    // Walk up from a hit element to decide what part of the grid it belongs to.
    private static HitKind Classify(object source, out UsageRowVm? row)
    {
        row = null;
        var d = source as DependencyObject;
        while (d is not null)
        {
            // The expand/collapse toggle lives in a grid cell; leave its clicks to WPF so it toggles
            // the site list instead of selecting/dragging the row.
            if (d is FrameworkElement { Name: "DisclosureToggle" })
                return HitKind.Chrome;
            switch (d)
            {
                case System.Windows.Controls.ComboBox:
                    return HitKind.Chrome;     // tag/category dropdown — leave clicks to WPF
                case DataGridColumnHeader:
                case ScrollBar:
                    return HitKind.Chrome;     // header (sort) or scrollbar — leave to WPF
                case DataGridDetailsPresenter:
                    // A click inside a row's expanded site list. Encountered before the outer
                    // DataGridRow when walking up, so handle it first: leave the event to WPF so the
                    // nested website ListBox owns its own (click / Ctrl / Shift) selection.
                    return HitKind.Chrome;
                case DataGridRow dr:
                    row = dr.Item as UsageRowVm;
                    return HitKind.Row;
                case DataGrid:
                    return HitKind.Empty;      // inside the grid but below/beside the rows
            }
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return HitKind.Empty;
    }

    // Walk up from a hit element to the disclosure toggle (if any) and return the browser row it
    // belongs to. The toggle's DataContext is the row's UsageRowVm.
    private static UsageRowVm? DisclosureRow(object source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is FrameworkElement { Name: "DisclosureToggle" } fe)
                return fe.DataContext as UsageRowVm;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Expand/collapse the website breakdown ourselves. The ToggleButton's own click is unreliable
        // inside the grid — the preview mouse handlers here plus the DataGridCell's class handlers
        // swallow it, so its IsChecked->IsExpanded binding never fires. Toggling IsExpanded directly
        // (the toggle's IsChecked follows it via the two-way binding) makes the arrow work every time.
        if (DisclosureRow(e.OriginalSource) is { } toggleRow)
        {
            toggleRow.IsExpanded = !toggleRow.IsExpanded;
            e.Handled = true;
            return;
        }

        var kind = Classify(e.OriginalSource, out var row);
        if (kind == HitKind.Chrome) return;   // header click / scrollbar drag: don't interfere

        // Freeze the rebuild for the whole gesture so a refresh tick can't move rows mid-drag.
        _vm.SuspendRefresh = true;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (kind == HitKind.Empty)
        {
            // Rubber-band from empty space; the anchor is set by the first row the pointer reaches.
            if (!ctrl) DashboardGrid.UnselectAll();
            _anchorIndex = -1;
            _dragActive = true;
            DashboardGrid.CaptureMouse();
            e.Handled = true;
            return;
        }

        int idx = DashboardGrid.Items.IndexOf(row);
        if (idx < 0) { e.Handled = true; return; }

        if (shift && _anchorIndex >= 0)
        {
            SelectRange(_anchorIndex, idx);    // extend from the anchor, keep the anchor put
        }
        else if (ctrl)
        {
            if (DashboardGrid.SelectedItems.Contains(row)) DashboardGrid.SelectedItems.Remove(row);
            else DashboardGrid.SelectedItems.Add(row);
            _anchorIndex = idx;
        }
        else
        {
            // Plain press: select just this row and arm a drag — moving onto other rows turns it into
            // a range, which is what makes "click an entry, then drag from it" work.
            DashboardGrid.SelectedItems.Clear();
            DashboardGrid.SelectedItems.Add(row);
            _anchorIndex = idx;
            _dragActive = true;
            DashboardGrid.CaptureMouse();
        }
        e.Handled = true;
    }

    private void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragActive || e.LeftButton != MouseButtonState.Pressed) return;

        var hit = DashboardGrid.InputHitTest(e.GetPosition(DashboardGrid)) as DependencyObject;
        if (hit is null || Classify(hit, out var row) != HitKind.Row || row is null) return;

        int idx = DashboardGrid.Items.IndexOf(row);
        if (idx < 0) return;
        if (_anchorIndex < 0) _anchorIndex = idx;   // empty-space drag anchors on the first row reached
        SelectRange(_anchorIndex, idx);
        e.Handled = true;
    }

    private void OnGridMouseUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void OnGridLostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

    // Replace the selection with the contiguous range of rows between two view indices (inclusive).
    private void SelectRange(int a, int b)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        DashboardGrid.SelectedItems.Clear();
        for (int i = lo; i <= hi && i < DashboardGrid.Items.Count; i++)
            DashboardGrid.SelectedItems.Add(DashboardGrid.Items[i]);
    }

    // Close out any drag gesture: release capture and resume refreshing, catching up on the ticks we
    // skipped while the button was down so the grid lands on fresh totals.
    private void EndDrag()
    {
        _dragActive = false;
        if (DashboardGrid.IsMouseCaptured) DashboardGrid.ReleaseMouseCapture();
        if (_vm.SuspendRefresh)
        {
            _vm.SuspendRefresh = false;
            _vm.Reload();
        }
    }

    /// <summary>Restore the saved size/position, falling back to half the primary screen, centered,
    /// the first time (or whenever the saved rect would land off every monitor).</summary>
    private void RestoreWindowBounds()
    {
        var saved = App.Current.Settings.GetWindowBounds();
        if (saved is { } b && IsOnScreen(b))
        {
            Left = b.Left;
            Top = b.Top;
            Width = b.Width;
            Height = b.Height;
            WindowState = b.Maximized ? WindowState.Maximized : WindowState.Normal;
            return;
        }

        Width = SystemParameters.PrimaryScreenWidth / 2;
        Height = SystemParameters.PrimaryScreenHeight / 2;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
    }

    private void SaveWindowBounds()
    {
        var maximized = WindowState == WindowState.Maximized;
        // RestoreBounds holds the normal-state rect when maximized/minimized; Left/Top/Width/Height
        // are only meaningful while the window is in the Normal state.
        var r = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return;

        App.Current.Settings.SetWindowBounds(
            new WindowBounds(r.Left, r.Top, r.Width, r.Height, maximized));
    }

    // Keep the window reachable: require it to overlap the virtual desktop by a small margin so a
    // saved position from a now-disconnected monitor doesn't open it out of view.
    private static bool IsOnScreen(WindowBounds b)
    {
        var virt = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var rect = new Rect(b.Left, b.Top, b.Width, b.Height);
        rect.Intersect(virt);
        return rect is { Width: >= 80, Height: >= 40 };
    }

    // Owns the grid's sort order via the rows' collection view: a primary key plus a DisplayName
    // tie-breaker, with the matching column header showing the arrow. Sorting the view (not the
    // ObservableCollection) keeps the order stable across the rebuilds in DashboardViewModel.Reload.
    private void ApplySort(string path, ListSortDirection direction, DataGridColumn? column)
    {
        var view = CollectionViewSource.GetDefaultView(_vm.Rows);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(path, direction));
            if (path != TieBreakerPath)
                view.SortDescriptions.Add(new SortDescription(TieBreakerPath, ListSortDirection.Ascending));
        }

        foreach (var c in DashboardGrid.Columns)
            c.SortDirection = null;
        if (column is not null) column.SortDirection = direction;
    }

    // When the grid selection changes, let the view model decide whether the header shows the range
    // totals or the combined times of the selected rows.
    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _vm.UpdateSelectionTotals(DashboardGrid.SelectedItems.OfType<UsageRowVm>());

    private void OnGridSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var path = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(path)) return;

        var direction = e.Column.SortDirection != ListSortDirection.Ascending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;
        ApplySort(path, direction, e.Column);
    }

    /// <summary>Switch the window to the settings page (called from the tray and the header button).</summary>
    public void NavigateToSettings()
    {
        LoadSettingsFields();
        _vm.LoadBlocked();
        _vm.LoadEfficiencyLabelsForEditing();
        _vm.Page = DashboardPage.Settings;
    }

    // Shown in a hotkey box when no key is bound; mapped back to an empty stored value on save.
    private const string NoHotkey = "(None)";

    private void LoadSettingsFields()
    {
        var s = App.Current.Settings;
        IdleSecondsBox.Text = s.GetIdleThresholdSeconds().ToString();
        FocusRefreshBox.Text = s.GetFocusRefreshSeconds().ToString();
        RunRefreshBox.Text = s.GetRunRefreshSeconds().ToString();
        DeleteHotkeyBox.Text = HotkeyDisplay(s.GetDeleteHotkey());
        BlockHotkeyBox.Text = HotkeyDisplay(s.GetBlockHotkey());
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        AutoUpdateCheck.IsChecked = s.GetAutoUpdateEnabled();
        DayStartBox.Text = s.GetDayStart();
        DayEndBox.Text = s.GetDayEnd();
        LockStopCheck.IsChecked = s.GetLockStopEnabled();
        LockStopMinutesBox.Text = s.GetLockStopMinutes().ToString();
        TooltipDelayBox.Text = (s.GetTooltipDelayMs() / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
        GrowOnHoverCheck.IsChecked = s.GetGrowOnHover();
        StatusText.Text = "";
    }

    // ── Accent colour ─────────────────────────────────────────────────────────────────────────────
    // The swatch shows the current accent and opens a colour-picker popup. Selections apply and persist
    // immediately (so the change is visible at once), independent of the Save button.

    private void OnOpenAccentPicker(object sender, RoutedEventArgs e)
    {
        AccentPicker.SelectedColor = ((System.Windows.Media.SolidColorBrush)FindResource("AccentBrush")).Color;
        AccentPopup.IsOpen = true;
    }

    private void OnAccentPickerChanged(object? sender, EventArgs e) =>
        ApplyAccentSelection(ToHex(AccentPicker.SelectedColor));

    private void OnResetAccent(object sender, RoutedEventArgs e) =>
        ApplyAccentSelection(SettingsRepository.DefaultAccentColor);

    private void ApplyAccentSelection(string hex)
    {
        App.Current.Settings.SetAccentColor(hex);
        App.Current.ApplyAccent(hex);
        StatusText.Text = "";
    }

    // ── Main (background) colour ──────────────────────────────────────────────────────────────────
    // Like the accent picker, selections apply and persist immediately. "Use system theme" clears the
    // override (empty value) so the app follows the Windows light/dark theme.

    private void OnOpenMainPicker(object sender, RoutedEventArgs e)
    {
        MainPicker.SelectedColor = ((System.Windows.Media.SolidColorBrush)FindResource("WindowBackgroundBrush")).Color;
        MainPopup.IsOpen = true;
    }

    private void OnMainPickerChanged(object? sender, EventArgs e) =>
        ApplyMainSelection(ToHex(MainPicker.SelectedColor));

    private void OnResetMainColor(object sender, RoutedEventArgs e)
    {
        MainPopup.IsOpen = false;
        ApplyMainSelection("");
    }

    private void ApplyMainSelection(string hex)
    {
        App.Current.Settings.SetMainColor(hex);
        App.Current.ApplyMainColor(hex);
        StatusText.Text = "";
    }

    private static string ToHex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string HotkeyDisplay(string keyName) =>
        string.IsNullOrWhiteSpace(keyName) ? NoHotkey : keyName;

    private static string HotkeyValue(string text) => text == NoHotkey ? "" : text;

    // Capture the next key press into the focused hotkey box. Bare modifier keys and Escape are
    // ignored so the user can tab/click away without binding one of them.
    private void OnHotkeyCapture(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.Escape)
            return;
        box.Text = key.ToString();
    }

    private void OnClearDeleteHotkey(object sender, RoutedEventArgs e) => DeleteHotkeyBox.Text = NoHotkey;
    private void OnClearBlockHotkey(object sender, RoutedEventArgs e) => BlockHotkeyBox.Text = NoHotkey;

    private void OnOpenSettings(object sender, RoutedEventArgs e) => NavigateToSettings();

    private void OnOpenCategories(object sender, RoutedEventArgs e)
    {
        _vm.LoadCategoriesPage();
        _vm.Page = DashboardPage.Categories;
    }

    private void OnBackToDashboard(object sender, RoutedEventArgs e) =>
        _vm.Page = DashboardPage.Dashboard;

    private void OnBackFromCategories(object sender, RoutedEventArgs e) =>
        _vm.Page = DashboardPage.Dashboard;

    private void OnAddCategory(object sender, RoutedEventArgs e)
    {
        _vm.CreateCategory(NewCategoryBox.Text);
        NewCategoryBox.Clear();
    }

    private void OnDeleteCategory(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CategoryItemVm cat })
            _vm.DeleteCategory(cat.Id);
    }

    // Right-click Delete on the "Assign apps to categories" grid: forget the app's pending deltas and
    // drop its rows, then rebuild the categories page so the entry disappears immediately.
    private void OnDeleteManagedApp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppCategoryItemVm app })
        {
            _vm.DeleteApp(app.ExeName);
            _vm.LoadCategoriesPage();
        }
    }

    private void OnManualRefresh(object sender, RoutedEventArgs e) => _vm.ForceRefresh();

    // Flip the charts between focused and running time. (A subtle-styled button rather than a raw
    // ToggleButton so it matches the other header buttons.)
    private void OnToggleMetric(object sender, RoutedEventArgs e) => _vm.ShowFocused = !_vm.ShowFocused;

    // Capture-on-open: ContextMenuOpening fires reliably every open with the element under the cursor,
    // unlike reading the menu's PlacementTarget/Parent at click time. Explorer-style: right-clicking a
    // row already part of the selection keeps the whole (mixed app + site) selection; right-clicking an
    // unselected row makes it the sole target, clearing any site selection too. Selecting a browser row
    // also selects all of its websites (UsageRowVm.IsRowSelected), so deleting/blocking it takes them.
    private void OnRowContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // A right-click inside a row's expanded site list bubbles up to here too; leave it to the
        // site handler (the website ListBox has its own context menu).
        if (SiteFrom(e.OriginalSource) is not null) return;

        var row = RowFrom(e.OriginalSource);
        if (row is null) { e.Handled = true; return; }   // suppress the menu on empty space

        if (!DashboardGrid.SelectedItems.Contains(row))
        {
            DashboardGrid.UnselectAll();
            _vm.ClearSiteSelection();
            DashboardGrid.SelectedItem = row;   // selects the row (and its sites via IsRowSelected)
        }
    }

    // Delete/Block apply to the whole current selection: selected app rows plus any selected websites
    // (across every browser row). The row menu, the site menu, and the keyboard shortcuts all route
    // here, so a mixed manual selection is treated as one batch. Sites whose browser is also selected
    // are skipped in the VM.
    private void OnDeleteSelection(object sender, RoutedEventArgs e) => DeleteCurrentSelection();

    private void OnBlockSelection(object sender, RoutedEventArgs e) => BlockCurrentSelection();

    // Reset the selected apps' tracked time for the current range (kept app rows, tags, categories).
    private void OnResetSelection(object sender, RoutedEventArgs e)
    {
        var apps = DashboardGrid.SelectedItems.OfType<UsageRowVm>().Select(r => r.ExeName).ToList();
        if (apps.Count == 0) return;
        var who = apps.Count == 1 ? $"\"{apps[0]}\"" : $"{apps.Count} apps";
        var ok = MessageBox.Show(
            $"Reset tracked data for {who} in the {_vm.SelectedRange} range?\nThis can't be undone.",
            "Reset data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok == MessageBoxResult.Yes) _vm.ResetSelection(apps);
    }

    // Reset every app's tracked time for the current range.
    private void OnClearRange(object sender, RoutedEventArgs e)
    {
        var ok = MessageBox.Show(
            $"Clear all tracked time for the {_vm.SelectedRange} range?\nThis clears every app and can't be undone.",
            "Clear range data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok == MessageBoxResult.Yes) _vm.ResetAllForRange();
    }

    private void DeleteCurrentSelection()
    {
        var apps = DashboardGrid.SelectedItems.OfType<UsageRowVm>().Select(r => r.ExeName).ToList();
        var sites = _vm.SelectedSites().ToList();
        if (apps.Count == 0 && sites.Count == 0) return;
        _vm.DeleteSelection(apps, sites);
    }

    private void BlockCurrentSelection()
    {
        var apps = DashboardGrid.SelectedItems.OfType<UsageRowVm>().Select(r => r.ExeName).ToList();
        var sites = _vm.SelectedSites().ToList();
        if (apps.Count == 0 && sites.Count == 0) return;
        _vm.BlockSelection(apps, sites);
    }

    private static UsageRowVm? RowFrom(object source)
    {
        var d = source as DependencyObject;
        while (d is not null and not DataGridRow)
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return (d as DataGridRow)?.Item as UsageRowVm;
    }

    private void OnUnblockApp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string exe })
            _vm.UnblockApp(exe);
    }

    // Right-clicking a website uses the same Delete/Block handlers as rows (the SiteContextMenu points
    // at OnDeleteSelection/OnBlockSelection), so apps and sites are treated as one selection. This only
    // resets the target Explorer-style: a right-clicked site already in the selection keeps the whole
    // (mixed) selection; an unselected one becomes the sole target, clearing rows and other sites first.
    private void OnSiteContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var site = SiteFrom(e.OriginalSource);
        if (site is null) { e.Handled = true; return; }   // suppress the menu on empty space

        if (!site.IsSelected)
        {
            DashboardGrid.UnselectAll();   // clears row selection (and their sites via IsRowSelected)
            _vm.ClearSiteSelection();      // clears any other manually-selected sites
            site.IsSelected = true;
        }
    }

    private static SiteRowVm? SiteFrom(object source)
    {
        var d = source as DependencyObject;
        while (d is not null and not ListBoxItem)
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as SiteRowVm;
    }

    private void OnUnblockSite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string host })
            _vm.UnblockSite(host);
    }

    // ── Website drag selection ────────────────────────────────────────────────────────────────────
    // Each browser row's site list is its own ListBox, so this mirrors the main grid's drag logic but
    // operates on whichever ListBox raised the event (one click / Ctrl / Shift / drag path, with a
    // per-gesture anchor). The grid rebuild is frozen for the gesture (SuspendRefresh) so a refresh
    // tick can't move site rows mid-drag.
    private bool _siteDragActive;
    private int _siteAnchorIndex = -1;
    private ListBox? _siteList;

    private void OnSiteMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        _siteList = list;
        _vm.SuspendRefresh = true;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var site = SiteFrom(e.OriginalSource);

        if (site is null)
        {
            if (!ctrl) list.UnselectAll();
            _siteAnchorIndex = -1;
            _siteDragActive = true;
            list.CaptureMouse();
            e.Handled = true;
            return;
        }

        int idx = list.Items.IndexOf(site);
        if (idx < 0) { e.Handled = true; return; }

        if (shift && _siteAnchorIndex >= 0)
        {
            SelectSiteRange(list, _siteAnchorIndex, idx);
        }
        else if (ctrl)
        {
            if (list.SelectedItems.Contains(site)) list.SelectedItems.Remove(site);
            else list.SelectedItems.Add(site);
            _siteAnchorIndex = idx;
        }
        else
        {
            list.SelectedItems.Clear();
            list.SelectedItems.Add(site);
            _siteAnchorIndex = idx;
            _siteDragActive = true;
            list.CaptureMouse();
        }
        e.Handled = true;
    }

    private void OnSiteMouseMove(object sender, MouseEventArgs e)
    {
        if (!_siteDragActive || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ListBox list) return;

        var hit = list.InputHitTest(e.GetPosition(list)) as DependencyObject;
        if (hit is null) return;
        var site = SiteFrom(hit);
        if (site is null) return;

        int idx = list.Items.IndexOf(site);
        if (idx < 0) return;
        if (_siteAnchorIndex < 0) _siteAnchorIndex = idx;
        SelectSiteRange(list, _siteAnchorIndex, idx);
        e.Handled = true;
    }

    private void OnSiteMouseUp(object sender, MouseButtonEventArgs e) => EndSiteDrag();

    private void OnSiteLostMouseCapture(object sender, MouseEventArgs e) => EndSiteDrag();

    private void SelectSiteRange(ListBox list, int a, int b)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        list.SelectedItems.Clear();
        for (int i = lo; i <= hi && i < list.Items.Count; i++)
            list.SelectedItems.Add(list.Items[i]);
    }

    private void EndSiteDrag()
    {
        _siteDragActive = false;
        if (_siteList?.IsMouseCaptured == true) _siteList.ReleaseMouseCapture();
        if (_vm.SuspendRefresh)
        {
            _vm.SuspendRefresh = false;
            _vm.Reload();
        }
    }

    // Add/remove rows in the efficiency-ratings editor. These only change the editable list; nothing
    // persists until the user clicks Save (OnSaveSettings → TrySaveEfficiencyLabels).
    private void OnAddRating(object sender, RoutedEventArgs e) => _vm.AddRating();

    private void OnDeleteRating(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RatingLabelVm row } && !_vm.RemoveRating(row, out var error))
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = error!;
        }
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdleSecondsBox.Text, out var idle) || idle < 1 ||
            !int.TryParse(FocusRefreshBox.Text, out var focus) || focus < 1 ||
            !int.TryParse(RunRefreshBox.Text, out var run) || run < 1)
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = "Enter whole numbers of seconds (1 or more) in every field.";
            return;
        }

        if (!TimeOnly.TryParse(DayStartBox.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dayStartT) ||
            !TimeOnly.TryParse(DayEndBox.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dayEndT))
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = "Enter day start and end as 24-hour times like 08:00.";
            return;
        }

        if (!int.TryParse(LockStopMinutesBox.Text, out var lockMinutes) || lockMinutes < 0)
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = "Enter the lock timeout as a whole number of minutes (0 or more).";
            return;
        }

        if (!double.TryParse(TooltipDelayBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tooltipSeconds)
            || tooltipSeconds < 0)
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = "Enter the chart tooltip delay as a number of seconds (0 or more).";
            return;
        }
        int tooltipDelay = (int)Math.Round(tooltipSeconds * 1000);

        if (!_vm.TrySaveEfficiencyLabels(out var labelError))
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = labelError;
            return;
        }

        var deleteHotkey = HotkeyValue(DeleteHotkeyBox.Text);
        var blockHotkey = HotkeyValue(BlockHotkeyBox.Text);
        if (deleteHotkey.Length > 0 && deleteHotkey == blockHotkey)
        {
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            StatusText.Text = "Delete and Block can't be bound to the same key.";
            return;
        }

        var s = App.Current.Settings;
        var tracker = App.Current.Tracker;

        s.SetDeleteHotkey(deleteHotkey);
        s.SetBlockHotkey(blockHotkey);
        LoadHotkeys();

        s.SetIdleThresholdSeconds(idle);
        tracker.SetIdleThresholdSeconds(idle);

        s.SetFocusRefreshSeconds(focus);
        tracker.SetFocusIntervalSeconds(focus);

        s.SetRunRefreshSeconds(run);
        tracker.SetRunIntervalSeconds(run);

        AutoStart.SetEnabled(AutoStartCheck.IsChecked == true);
        App.Current.NotifyAutoStartChanged();

        s.SetAutoUpdateEnabled(AutoUpdateCheck.IsChecked == true);

        // Normalize the times to "HH:mm" so the stored/displayed values stay consistent.
        var dayStart = dayStartT.ToString("HH:mm", CultureInfo.InvariantCulture);
        var dayEnd = dayEndT.ToString("HH:mm", CultureInfo.InvariantCulture);
        s.SetDayStart(dayStart);
        s.SetDayEnd(dayEnd);
        tracker.SetDayHours(dayStart, dayEnd);

        var lockStopEnabled = LockStopCheck.IsChecked == true;
        s.SetLockStopEnabled(lockStopEnabled);
        tracker.SetLockStopEnabled(lockStopEnabled);

        s.SetLockStopMinutes(lockMinutes);
        tracker.SetLockStopMinutes(lockMinutes);

        s.SetTooltipDelayMs(tooltipDelay);
        _vm.SetTooltipDelayMs(tooltipDelay);

        var growOnHover = GrowOnHoverCheck.IsChecked == true;
        s.SetGrowOnHover(growOnHover);
        _vm.SetGrowOnHover(growOnHover);

        _vm.ApplyRefreshSettings();
        _vm.Page = DashboardPage.Dashboard;
    }

    // ── Update buttons ────────────────────────────────────────────────────────────────────────────

    /// <summary>Manually checks GitHub for a newer release without downloading anything.</summary>
    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        _vm.UpdateControlsEnabled = false;
        _vm.UpdateStatusText = "Checking for updates…";
        try
        {
            var info = await UpdateService.CheckAsync();
            App.Current.Settings.SetLastUpdateCheckDate(UsageRepository.LocalDate(DateTime.Now));

            if (info is null)
                _vm.UpdateStatusText = "Couldn't check for updates. Please try again later.";
            else if (UpdateService.IsUpdateAvailable(info))
                _vm.UpdateStatusText = $"Update available: {info.Tag} (you have v{UpdateService.CurrentVersion.ToString(3)}). " +
                                       "Click \"Update Now\" to install.";
            else
                _vm.UpdateStatusText = $"You're up to date (v{UpdateService.CurrentVersion.ToString(3)}).";
        }
        finally
        {
            _vm.UpdateControlsEnabled = true;
        }
    }

    /// <summary>Checks, downloads, and installs the latest release, then restarts the app.</summary>
    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        _vm.UpdateControlsEnabled = false;
        _vm.UpdateStatusText = "Checking for updates…";
        try
        {
            var info = await UpdateService.CheckAsync();
            App.Current.Settings.SetLastUpdateCheckDate(UsageRepository.LocalDate(DateTime.Now));

            if (info is null)
            {
                _vm.UpdateStatusText = "Couldn't check for updates. Please try again later.";
                return;
            }
            if (!UpdateService.IsUpdateAvailable(info))
            {
                _vm.UpdateStatusText = $"You're already up to date (v{UpdateService.CurrentVersion.ToString(3)}).";
                return;
            }

            _vm.UpdateStatusText = $"Downloading {info.Tag}…";
            _vm.DownloadProgress = 0;
            _vm.IsDownloading = true;
            var progress = new Progress<double>(p => _vm.DownloadProgress = p);

            string newExe = await UpdateService.DownloadAsync(info, progress);

            _vm.UpdateStatusText = "Installing update and restarting…";
            App.Current.ApplyUpdateAndRestart(newExe);
        }
        catch (Exception ex)
        {
            _vm.IsDownloading = false;
            _vm.UpdateStatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            _vm.UpdateControlsEnabled = true;
        }
    }
}
