namespace ImageConverter.Core;

public sealed record JobUpdate(ConvertItem Item, ItemStatus Status, string? Message = null, long OutputBytes = 0);

/// <summary>Runs planned jobs a few at a time and reports every status change.</summary>
public static class BatchRunner
{
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 1, 1, 8);

    /// <summary>
    /// Every job gets exactly one final update (Done, Skipped, Failed or Cancelled), also when
    /// <paramref name="ct"/> is cancelled part way: jobs not started yet are reported as Cancelled.
    /// </summary>
    public static Task RunAsync(IReadOnlyList<PlannedJob> jobs, ConvertOptions options, IProgress<JobUpdate> progress,
        CancellationToken ct, int? parallelism = null)
    {
        // A snapshot, so editing options in the UI mid-run doesn't change half the batch.
        var snapshot = options.Clone();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = parallelism ?? DefaultParallelism };

        return Parallel.ForEachAsync(jobs, parallel, (job, _) =>
        {
            progress.Report(RunOne(job, snapshot, progress, ct));
            return ValueTask.CompletedTask;
        });
    }

    private static JobUpdate RunOne(PlannedJob job, ConvertOptions options, IProgress<JobUpdate> progress, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return new(job.Item, ItemStatus.Cancelled);
        if (job.SkipReason != null || job.Target == null) return new(job.Item, ItemStatus.Skipped, job.SkipReason);

        progress.Report(new(job.Item, ItemStatus.Converting));
        try
        {
            var bytes = Converter.Convert(job.Item.FullPath, job.Target, options, ct);
            return new(job.Item, ItemStatus.Done, null, bytes);
        }
        catch (OperationCanceledException)
        {
            return new(job.Item, ItemStatus.Cancelled);
        }
        catch (Exception e)
        {
            return new(job.Item, ItemStatus.Failed, Describe(e));
        }
    }

    /// <summary>ImageMagick messages end with "@ error/coders/foo.c/Bar/123"; users don't need that part.</summary>
    public static string Describe(Exception e)
    {
        var message = e.Message;
        int at = message.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0) message = message[..at];
        return message.Trim();
    }
}
