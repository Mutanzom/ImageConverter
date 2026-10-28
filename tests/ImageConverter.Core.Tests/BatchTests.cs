namespace ImageConverter.Core.Tests;

public class BatchTests
{
    /// <summary>Reports synchronously, unlike Progress&lt;T&gt;, so the test sees every update before it asserts.</summary>
    private sealed class Recorder : IProgress<JobUpdate>
    {
        public List<JobUpdate> Updates { get; } = [];
        public void Report(JobUpdate value) { lock (Updates) Updates.Add(value); }
        public ItemStatus Final(ConvertItem item) { lock (Updates) return Updates.Last(u => u.Item == item).Status; }
    }

    [Fact]
    public async Task ConvertsAllAndReportsEachOnce()
    {
        using var dir = new TempDir();
        for (int i = 0; i < 6; i++) dir.Image($"img{i}.png", 30, 30);
        dir.Image("existing/img0.jpg");
        File.WriteAllText(dir.File("broken.png"), "nope");
        var items = ImageScanner.Scan([dir.Path], recursive: false);
        var options = new ConvertOptions { Format = OutputFormat.Jpeg, SubfolderName = "existing", Conflict = ConflictMode.Skip };
        var jobs = OutputPlanner.Plan(items, options);
        var recorder = new Recorder();

        await BatchRunner.RunAsync(jobs, options, recorder, CancellationToken.None, parallelism: 3);

        var finals = items.ToDictionary(i => i.FileName, recorder.Final);
        Assert.Equal(ItemStatus.Skipped, finals["img0.png"]);
        Assert.Equal(ItemStatus.Failed, finals["broken.png"]);
        Assert.All(Enumerable.Range(1, 5), i => Assert.Equal(ItemStatus.Done, finals[$"img{i}.png"]));
        Assert.True(File.Exists(dir.File("existing/img5.jpg")));
        var failed = recorder.Updates.Single(u => u.Status == ItemStatus.Failed);
        Assert.DoesNotContain(" @ ", failed.Message);
    }

    [Fact]
    public async Task CancelledBatchMarksEverythingCancelledAndWritesNothing()
    {
        using var dir = new TempDir();
        for (int i = 0; i < 4; i++) dir.Image($"img{i}.png");
        var items = ImageScanner.Scan([dir.Path], recursive: false);
        var options = new ConvertOptions();
        var jobs = OutputPlanner.Plan(items, options);
        var recorder = new Recorder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await BatchRunner.RunAsync(jobs, options, recorder, cts.Token);

        Assert.All(items, i => Assert.Equal(ItemStatus.Cancelled, recorder.Final(i)));
        Assert.False(Directory.Exists(dir.File("converted")));
    }

    [Fact]
    public void OptionsSurviveJsonRoundTrip()
    {
        var options = new ConvertOptions { Format = OutputFormat.Dds, DdsCompression = DdsCompression.Dxt5, Quality = 42, Suffix = "_x" };
        var back = ConvertOptions.FromJson(options.ToJson());
        Assert.Equal((OutputFormat.Dds, DdsCompression.Dxt5, 42, "_x"), (back.Format, back.DdsCompression, back.Quality, back.Suffix));
    }
}
