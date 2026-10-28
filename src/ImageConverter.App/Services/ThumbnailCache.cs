using System.Runtime.InteropServices.WindowsRuntime;
using ImageConverter.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace ImageConverter.App.Services;

/// <summary>
/// Loads Explorer's thumbnails (fast, cached by Windows). When Explorer only has a file-type icon
/// (DDS, TGA, PSD…) the preview is decoded with Magick.NET instead. UI thread only.
/// </summary>
public sealed class ThumbnailCache
{
    private const int MaxEntries = 600;

    // Fast scrolling realizes dozens of rows per second; decoding them all at once on a slow disk
    // starves the ones actually on screen. Few at a time, and stale requests are skipped (see GetAsync).
    private readonly SemaphoreSlim _gate = new(4);

    private readonly Dictionary<ConvertItem, BitmapImage> _cache = [];
    private readonly LinkedList<ConvertItem> _order = [];
    private readonly Dictionary<ConvertItem, Task<BitmapImage?>> _pending = [];

    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
        _pending.Clear();
    }

    public bool TryGet(ConvertItem item, out BitmapImage? image) => _cache.TryGetValue(item, out image);

    /// <summary>
    /// Gets the thumbnail, sharing a load already in progress for the same item.
    /// <paramref name="stillWanted"/> is checked when the load reaches the front of the queue; if it
    /// returns false the load is skipped and the result is null.
    /// </summary>
    public Task<BitmapImage?> GetAsync(ConvertItem item, uint size, Func<bool> stillWanted)
    {
        if (_cache.TryGetValue(item, out var hit)) return Task.FromResult<BitmapImage?>(hit);
        if (_pending.TryGetValue(item, out var running)) return running;
        var task = LoadAsync(item, size, stillWanted);
        if (!task.IsCompleted) _pending[item] = task;
        return task;
    }

    private async Task<BitmapImage?> LoadAsync(ConvertItem item, uint size, Func<bool> stillWanted)
    {
        await _gate.WaitAsync();
        try
        {
            if (!stillWanted()) return null;
            var bmp = await FromShellAsync(item.FullPath, size) ?? await FromMagickAsync(item.FullPath, size);
            if (bmp != null) Add(item, bmp);
            return bmp;
        }
        catch (Exception)
        {
            return null; // missing file, disk busy: the caller may retry
        }
        finally
        {
            _gate.Release();
            _pending.Remove(item);
        }
    }

    private static async Task<BitmapImage?> FromShellAsync(string path, uint size)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail);
        // An icon means Windows can't render this format; a real preview beats a generic page icon.
        if (thumb == null || thumb.Type == ThumbnailType.Icon) return null;
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(thumb);
        return bmp;
    }

    private static async Task<BitmapImage?> FromMagickAsync(string path, uint size)
    {
        var png = await Task.Run(() => Preview.RenderPng(path, size));
        if (png == null) return null;
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream);
        return bmp;
    }

    private void Add(ConvertItem item, BitmapImage bmp)
    {
        _cache[item] = bmp;
        _order.AddLast(item);
        while (_order.Count > MaxEntries)
        {
            _cache.Remove(_order.First!.Value);
            _order.RemoveFirst();
        }
    }
}
