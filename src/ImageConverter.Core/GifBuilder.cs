using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageMagick;

namespace ImageConverter.Core;

/// <summary>Settings for an animated GIF made from several images.</summary>
public partial class GifOptions : ObservableObject
{
    /// <summary>How long each frame shows, in milliseconds. GIF stores hundredths of a second.</summary>
    [ObservableProperty] private int _frameDelayMs = 500;

    /// <summary>0 = loop forever.</summary>
    [ObservableProperty] private int _loops;

    /// <summary>Play the frames forward, then backward (a b c b a b c…).</summary>
    [ObservableProperty] private bool _pingPong;

    /// <summary>Longest side of the GIF in pixels; 0 = the first image's own size.</summary>
    [ObservableProperty] private int _maxSize = 800;

    [ObservableProperty] private bool _keepTransparency = true;

    /// <summary>Smoother gradients with GIF's 256 colours, at the cost of a bigger file.</summary>
    [ObservableProperty] private bool _dither = true;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static GifOptions FromJson(string json) => JsonSerializer.Deserialize<GifOptions>(json) ?? new();
}

/// <summary>Builds an animated GIF from a list of images, in the order given.</summary>
public static class GifBuilder
{
    /// <summary>
    /// The canvas is the first image's size (fitted within <see cref="GifOptions.MaxSize"/>); later
    /// images are fitted inside it and centred. <paramref name="progress"/> gets the number of frames
    /// loaded so far. Written via a temporary file like conversions, so a cancelled or failed run
    /// leaves nothing behind. Returns the size of the written file.
    /// </summary>
    public static long Create(IReadOnlyList<string> sources, string target, GifOptions options, string background,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (sources.Count == 0) throw new ArgumentException("No images to animate.", nameof(sources));

        using var frames = new MagickImageCollection();
        uint canvasW = 0, canvasH = 0;
        var fill = options.KeepTransparency ? MagickColors.Transparent : Converter.ParseColor(background);
        // GIF delays are in 1/100 s; browsers treat 0 or 1 as "as fast as possible", usually ~10 fps.
        var delay = (uint)Math.Clamp((options.FrameDelayMs + 5) / 10, 2, 65535);

        for (int i = 0; i < sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var image = Converter.Read(sources[i]);
            image.AutoOrient();
            if (i == 0)
            {
                if (options.MaxSize > 0) image.Resize(new MagickGeometry((uint)options.MaxSize, (uint)options.MaxSize) { Greater = true });
                (canvasW, canvasH) = (image.Width, image.Height);
            }
            else if (image.Width != canvasW || image.Height != canvasH)
            {
                image.Resize(new MagickGeometry(canvasW, canvasH));
            }

            // Centre on a canvas of the first frame's size, also flattening alpha when it isn't kept.
            var frame = new MagickImage(fill, canvasW, canvasH);
            frame.Composite(image, Gravity.Center, CompositeOperator.Over);
            image.Dispose();

            frame.AnimationDelay = delay;
            // Clear each frame before the next: with transparency, frames would otherwise pile up.
            frame.GifDisposeMethod = GifDisposeMethod.Background;
            frames.Add(frame);
            progress?.Report(i + 1);
        }

        if (options.PingPong && frames.Count > 2)
        {
            // a b c d → a b c d c b: the ends aren't repeated, so the loop doesn't stutter.
            for (int i = frames.Count - 2; i > 0; i--) frames.Add(frames[i].Clone());
        }
        frames[0].AnimationIterations = (uint)Math.Max(0, options.Loops);

        ct.ThrowIfCancellationRequested();
        frames.Quantize(new QuantizeSettings
        {
            Colors = 256,
            DitherMethod = options.Dither ? DitherMethod.FloydSteinberg : DitherMethod.No,
        });
        ct.ThrowIfCancellationRequested();

        var folder = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.partial");
        try
        {
            frames.Write(temp, MagickFormat.Gif);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return new FileInfo(target).Length;
    }

    /// <summary>"animation.gif" next to the first image, numbered if that name is taken.</summary>
    public static string SuggestTarget(string firstSource, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var folder = Path.GetDirectoryName(firstSource) ?? "";
        var path = Path.Combine(folder, "animation.gif");
        for (int n = 2; fileExists(path); n++) path = Path.Combine(folder, $"animation ({n}).gif");
        return path;
    }
}
