using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageConverter.App.Services;
using ImageConverter.App.ViewModels;
using ImageConverter.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace ImageConverter.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly AppSettings _settings;
    private readonly ThumbnailCache _thumbs = new();
    private readonly IntPtr _hwnd;

    public MainWindow(string[] args)
    {
        _settings = AppSettings.Load();
        ViewModel = new MainViewModel(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), _settings);
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        SetUpChrome();
        ItemsList.ContextRequested += Items_ContextRequested;

        if (args.Length > 0)
            _ = LoadAsync(args, append: false);
    }

    // ================================================================== window chrome

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private double Scale => GetDpiForWindow(_hwnd) / 96.0;

    private void SetUpChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        var size = new SizeInt32((int)(_settings.WindowWidth * Scale), (int)(_settings.WindowHeight * Scale));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        size.Width = Math.Min(size.Width, work.Width);
        size.Height = Math.Min(size.Height, work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2, size.Width, size.Height));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(900 * Scale);
            p.PreferredMinimumHeight = (int)(560 * Scale);
            if (_settings.WindowMaximized) p.Maximize();
        }

        AppWindow.Closing += (_, _) =>
        {
            ViewModel.Cancel();
            SaveSettings();
        };
    }

    private void SaveSettings()
    {
        ViewModel.SaveTo(_settings);
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            _settings.WindowMaximized = true;
        }
        else
        {
            _settings.WindowMaximized = false;
            _settings.WindowWidth = (int)(AppWindow.Size.Width / Scale);
            _settings.WindowHeight = (int)(AppWindow.Size.Height / Scale);
        }
        _settings.Save();
    }

    // ================================================================== notices

    public void ShowError(string title, string message) => ShowNotice(InfoBarSeverity.Error, title, message);

    private void ShowNotice(InfoBarSeverity severity, string title, string message)
    {
        Notice.Severity = severity;
        Notice.Title = title;
        Notice.Message = message;
        Notice.IsOpen = true;
    }

    // ================================================================== loading

    private async Task LoadAsync(IEnumerable<string> paths, bool append)
    {
        try
        {
            if (!append) _thumbs.Clear();
            Notice.IsOpen = false;
            await ViewModel.LoadAsync(paths, append);
            if (ViewModel.TotalCount == 0)
                ShowNotice(InfoBarSeverity.Warning, "No images found",
                    ViewModel.IncludeSubfolders ? "There are no image files here." : "There are no image files here. Turn on \"Include subfolders\" to look deeper.");
        }
        catch (Exception e)
        {
            ShowError("Couldn't open", e.Message);
        }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) await LoadAsync([folder.Path], append: false);
    }

    private async void AddImages_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var ext in ImageScanner.DefaultExtensions) picker.FileTypeFilter.Add(ext);
        InitializeWithWindow.Initialize(picker, _hwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0) await LoadAsync(files.Select(f => f.Path), append: ViewModel.HasItems);
    }

    private async void Subfolders_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasItems) return;
        _thumbs.Clear();
        await ViewModel.ReloadAsync();
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsIdle || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = ViewModel.HasItems ? "Add to list" : "Open";
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsIdle || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        await LoadAsync(items.Select(i => i.Path), append: ViewModel.HasItems);
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (!ViewModel.IsIdle || ViewModel.SelectedItems.Count == 0) return;
        ViewModel.Remove(ViewModel.SelectedItems);
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        _thumbs.Clear();
        ViewModel.Clear();
    }

    // ================================================================== thumbnails

    // Each thumbnail Image's Tag holds the item it should currently show. It is set synchronously when
    // the container is (re)used, so it is the only reliable "still wanted" check: during recycling the
    // container's Content still reports the previous item, and a slow load for that item would otherwise
    // land in the wrong row.
    private void Items_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if ((args.ItemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("Thumb") is not Image img) return;
        if (args.InRecycleQueue || args.Item is not ConvertItem item)
        {
            img.Tag = null;
            img.Source = null;
            return;
        }

        img.Tag = item;
        if (_thumbs.TryGet(item, out var cached))
        {
            img.Source = cached;
            return;
        }
        img.Source = null;
        LoadThumbnail(img, item);
    }

    private async void LoadThumbnail(Image img, ConvertItem item)
    {
        bool Wanted() => ReferenceEquals(img.Tag, item);

        for (int attempt = 0; attempt < 3 && Wanted(); attempt++)
        {
            if (attempt > 0) await Task.Delay(400 * attempt); // disk busy or file still being written
            var bmp = await _thumbs.GetAsync(item, 96, Wanted);
            if (bmp == null) continue;
            if (Wanted()) img.Source = bmp;
            return;
        }
    }

    // ================================================================== list interaction

    private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        ViewModel.SetSelection(ItemsList.SelectedItems.OfType<ConvertItem>());
    }

    private bool _syncingSelection;

    /// <summary>Selects exactly these items (or adds them) using index ranges, which is fast for large lists.</summary>
    private void ApplySelection(IEnumerable<ConvertItem> items, bool add = false)
    {
        var index = new Dictionary<ConvertItem, int>(ViewModel.Items.Count);
        for (int i = 0; i < ViewModel.Items.Count; i++) index[ViewModel.Items[i]] = i;
        var positions = items.Select(i => index.TryGetValue(i, out var p) ? p : -1).Where(p => p >= 0).Distinct().Order().ToList();

        _syncingSelection = true;
        try
        {
            if (!add && ViewModel.Items.Count > 0)
                ItemsList.DeselectRange(new ItemIndexRange(0, (uint)ViewModel.Items.Count));
            for (int i = 0; i < positions.Count;)
            {
                int j = i;
                while (j + 1 < positions.Count && positions[j + 1] == positions[j] + 1) j++;
                ItemsList.SelectRange(new ItemIndexRange(positions[i], (uint)(j - i + 1)));
                i = j + 1;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
        ViewModel.SetSelection(ItemsList.SelectedItems.OfType<ConvertItem>());
        if (positions.Count > 0) ItemsList.ScrollIntoView(ViewModel.Items[positions[0]]);
    }

    private void SelectMatching_Click(object sender, RoutedEventArgs e) => SelectByRange(add: false);
    private void AddMatching_Click(object sender, RoutedEventArgs e) => SelectByRange(add: true);

    private void RangeBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => SelectByRange(add: false);

    private void SelectByRange(bool add)
    {
        var text = RangeBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            RangeBox.Focus(FocusState.Programmatic);
            return;
        }
        var matches = ViewModel.Match(text);
        if (matches.Count == 0)
        {
            ShowNotice(InfoBarSeverity.Warning, "Nothing matches", $"No image matches \"{text}\".");
            return;
        }
        Notice.IsOpen = false;
        ApplySelection(matches, add);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ApplySelection(ViewModel.Items);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => ApplySelection([]);
    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedItems.ToHashSet();
        ApplySelection(ViewModel.Items.Where(i => !selected.Contains(i)));
    }
    private void SelectFailed_Click(object sender, RoutedEventArgs e) =>
        ApplySelection(ViewModel.Items.Where(i => i.Status == ItemStatus.Failed));
    private void SelectNotConverted_Click(object sender, RoutedEventArgs e) =>
        ApplySelection(ViewModel.Items.Where(i => i.Status != ItemStatus.Done));

    private void Items_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete) return;
        RemoveSelected();
        e.Handled = true;
    }

    /// <summary>Right-clicking an unselected row selects it first, like Explorer.</summary>
    private void Items_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: ConvertItem item } && !ItemsList.SelectedItems.Contains(item))
            ItemsList.SelectedItem = item;
    }

    private void ItemMenu_Opening(object? sender, object e)
    {
        OpenConvertedItem.IsEnabled = ViewModel.SelectedItems.Any(i => i.Status == ItemStatus.Done);
        ConvertSelectedItem.IsEnabled = ViewModel.CanConvertSelected;
        ConvertSelectedItem.Text = ViewModel.ConvertSelectedText;
    }

    private void Item_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConvertItem item) return;
        Open(item.Status == ItemStatus.Done && item.TargetPath != null ? item.TargetPath : item.FullPath);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewModel.SelectedItems.Take(10)) Open(item.FullPath);
    }

    private void OpenConverted_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewModel.SelectedItems.Where(i => i.Status == ItemStatus.Done && i.TargetPath != null).Take(10))
            Open(item.TargetPath!);
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItems.FirstOrDefault() is not { } item) return;
        var path = item.Status == ItemStatus.Done && item.TargetPath != null ? item.TargetPath : item.FullPath;
        Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LastOutputFolder is { } folder && Directory.Exists(folder)) Start("explorer.exe", $"\"{folder}\"");
    }

    private void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open the file", ex.Message);
        }
    }

    private static void Start(string exe, string arguments)
    {
        try { Process.Start(exe, arguments); } catch (Exception) { /* nothing useful to report */ }
    }

    // ================================================================== options

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) ViewModel.Options.OutputFolder = folder.Path;
    }

    private bool _settingPicker;

    private void BackgroundFlyout_Opening(object? sender, object e)
    {
        _settingPicker = true;
        BackgroundPicker.Color = Ui.ParseHex(ViewModel.Options.Background);
        _settingPicker = false;
    }

    private void BackgroundPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_settingPicker) ViewModel.Options.Background = Ui.ToHex(args.NewColor);
    }

    // ================================================================== converting

    private async void Convert_Click(object sender, RoutedEventArgs e) => await RunAsync(only: null);

    private async void ConvertSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItems.Count == 0) return;
        await RunAsync(ViewModel.SelectedItems.ToList());
    }

    private async Task RunAsync(IReadOnlyList<ConvertItem>? only)
    {
        if (ViewModel.Options.Location == OutputLocation.Folder && string.IsNullOrWhiteSpace(ViewModel.Options.OutputFolder))
        {
            ShowNotice(InfoBarSeverity.Warning, "Choose a folder", "Pick where the converted images should go, or save them next to the originals.");
            return;
        }

        Notice.IsOpen = false;
        try
        {
            await ViewModel.ConvertAsync(only);
        }
        catch (Exception ex)
        {
            ShowError("Conversion stopped", ex.Message);
            return;
        }

        if (ViewModel.FailedCount > 0)
            ShowNotice(InfoBarSeverity.Warning, $"{ViewModel.FailedCount} image{(ViewModel.FailedCount == 1 ? "" : "s")} couldn't be converted",
                "Hover the red icon in the list to see why.");
        else if (ViewModel.DoneCount > 0)
            ShowNotice(InfoBarSeverity.Success, "Done", $"{ViewModel.DoneCount} image{(ViewModel.DoneCount == 1 ? "" : "s")} converted.");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.Cancel();
}
