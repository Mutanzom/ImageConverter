using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageConverter.App.Services;
using ImageConverter.Core;
using Microsoft.UI.Dispatching;

namespace ImageConverter.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _replanTimer;
    private CancellationTokenSource? _runCts;
    private List<string> _sources = [];

    public MainViewModel(DispatcherQueue dispatcher, AppSettings settings)
    {
        _dispatcher = dispatcher;
        _replanTimer = dispatcher.CreateTimer();
        _replanTimer.Interval = TimeSpan.FromMilliseconds(120);
        _replanTimer.IsRepeating = false;
        _replanTimer.Tick += (_, _) => Replan();

        _includeSubfolders = settings.IncludeSubfolders;

        ConvertOptions? restored = null;
        if (!string.IsNullOrEmpty(settings.OptionsJson))
        {
            try { restored = ConvertOptions.FromJson(settings.OptionsJson); }
            catch (Exception) { /* ignore broken saved options */ }
        }
        Options = restored ?? new ConvertOptions();
        // A saved format this build can't write (e.g. HEIC without an encoder) falls back to the first.
        if (!FormatInfo.Writable.Any(f => f.Format == Options.Format)) Options.Format = FormatInfo.Writable[0].Format;
        Options.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedFormat));
            ScheduleReplan();
        };

        try { Gif = string.IsNullOrEmpty(settings.GifJson) ? new() : GifOptions.FromJson(settings.GifJson); }
        catch (Exception) { Gif = new(); }
    }

    // ---------------------------------------------------------------- state

    public ConvertOptions Options { get; }

    public GifOptions Gif { get; }

    public ObservableCollection<ConvertItem> Items { get; } = [];

    /// <summary>Selected items in list order, kept in sync by the view.</summary>
    public IReadOnlyList<ConvertItem> SelectedItems { get; private set; } = [];

    public void SetSelection(IEnumerable<ConvertItem> selected)
    {
        var set = selected.ToHashSet();
        SelectedItems = Items.Where(set.Contains).ToList();
        SelectionCount = SelectedItems.Count;
    }

    public List<ConvertItem> Match(string text) => RangeSelector.Select(text, Items);

    public FormatInfo SelectedFormat
    {
        get => Options.Info;
        set { if (value != null) Options.Format = value.Format; }
    }

    [ObservableProperty] private bool _includeSubfolders;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanConvert), nameof(CanConvertSelected), nameof(CanCreateGif), nameof(CanRemove), nameof(CanClear), nameof(IsIdle))] private bool _isLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanConvert), nameof(CanConvertSelected), nameof(CanCreateGif), nameof(CanRemove), nameof(CanClear), nameof(IsIdle), nameof(StatusText))] private bool _isRunning;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText), nameof(HasItems), nameof(CanConvert), nameof(CanClear))] private int _totalCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText), nameof(CanConvert))] private int _toConvertCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText))] private int _finishedCount;
    /// <summary>How many images the current (or last) run converts: the whole list or the selection.</summary>
    [ObservableProperty] private int _runCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText))] private int _doneCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText))] private int _failedCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasOutputFolder))] private string? _lastOutputFolder;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SelectionText), nameof(CanConvertSelected), nameof(ConvertSelectedText), nameof(HasSelection), nameof(CanRemove), nameof(CanCreateGif))]
    private int _selectionCount;
    [ObservableProperty] private string _folderTitle = "";

    public bool HasItems => TotalCount > 0;
    public bool IsIdle => !IsRunning && !IsLoading;
    public bool CanConvert => IsIdle && ToConvertCount > 0;
    public bool HasSelection => SelectionCount > 0;
    public bool CanRemove => IsIdle && SelectionCount > 0;
    public bool CanClear => IsIdle && TotalCount > 0;
    public bool CanConvertSelected => IsIdle && SelectionCount > 0;
    /// <summary>An animation needs at least two frames.</summary>
    public bool CanCreateGif => IsIdle && SelectionCount >= 2;
    public string SelectionText => SelectionCount == 0 ? "Nothing selected" : $"{SelectionCount} selected";
    public string ConvertSelectedText => SelectionCount == 0 ? "Convert selected" : $"Convert selected ({SelectionCount})";
    public bool HasOutputFolder => LastOutputFolder != null;

    public string StatusText
    {
        get
        {
            if (TotalCount == 0) return "No images loaded";
            if (IsRunning) return _creatingGif
                ? FinishedCount < RunCount ? $"Creating GIF: frame {FinishedCount + 1} of {RunCount}…" : "Creating GIF: saving…"
                : $"Converting {FinishedCount} of {RunCount}…";
            var s = $"{TotalCount} image{(TotalCount == 1 ? "" : "s")}";
            if (DoneCount > 0 || FailedCount > 0)
            {
                s += $" · {DoneCount} converted";
                if (FailedCount > 0) s += $" · {FailedCount} failed";
            }
            else if (ToConvertCount < TotalCount)
            {
                s += $" · {TotalCount - ToConvertCount} will be skipped";
            }
            return s;
        }
    }

    // ---------------------------------------------------------------- loading

    public async Task LoadAsync(IEnumerable<string> paths, bool append)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        _sources = append ? [.. _sources, .. list] : list;
        await RescanAsync(keepExisting: append);
    }

    public Task ReloadAsync() => RescanAsync(keepExisting: false);

    private async Task RescanAsync(bool keepExisting)
    {
        IsLoading = true;
        try
        {
            var recursive = IncludeSubfolders;
            var sources = _sources.ToList();
            var found = await Task.Run(() => ImageScanner.Scan(sources, recursive));

            var known = keepExisting
                ? Items.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            if (!keepExisting) Items.Clear();
            foreach (var item in found)
                if (!known.Contains(item.FullPath)) Items.Add(item);

            FolderTitle = sources.Count == 1 ? sources[0] : sources.Count > 1 ? $"{sources.Count} locations" : "";
            Replan();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Remove(IEnumerable<ConvertItem> items)
    {
        foreach (var item in items.ToList()) Items.Remove(item);
        if (Items.Count == 0) _sources.Clear();
        SetSelection(SelectedItems.Where(Items.Contains));
        Replan();
    }

    public void Clear()
    {
        Items.Clear();
        _sources.Clear();
        FolderTitle = "";
        Replan();
    }

    // ---------------------------------------------------------------- planning

    private void ScheduleReplan()
    {
        if (IsRunning) return;
        _replanTimer.Stop();
        _replanTimer.Start();
    }

    /// <summary>
    /// Shows each item's target name, and which ones will be skipped, before converting. Results of
    /// the last run stay on screen until something changes (options, the list), which replans.
    /// </summary>
    public void Replan()
    {
        if (IsRunning) return;
        var jobs = OutputPlanner.Plan(Items, Options);
        foreach (var job in jobs)
        {
            job.Item.TargetPath = job.Target;
            job.Item.Status = job.SkipReason == null ? ItemStatus.Pending : ItemStatus.Skipped;
            job.Item.Message = job.SkipReason;
            job.Item.OutputBytes = 0;
        }
        ResetResults();
        ToConvertCount = jobs.Count(j => j.SkipReason == null);
        TotalCount = Items.Count;
    }

    private void ResetResults()
    {
        DoneCount = 0;
        FailedCount = 0;
        LastOutputFolder = null;
    }

    // ---------------------------------------------------------------- converting

    /// <summary>Converts <paramref name="only"/> if given (the selection), otherwise every image in the list.</summary>
    public async Task ConvertAsync(IReadOnlyList<ConvertItem>? only = null)
    {
        if (only == null ? !CanConvert : !IsIdle || only.Count == 0) return;
        // Plan again right before writing: files may have appeared since the list was shown.
        Replan();
        var jobs = OutputPlanner.Plan(only ?? Items, Options);
        // Planned on their own, the chosen images may get other names than in the full-list preview
        // (no clash with an unselected photo.jpg / photo.png twin), so show what will really be written.
        foreach (var job in jobs) job.Item.TargetPath = job.Target;

        _runCts = new CancellationTokenSource();
        FinishedCount = 0;
        RunCount = jobs.Count(j => j.SkipReason == null);
        IsRunning = true;

        var progress = new UiProgress(_dispatcher, u =>
        {
            u.Item.Status = u.Status;
            if (u.Status == ItemStatus.Converting) return;
            u.Item.Message = u.Message;
            u.Item.OutputBytes = u.OutputBytes;
            if (u.Status == ItemStatus.Skipped) return;
            FinishedCount++;
            if (u.Status == ItemStatus.Done) DoneCount++;
            if (u.Status == ItemStatus.Failed) FailedCount++;
        });

        try
        {
            await BatchRunner.RunAsync(jobs, Options, progress, _runCts.Token);
            await progress.FlushAsync();
        }
        finally
        {
            IsRunning = false;
            _runCts.Dispose();
            _runCts = null;
        }

        var firstDone = jobs.FirstOrDefault(j => j.Item.Status == ItemStatus.Done)?.Target;
        LastOutputFolder = firstDone == null ? null : Path.GetDirectoryName(firstDone);
    }

    /// <summary>
    /// Posts updates to the UI thread in order. Unlike Progress&lt;T&gt;, it can be flushed, so the
    /// summary after a run counts every update.
    /// </summary>
    private sealed class UiProgress(DispatcherQueue dispatcher, Action<JobUpdate> handler) : IProgress<JobUpdate>
    {
        public void Report(JobUpdate value) => dispatcher.TryEnqueue(() => handler(value));

        public Task FlushAsync()
        {
            var done = new TaskCompletionSource();
            // Same priority, so it runs after everything already queued.
            if (!dispatcher.TryEnqueue(() => done.SetResult())) done.SetResult();
            return done.Task;
        }
    }

    public void Cancel() => _runCts?.Cancel();

    private bool _creatingGif;

    /// <summary>
    /// Makes an animated GIF of <paramref name="frames"/> in list order. Returns the file size, or
    /// null when cancelled. Read errors (a broken frame) are thrown to the caller.
    /// </summary>
    public async Task<long?> CreateGifAsync(IReadOnlyList<ConvertItem> frames, string target)
    {
        if (!IsIdle || frames.Count < 2) return null;
        _runCts = new CancellationTokenSource();
        _creatingGif = true;
        FinishedCount = 0;
        RunCount = frames.Count;
        IsRunning = true;
        try
        {
            var paths = frames.Select(f => f.FullPath).ToList();
            var options = GifOptions.FromJson(Gif.ToJson()); // snapshot
            var background = Options.Background;
            var progress = new Progress<int>(n => FinishedCount = n);
            var token = _runCts.Token;
            var bytes = await Task.Run(() => GifBuilder.Create(paths, target, options, background, progress, token));
            LastOutputFolder = Path.GetDirectoryName(target);
            return bytes;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _creatingGif = false;
            IsRunning = false;
            _runCts.Dispose();
            _runCts = null;
        }
    }

    public void SaveTo(AppSettings settings)
    {
        settings.IncludeSubfolders = IncludeSubfolders;
        settings.OptionsJson = Options.ToJson();
        settings.GifJson = Gif.ToJson();
    }
}
