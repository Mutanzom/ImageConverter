namespace ImageConverter.Core;

public static class ImageScanner
{
    public static readonly string[] DefaultExtensions =
    [
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".jxl",
        ".ico", ".dds", ".tga", ".psd",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".srw", ".pef",
    ];

    /// <summary>Finds image files in the given files/folders, sorted naturally by folder then name.</summary>
    public static List<ConvertItem> Scan(IEnumerable<string> paths, bool recursive, IReadOnlyCollection<string>? extensions = null)
    {
        var exts = new HashSet<string>(extensions ?? DefaultExtensions, StringComparer.OrdinalIgnoreCase);
        // Path → the folder it was found under (a dropped file's own folder).
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var option = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var root = Path.GetFullPath(p);
                foreach (var f in Directory.EnumerateFiles(root, "*", option))
                    if (exts.Contains(Path.GetExtension(f))) files.TryAdd(Path.GetFullPath(f), root);
            }
            else if (File.Exists(p) && exts.Contains(Path.GetExtension(p)))
            {
                var full = Path.GetFullPath(p);
                files.TryAdd(full, Path.GetDirectoryName(full) ?? "");
            }
        }

        return files
            .OrderBy(f => Path.GetDirectoryName(f.Key) ?? "", NaturalComparer.Instance)
            .ThenBy(f => Path.GetFileName(f.Key), NaturalComparer.Instance)
            .Select(f => new ConvertItem(f.Key, f.Value))
            .ToList();
    }
}
