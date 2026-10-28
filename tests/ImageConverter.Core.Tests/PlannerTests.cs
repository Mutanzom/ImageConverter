namespace ImageConverter.Core.Tests;

public class PlannerTests
{
    private static ConvertItem Item(string path, string? baseFolder = null) =>
        new(path, baseFolder ?? Path.GetDirectoryName(path)!);

    private static List<PlannedJob> Plan(ConvertOptions options, IEnumerable<string>? existing = null, params ConvertItem[] items)
    {
        var onDisk = new HashSet<string>(existing ?? [], StringComparer.OrdinalIgnoreCase);
        return OutputPlanner.Plan(items, options, onDisk.Contains);
    }

    [Fact]
    public void SameStemInOneBatchGetsNumbered()
    {
        var jobs = Plan(new ConvertOptions { Format = OutputFormat.WebP, Location = OutputLocation.SameFolder }, null,
            Item(@"C:\p\photo.jpg"), Item(@"C:\p\photo.png"));

        Assert.Equal(@"C:\p\photo.webp", jobs[0].Target);
        Assert.Equal(@"C:\p\photo (2).webp", jobs[1].Target);
    }

    [Fact]
    public void SourceIsNeverOverwrittenEvenWithOverwriteOn()
    {
        var jobs = Plan(new ConvertOptions { Format = OutputFormat.Png, Location = OutputLocation.SameFolder, Conflict = ConflictMode.Overwrite },
            [@"C:\p\a.png"], Item(@"C:\p\a.png"));

        Assert.Equal(@"C:\p\a (2).png", jobs[0].Target);
        Assert.Null(jobs[0].SkipReason);
    }

    [Theory]
    [InlineData(ConflictMode.AddNumber, @"C:\p\converted\a (3).webp", false)]
    [InlineData(ConflictMode.Overwrite, @"C:\p\converted\a.webp", false)]
    [InlineData(ConflictMode.Skip, @"C:\p\converted\a.webp", true)]
    public void ExistingFilesFollowConflictSetting(ConflictMode mode, string expected, bool skipped)
    {
        var jobs = Plan(new ConvertOptions { Format = OutputFormat.WebP, Conflict = mode },
            [@"C:\p\converted\a.webp", @"C:\p\converted\a (2).webp"], Item(@"C:\p\a.jpg"));

        Assert.Equal(expected, jobs[0].Target);
        Assert.Equal(skipped, jobs[0].SkipReason != null);
    }

    [Fact]
    public void SubfolderAndSuffix()
    {
        var jobs = Plan(new ConvertOptions { Format = OutputFormat.Jpeg, SubfolderName = "out", Suffix = "_small" }, null,
            Item(@"C:\p\IMG_1.png"));

        Assert.Equal(@"C:\p\out\IMG_1_small.jpg", jobs[0].Target);
    }

    [Fact]
    public void OutputFolderKeepsTreeBelowOpenedFolder()
    {
        var options = new ConvertOptions { Format = OutputFormat.Png, Location = OutputLocation.Folder, OutputFolder = @"D:\out" };
        var jobs = Plan(options, null, Item(@"C:\shoot\day1\a.jpg", @"C:\shoot"), Item(@"C:\shoot\b.jpg", @"C:\shoot"));

        Assert.Equal(@"D:\out\day1\a.png", jobs[0].Target);
        Assert.Equal(@"D:\out\b.png", jobs[1].Target);

        options.KeepFolderStructure = false;
        jobs = Plan(options, null, Item(@"C:\shoot\day1\a.jpg", @"C:\shoot"));
        Assert.Equal(@"D:\out\a.png", jobs[0].Target);
    }

    [Fact]
    public void FlatOutputFolderNumbersClashesFromDifferentFolders()
    {
        var options = new ConvertOptions { Format = OutputFormat.Png, Location = OutputLocation.Folder, OutputFolder = @"D:\out", KeepFolderStructure = false };
        var jobs = Plan(options, null, Item(@"C:\a\x.jpg", @"C:\"), Item(@"C:\b\x.jpg", @"C:\"));

        Assert.Equal(@"D:\out\x.png", jobs[0].Target);
        Assert.Equal(@"D:\out\x (2).png", jobs[1].Target);
    }

    [Fact]
    public void ExtensionMatchIsCultureInvariant()
    {
        // Turkish locale: "I".ToLower() is "ı", which must not break ".JPG" / ".TIF" handling.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            using var dir = new TempDir();
            dir.Image("PHOTO.JPG");
            dir.Image("SCAN.TIF");
            var items = ImageScanner.Scan([dir.Path], recursive: false);
            Assert.Equal(["PHOTO.JPG", "SCAN.TIF"], items.Select(i => i.FileName));
            Assert.Equal(["JPG", "TIF"], items.Select(i => i.SourceFormat));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
