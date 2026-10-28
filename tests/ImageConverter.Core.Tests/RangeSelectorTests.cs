namespace ImageConverter.Core.Tests;

public class RangeSelectorTests
{
    private static List<ConvertItem> DscRange(int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(n => new ConvertItem($@"C:\p\DSC_{n}.JPG", @"C:\p")).ToList();

    [Fact]
    public void NumbersPositionsWildcards()
    {
        var items = DscRange(5915, 5934);
        Assert.Equal(6, RangeSelector.Select("5915-5920", items).Count);
        Assert.Equal(["DSC_5915.JPG", "DSC_5925.JPG"], RangeSelector.Select("5925, 5915", items).Select(i => i.FileName)); // list order kept
        Assert.Equal(["DSC_5917.JPG", "DSC_5918.JPG"], RangeSelector.Select("#3-#4", items).Select(i => i.FileName));
        Assert.Equal(10, RangeSelector.Select("DSC_592*", items).Count);
        Assert.Equal(20, RangeSelector.Select("5920-5915; 1-99999", items).Count);
        Assert.Empty(RangeSelector.Select("#50-#60", items));
    }

    [Fact]
    public void WildcardWithExtensionMatchesFileNameOnly()
    {
        var items = new List<ConvertItem> { new(@"C:\p\a.dds", @"C:\p"), new(@"C:\p\b.png", @"C:\p"), new(@"C:\p\c.DDS", @"C:\p") };
        Assert.Equal(["a.dds", "c.DDS"], RangeSelector.Select("*.dds", items).Select(i => i.FileName));
    }

    [Fact]
    public async Task OnlyGivenItemsAreConverted()
    {
        using var dir = new TempDir();
        for (int i = 1; i <= 4; i++) dir.Image($"img{i}.png", 20, 20);
        var items = ImageScanner.Scan([dir.Path], recursive: false);
        var options = new ConvertOptions { Format = OutputFormat.Jpeg };
        var chosen = RangeSelector.Select("2-3", items);

        await BatchRunner.RunAsync(OutputPlanner.Plan(chosen, options), options, new Progress<JobUpdate>(), CancellationToken.None);

        Assert.Equal(["img2.jpg", "img3.jpg"], Directory.GetFiles(dir.File("converted")).Select(Path.GetFileName).Order());
    }
}
