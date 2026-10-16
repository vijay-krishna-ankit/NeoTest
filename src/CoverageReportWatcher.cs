using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCoverage
{
    internal sealed class CoverageReportWatcher : IDisposable
    {
        private readonly FileSystemWatcher watcher;
        private readonly string testResultsDirectory;
        private readonly object scheduleLock = new object();
        private readonly object cacheLock = new object();
        private readonly Dictionary<string, CachedCoverageReport> coverageCache = new Dictionary<string, CachedCoverageReport>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource pendingRefresh;
        private DateTime lastReadableCoverageUtc;
        private int missingResultRetentionScheduled;
        private volatile bool disposed;

        public CoverageReportWatcher(string solutionDirectory, string resultsDirectory)
        {
            testResultsDirectory = Path.GetFullPath(Path.Combine(solutionDirectory, resultsDirectory));
            Directory.CreateDirectory(testResultsDirectory);
            watcher = new FileSystemWatcher(testResultsDirectory, "*.*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Created += OnReportChanged;
            watcher.Changed += OnReportChanged;
            watcher.Renamed += OnReportRenamed;
            watcher.Deleted += OnReportChanged;
            watcher.Error += OnWatcherError;
            CoverageStore.SetStatus("Watching " + resultsDirectory + " for Cobertura/OpenCover coverage and TRX test reports.");
            // Reports can already exist when the extension is loaded; a watcher only observes future changes.
            ScheduleRefresh();
        }

        private void OnReportChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleRefresh();
        }

        private void OnReportRenamed(object sender, RenamedEventArgs e)
        {
            ScheduleRefresh();
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // FileSystemWatcher can lose individual notifications under heavy parallel test output.
            // A full rescan is sufficient to recover because reports are read from disk, not events.
            CoverageStore.SetStatus("Coverage file notifications were interrupted; rescanning TestResults…");
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (disposed) return;
            CoverageStore.SetStatus("Reading updated coverage reports…");
            CancellationTokenSource refresh;
            lock (scheduleLock)
            {
                pendingRefresh?.Cancel();
                pendingRefresh?.Dispose();
                pendingRefresh = new CancellationTokenSource();
                refresh = pendingRefresh;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Parallel test hosts can finish within milliseconds of one another. Debounce their
                    // events, then merge every completed report so the editor never flashes partial data.
                    await Task.Delay(500, refresh.Token);
                    // Test runners and cleanup jobs can replace the complete results directory between
                    // watcher events. Recreate the empty root so a transient deletion is treated as an
                    // empty report set rather than a failed refresh.
                    Directory.CreateDirectory(testResultsDirectory);
                    var merged = new Dictionary<string, CoverageLineSet>(StringComparer.OrdinalIgnoreCase);
                    var newestResultTime = DateTime.MinValue;
                    var coverageReportCount = 0;
                    var readableCoverageReportCount = 0;
                    var unreadableReportCount = 0;
                    var reports = Directory.EnumerateFiles(testResultsDirectory, "*.cobertura.xml", SearchOption.AllDirectories)
                        .Concat(Directory.EnumerateFiles(testResultsDirectory, "*.opencover.xml", SearchOption.AllDirectories))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    foreach (var report in reports)
                    {
                        try
                        {
                            coverageReportCount++;
                            foreach (var entry in ReadCoverageReport(report))
                            {
                                if (!CoverageFileFilters.ShouldInclude(entry.Key)) continue;
                                if (!merged.TryGetValue(entry.Key, out var lines))
                                    merged[entry.Key] = lines = new CoverageLineSet();
                                lines.Reportable.UnionWith(entry.Value.Reportable);
                                lines.Covered.UnionWith(entry.Value.Covered);
                                foreach (var hit in entry.Value.LineHits)
                                {
                                    if (!lines.LineHits.TryGetValue(hit.Key, out var currentHits) || hit.Value > currentHits)
                                        lines.LineHits[hit.Key] = hit.Value;
                                }
                                foreach (var branch in entry.Value.Branches)
                                {
                                    if (!lines.Branches.TryGetValue(branch.Key, out var current)
                                        || branch.Value.Total > current.Total
                                        || (branch.Value.Total == current.Total && branch.Value.Covered > current.Covered))
                                        lines.Branches[branch.Key] = branch.Value;
                                }
                            }
                            readableCoverageReportCount++;
                            newestResultTime = Max(newestResultTime, File.GetLastWriteTimeUtc(report));
                        }
                        // A test host can still be closing a report. Its subsequent Changed event retries it.
                        catch (IOException) { unreadableReportCount++; }
                        catch (System.Xml.XmlException) { unreadableReportCount++; }
                    }
                    var trxReports = Directory.EnumerateFiles(testResultsDirectory, "*.trx", SearchOption.AllDirectories)
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .ToList();

                    // A new test execution supersedes prior executions. Parallel test hosts can produce
                    // several TRX files, so include the newest completion batch rather than retaining a
                    // historical failure forever. A clean later batch therefore removes its red markers.
                    if (trxReports.Count > 0)
                    {
                        var newestCompletion = trxReports[0].LastWriteTimeUtc;
                        newestResultTime = Max(newestResultTime, newestCompletion);
                        foreach (var report in trxReports.Where(file => newestCompletion - file.LastWriteTimeUtc <= TimeSpan.FromSeconds(CoverageStyle.ParallelTestResultWindowSeconds)))
                        {
                            try
                            {
                                foreach (var failure in TrxReader.ReadFailedLocations(report.FullName))
                                {
                                    if (!merged.TryGetValue(failure.FailurePath, out var lines))
                                        merged[failure.FailurePath] = lines = new CoverageLineSet();
                                    lines.Failed.Add(failure.FailureLine);
                                    if (!lines.FailureNavigation.ContainsKey(failure.FailureLine))
                                        lines.FailureNavigation[failure.FailureLine] = failure;
                                }
                            }
                            catch (IOException) { }
                            catch (System.Xml.XmlException) { }
                        }
                    }
                    if (!disposed && !refresh.IsCancellationRequested)
                    {
                        if (coverageReportCount > 0 && readableCoverageReportCount == 0 && CoverageStore.HasResults)
                        {
                            CoverageStore.SetStatus("Coverage reports are still being written or are invalid; retaining the last valid coverage result.");
                            return;
                        }
                        if (readableCoverageReportCount > 0)
                            lastReadableCoverageUtc = DateTime.UtcNow;
                        else if (coverageReportCount == 0 && CoverageStore.HasResults
                            && CoverageStyle.RetainMissingResultsSeconds > 0
                            && DateTime.UtcNow - lastReadableCoverageUtc <= TimeSpan.FromSeconds(CoverageStyle.RetainMissingResultsSeconds))
                        {
                            CoverageStore.SetStatus("Coverage reports were removed; retaining the last result for "
                                + CoverageStyle.RetainMissingResultsSeconds + " seconds.");
                            ScheduleRetentionExpiryRefresh();
                            return;
                        }
                        PruneCache(reports);
                        DiffCoverage.Refresh(testResultsDirectory, CoverageStyle.DiffBaseReference);
                        CoverageStore.Replace(merged, newestResultTime);
                        var status = coverageReportCount == 0
                            ? "No supported coverage report (*.cobertura.xml or *.opencover.xml) was found under " + testResultsDirectory + "."
                            : $"Loaded {readableCoverageReportCount} coverage report(s) for {merged.Count} file(s)."
                                + (unreadableReportCount > 0 ? $" {unreadableReportCount} report(s) are still being written or are invalid." : string.Empty);
                        CoverageStore.SetStatus(status + " " + QualityGateEvaluator.Evaluate() + DiffCoverage.StatusSuffix);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    CoverageStore.SetStatus("Coverage refresh failed: " + exception.Message);
                }
            });
        }

        public void RefreshNow()
        {
            if (disposed) return;
            ScheduleRefresh();
        }

        public void ConfigurationChanged()
        {
            if (disposed) return;
            lock (cacheLock) coverageCache.Clear();
            ScheduleRefresh();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            watcher.Dispose();
            lock (scheduleLock)
            {
                pendingRefresh?.Cancel();
                pendingRefresh?.Dispose();
            }
        }

        private static DateTime Max(DateTime first, DateTime second) => first > second ? first : second;

        private IDictionary<string, CoverageLineSet> ReadCoverageReport(string reportPath)
        {
            var info = new FileInfo(reportPath);
            lock (cacheLock)
            {
                if (coverageCache.TryGetValue(reportPath, out var cached)
                    && cached.Length == info.Length && cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
                    return cached.Result;

                var result = reportPath.EndsWith(".opencover.xml", StringComparison.OrdinalIgnoreCase)
                    ? OpenCoverReader.Read(reportPath)
                    : CoberturaReader.Read(reportPath);
                coverageCache[reportPath] = new CachedCoverageReport(info.Length, info.LastWriteTimeUtc, result);
                return result;
            }
        }

        private void PruneCache(IEnumerable<string> reports)
        {
            var activeReports = new HashSet<string>(reports, StringComparer.OrdinalIgnoreCase);
            lock (cacheLock)
            {
                foreach (var cachedPath in coverageCache.Keys.Where(path => !activeReports.Contains(path)).ToList())
                    coverageCache.Remove(cachedPath);
            }
        }

        private void ScheduleRetentionExpiryRefresh()
        {
            if (Interlocked.Exchange(ref missingResultRetentionScheduled, 1) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(CoverageStyle.RetainMissingResultsSeconds + 1));
                    if (!disposed) ScheduleRefresh();
                }
                finally
                {
                    Interlocked.Exchange(ref missingResultRetentionScheduled, 0);
                }
            });
        }

        private sealed class CachedCoverageReport
        {
            public CachedCoverageReport(long length, DateTime lastWriteTimeUtc, IDictionary<string, CoverageLineSet> result)
            {
                Length = length;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Result = result;
            }

            public long Length { get; }
            public DateTime LastWriteTimeUtc { get; }
            public IDictionary<string, CoverageLineSet> Result { get; }
        }
    }
}
