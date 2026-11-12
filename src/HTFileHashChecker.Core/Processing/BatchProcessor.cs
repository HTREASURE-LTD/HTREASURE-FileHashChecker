using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HTFileHashChecker.Core.Hashing;
using HTFileHashChecker.Core.IO;
using HTFileHashChecker.Core.Logging;

namespace HTFileHashChecker.Core.Processing
{
    public sealed class BatchOptions
    {
        public BatchOptions()
        {
            MaxParallelFiles = 1;
        }

        /// <summary>Number of files read at the same time (1-4). One is best for hard disks; two or more can help on SSDs.</summary>
        public int MaxParallelFiles { get; set; }
    }

    /// <summary>Snapshot of batch progress, raised on the caller's synchronisation context.</summary>
    public sealed class BatchProgress
    {
        public int Total { get; internal set; }
        public int Finished { get; internal set; }
        public int Succeeded { get; internal set; }
        public int Failed { get; internal set; }
        public int Skipped { get; internal set; }
        public long TotalBytes { get; internal set; }
        public long BytesDone { get; internal set; }
        public HashItem CurrentItem { get; internal set; }
        public long CurrentItemBytes { get; internal set; }
        public long CurrentItemLength { get; internal set; }
        public TimeSpan Elapsed { get; internal set; }

        /// <summary>The item whose state changed (finished or started), if any.</summary>
        public HashItem ChangedItem { get; internal set; }
    }

    public sealed class BatchSummary
    {
        public int Processed { get; internal set; }
        public int Calculated { get; internal set; }
        public int Matched { get; internal set; }
        public int Mismatched { get; internal set; }
        public int Errors { get; internal set; }
        public int Missing { get; internal set; }
        public int Unexpected { get; internal set; }
        public int Invalid { get; internal set; }
        public int Skipped { get; internal set; }
        public int CancelledItems { get; internal set; }
        public bool WasCancelled { get; internal set; }
        public long BytesHashed { get; internal set; }
        public TimeSpan Elapsed { get; internal set; }

        public int Failed { get { return Mismatched + Errors + Missing + Invalid; } }

        /// <summary>Recomputes counts from the final item states (used for reports and summaries).</summary>
        public static BatchSummary FromItems(IEnumerable<HashItem> items, TimeSpan elapsed, bool cancelled)
        {
            var s = new BatchSummary { Elapsed = elapsed, WasCancelled = cancelled };
            foreach (var item in items)
            {
                switch (item.Status)
                {
                    case ItemStatus.Hashed: s.Calculated++; break;
                    case ItemStatus.Match: s.Matched++; break;
                    case ItemStatus.Mismatch: s.Mismatched++; break;
                    case ItemStatus.Error: s.Errors++; break;
                    case ItemStatus.Missing: s.Missing++; break;
                    case ItemStatus.Unexpected: s.Unexpected++; break;
                    case ItemStatus.Invalid: s.Invalid++; break;
                    case ItemStatus.Skipped: s.Skipped++; break;
                    case ItemStatus.Cancelled: s.CancelledItems++; break;
                }
                if (item.Status != ItemStatus.Pending && item.Status != ItemStatus.Cancelled) s.Processed++;
            }
            return s;
        }
    }

    /// <summary>
    /// Hashes and verifies items on a small, bounded number of background workers so the user interface stays
    /// responsive and the disk is not flooded with simultaneous reads.
    /// </summary>
    public sealed class BatchProcessor
    {
        private readonly FileHasher hasher;
        private readonly ILogger logger;

        public BatchProcessor(FileHasher hasher, ILogger logger)
        {
            if (hasher == null) throw new ArgumentNullException("hasher");
            this.hasher = hasher;
            this.logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Processes every item that is not already final from planning (missing, unexpected, invalid). Each item is
        /// hashed with its own <see cref="HashItem.Algorithm"/> and compared with <see cref="HashItem.Expected"/> when set.
        /// </summary>
        public Task<BatchSummary> RunAsync(IReadOnlyList<HashItem> items, BatchOptions options, IProgress<BatchProgress> progress,
            CancellationToken cancellationToken)
        {
            if (items == null) throw new ArgumentNullException("items");
            if (options == null) options = new BatchOptions();
            int workers = Math.Max(1, Math.Min(4, options.MaxParallelFiles));
            return Task.Run(() => Run(items, workers, progress, cancellationToken));
        }

        private BatchSummary Run(IReadOnlyList<HashItem> items, int workerCount, IProgress<BatchProgress> progress, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            var queue = new ConcurrentQueue<HashItem>();
            long totalBytes = 0;
            int total = 0;
            foreach (var item in items)
            {
                if (item.IsPlanFinal) continue;
                item.ResetResult();
                queue.Enqueue(item);
                total++;
                totalBytes += item.Size ?? 0;
            }

            var state = new SharedState { Total = total, TotalBytes = totalBytes };
            logger.Info("Batch started: " + total + " file(s), " + workerCount + " worker(s).");

            var tasks = new Task[workerCount];
            for (int i = 0; i < workerCount; i++)
                tasks[i] = Task.Factory.StartNew(() => Worker(queue, state, progress, stopwatch, token),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Task.WaitAll(tasks);

            // Anything still queued after cancellation is marked as cancelled.
            HashItem leftover;
            while (queue.TryDequeue(out leftover))
            {
                leftover.Status = ItemStatus.Cancelled;
                leftover.Message = "Cancelled before it was processed.";
            }

            stopwatch.Stop();
            var summary = BatchSummary.FromItems(items, stopwatch.Elapsed, token.IsCancellationRequested);
            summary.BytesHashed = Interlocked.Read(ref state.BytesDone);
            Report(progress, state, stopwatch, null, null, 0, 0);
            logger.Info(string.Format("Batch finished in {0:0.0}s: {1} calculated, {2} matched, {3} mismatched, {4} errors, {5} missing, {6} cancelled.",
                summary.Elapsed.TotalSeconds, summary.Calculated, summary.Matched, summary.Mismatched, summary.Errors, summary.Missing, summary.CancelledItems));
            return summary;
        }

        private sealed class SharedState
        {
            public int Total;
            public long TotalBytes;
            public int Finished;
            public int Succeeded;
            public int Failed;
            public int Skipped;
            public long BytesDone;
        }

        private void Worker(ConcurrentQueue<HashItem> queue, SharedState state, IProgress<BatchProgress> progress, Stopwatch stopwatch,
            CancellationToken token)
        {
            HashItem item;
            while (!token.IsCancellationRequested && queue.TryDequeue(out item))
            {
                item.Status = ItemStatus.Hashing;
                long length = item.Size ?? 0;
                Report(progress, state, stopwatch, item, item, 0, length);

                long lastSeen = 0;
                var fileProgress = new SyncProgress<long>(done =>
                {
                    Interlocked.Add(ref state.BytesDone, done - lastSeen);
                    lastSeen = done;
                    Report(progress, state, stopwatch, item, null, done, length);
                });

                try
                {
                    FileHashOutcome outcome = hasher.ComputeHash(item.FullPath, item.Algorithm, token, fileProgress, item.ContainmentRoot);
                    item.HashBytes = outcome.Digest;
                    item.HashHex = outcome.Hex;
                    item.Size = outcome.Length;
                    item.Elapsed = outcome.Elapsed;
                    if (item.Expected != null)
                    {
                        if (item.Expected.Algorithm != item.Algorithm)
                        {
                            item.Status = ItemStatus.Error;
                            item.Message = "The expected hash is " + HashAlgorithms.Get(item.Expected.Algorithm).DisplayName
                                + " but the file was hashed with " + HashAlgorithms.Get(item.Algorithm).DisplayName + ".";
                        }
                        else if (item.Expected.Matches(outcome.Digest))
                        {
                            item.Status = ItemStatus.Match;
                            item.Message = HashItem.StatusDescription(ItemStatus.Match);
                        }
                        else
                        {
                            item.Status = ItemStatus.Mismatch;
                            item.Message = HashItem.StatusDescription(ItemStatus.Mismatch);
                        }
                    }
                    else
                    {
                        item.Status = ItemStatus.Hashed;
                        item.Message = null;
                    }

                    if (item.Status == ItemStatus.Hashed || item.Status == ItemStatus.Match) Interlocked.Increment(ref state.Succeeded);
                    else Interlocked.Increment(ref state.Failed);
                }
                catch (OperationCanceledException)
                {
                    item.Status = ItemStatus.Cancelled;
                    item.Message = "Cancelled while it was being read.";
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    if (item.Expected != null && ErrorDescriber.IsNotFound(ex))
                    {
                        item.Status = ItemStatus.Missing;
                        item.Message = HashItem.StatusDescription(ItemStatus.Missing);
                    }
                    else
                    {
                        item.Status = ItemStatus.Error;
                        item.Message = ErrorDescriber.Describe(ex);
                    }
                    Interlocked.Increment(ref state.Failed);
                    // File name only: full paths are not written to logs.
                    logger.Warn("Could not hash '" + item.FileName + "': " + ex.GetType().Name + " - " + item.Message);
                }

                // Keep overall byte progress consistent even when the file was shorter, longer or unreadable.
                long expectedAdvance = item.Status == ItemStatus.Cancelled ? lastSeen : Math.Max(length, lastSeen);
                Interlocked.Add(ref state.BytesDone, expectedAdvance - lastSeen);
                Interlocked.Increment(ref state.Finished);
                Report(progress, state, stopwatch, null, item, 0, 0);
            }
        }

        private static void Report(IProgress<BatchProgress> progress, SharedState state, Stopwatch stopwatch, HashItem current,
            HashItem changed, long currentBytes, long currentLength)
        {
            if (progress == null) return;
            progress.Report(new BatchProgress
            {
                Total = state.Total,
                Finished = Volatile.Read(ref state.Finished),
                Succeeded = Volatile.Read(ref state.Succeeded),
                Failed = Volatile.Read(ref state.Failed),
                Skipped = Volatile.Read(ref state.Skipped),
                TotalBytes = state.TotalBytes,
                BytesDone = Interlocked.Read(ref state.BytesDone),
                CurrentItem = current,
                CurrentItemBytes = currentBytes,
                CurrentItemLength = currentLength,
                Elapsed = stopwatch.Elapsed,
                ChangedItem = changed,
            });
        }

        /// <summary>IProgress that invokes the handler synchronously on the reporting thread.</summary>
        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> handler;
            public SyncProgress(Action<T> handler) { this.handler = handler; }
            public void Report(T value) { handler(value); }
        }
    }
}
