namespace ImageConverter.Core;

public sealed record PlannedJob(ConvertItem Item, string? Target, string? SkipReason);

/// <summary>Works out where each converted file goes, before anything is written.</summary>
public static class OutputPlanner
{
    public static List<PlannedJob> Plan(IReadOnlyList<ConvertItem> items, ConvertOptions options, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var ext = options.Info.Extension;
        // Targets already claimed by an earlier item in this batch: photo.jpg and photo.png both want photo.webp.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = new List<PlannedJob>(items.Count);

        foreach (var item in items)
        {
            var folder = TargetFolder(item, options);
            var stem = Path.GetFileNameWithoutExtension(item.FullPath) + Sanitize(options.Suffix);
            var target = Path.Combine(folder, stem + ext);

            bool IsTaken(string path) =>
                claimed.Contains(path) || string.Equals(path, item.FullPath, StringComparison.OrdinalIgnoreCase) || fileExists(path);

            // Clashes within the batch, and with the source itself, are always numbered: the source is
            // never overwritten. The conflict setting only decides what happens to files already on disk.
            bool isSource = string.Equals(target, item.FullPath, StringComparison.OrdinalIgnoreCase);
            if (claimed.Contains(target) || isSource)
            {
                target = NextFree(folder, stem, ext, IsTaken);
            }
            else if (fileExists(target))
            {
                if (options.Conflict == ConflictMode.Skip)
                {
                    jobs.Add(new PlannedJob(item, target, "A file with this name already exists"));
                    continue;
                }
                if (options.Conflict == ConflictMode.AddNumber)
                    target = NextFree(folder, stem, ext, IsTaken);
            }

            claimed.Add(target);
            jobs.Add(new PlannedJob(item, target, null));
        }
        return jobs;
    }

    private static string NextFree(string folder, string stem, string ext, Func<string, bool> isTaken)
    {
        for (int n = 2; ; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!isTaken(candidate)) return candidate;
        }
    }

    public static string TargetFolder(ConvertItem item, ConvertOptions options)
    {
        var sourceFolder = Path.GetDirectoryName(item.FullPath) ?? "";
        switch (options.Location)
        {
            case OutputLocation.Subfolder:
                var name = Sanitize(options.SubfolderName.Trim());
                return Path.Combine(sourceFolder, name.Length == 0 ? "converted" : name);
            case OutputLocation.Folder when !string.IsNullOrWhiteSpace(options.OutputFolder):
                if (options.KeepFolderStructure)
                {
                    var relative = Path.GetRelativePath(item.BaseFolder, sourceFolder);
                    if (relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                        return Path.Combine(options.OutputFolder, relative);
                }
                return options.OutputFolder;
            default:
                return sourceFolder;
        }
    }

    private static string Sanitize(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(text.Where(c => !invalid.Contains(c)).ToArray());
    }
}
