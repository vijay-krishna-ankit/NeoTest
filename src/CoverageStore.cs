using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCoverage
{
    internal static class CoverageStore
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, CoverageLineSet> Results = new Dictionary<string, CoverageLineSet>(StringComparer.OrdinalIgnoreCase);
        private static DateTime resultsGeneratedUtc;
        private static string status = "Waiting for coverage reports under TestResults.";
        public static event EventHandler Changed;
        public static event EventHandler<CoverageCategoryChangedEventArgs> CoverageCategoryDropped;
        public static event EventHandler RefreshRequested;

        public static string Status
        {
            get { lock (Gate) return status; }
        }

        public static bool HasResults
        {
            get { lock (Gate) return Results.Count > 0; }
        }

        public static DateTime ResultsGeneratedUtc
        {
            get { lock (Gate) return resultsGeneratedUtc; }
        }

        public static void RequestRefresh() => RefreshRequested?.Invoke(null, EventArgs.Empty);

        public static void SetStatus(string value)
        {
            lock (Gate) status = value ?? string.Empty;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static CoverageLineState GetState(string path, int oneBasedLine)
        {
            if (string.IsNullOrWhiteSpace(path)) return CoverageLineState.None;
            lock (Gate)
            {
                if (!Results.TryGetValue(CoveragePathMapper.Normalize(path), out var lines)) return CoverageLineState.None;
                if (lines.Failed.Contains(oneBasedLine)) return CoverageLineState.Failed;
                if (lines.Covered.Contains(oneBasedLine)) return CoverageLineState.Covered;
                return lines.Reportable.Contains(oneBasedLine) ? CoverageLineState.Uncovered : CoverageLineState.None;
            }
        }

        public static bool IsStale(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (Gate)
            {
                var fullPath = CoveragePathMapper.Normalize(path);
                return resultsGeneratedUtc != DateTime.MinValue
                    && Results.ContainsKey(fullPath)
                    && File.Exists(fullPath)
                    && File.GetLastWriteTimeUtc(fullPath) > resultsGeneratedUtc;
            }
        }

        public static FailedTestLocation GetFirstFailedTest(string path, int oneBasedLine)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            lock (Gate)
            {
                return Results.TryGetValue(CoveragePathMapper.Normalize(path), out var lines)
                    && lines.FailureNavigation.TryGetValue(oneBasedLine, out var test)
                    ? test
                    : null;
            }
        }

        public static LineCoverageDetails GetLineDetails(string path, int oneBasedLine)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            lock (Gate)
            {
                if (!Results.TryGetValue(CoveragePathMapper.Normalize(path), out var lines)) return null;
                lines.LineHits.TryGetValue(oneBasedLine, out var hits);
                lines.Branches.TryGetValue(oneBasedLine, out var branches);
                return new LineCoverageDetails(hits, branches?.Covered ?? 0, branches?.Total ?? 0);
            }
        }

        public static void Replace(IDictionary<string, CoverageLineSet> results, DateTime generatedUtc)
        {
            var dropped = new List<string>();
            lock (Gate)
            {
                foreach (var result in results)
                {
                    var path = CoveragePathMapper.Normalize(result.Key);
                    if (!Results.TryGetValue(path, out var previous)) continue;
                    if (CoverageCategory(result.Value) < CoverageCategory(previous)) dropped.Add(path);
                }
                Results.Clear();
                resultsGeneratedUtc = generatedUtc;
                foreach (var result in results)
                    Results[CoveragePathMapper.Normalize(result.Key)] = result.Value;
            }
            Changed?.Invoke(null, EventArgs.Empty);
            if (dropped.Count > 0) CoverageCategoryDropped?.Invoke(null, new CoverageCategoryChangedEventArgs(dropped));
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Results.Clear();
                resultsGeneratedUtc = DateTime.MinValue;
                status = "Coverage results cleared.";
            }
            DiffCoverage.Clear();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static IReadOnlyList<FileCoverageSummary> GetFileSummaries()
        {
            lock (Gate)
            {
                return Results.Select(entry => new FileCoverageSummary(
                    entry.Key,
                    entry.Value.Covered.Count,
                    entry.Value.Reportable.Count,
                    entry.Value.Branches.Values.Sum(branch => branch.Covered),
                    entry.Value.Branches.Values.Sum(branch => branch.Total),
                    entry.Value.Failed.Count,
                    IsStale(entry.Key)))
                    .OrderBy(summary => summary.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public static FileCoverageSummary GetFileSummary(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            lock (Gate)
            {
                var fullPath = CoveragePathMapper.Normalize(path);
                if (!Results.TryGetValue(fullPath, out var lines)) return null;
                return new FileCoverageSummary(fullPath, lines.Covered.Count, lines.Reportable.Count,
                    lines.Branches.Values.Sum(branch => branch.Covered), lines.Branches.Values.Sum(branch => branch.Total),
                    lines.Failed.Count, IsStale(path));
            }
        }

        public static bool IsChangedLine(string path, int oneBasedLine)
            => !string.IsNullOrWhiteSpace(path) && DiffCoverage.IsChangedLine(path, oneBasedLine);

        private static int CoverageCategory(CoverageLineSet lines)
        {
            if (lines.Reportable.Count == 0) return 0;
            var percentage = lines.Covered.Count * 100.0 / lines.Reportable.Count;
            return percentage >= CoverageStyle.GoodCoverageThreshold ? 3
                : percentage >= CoverageStyle.WarningCoverageThreshold ? 2 : 1;
        }
    }

    internal sealed class CoverageLineSet
    {
        public HashSet<int> Covered { get; } = new HashSet<int>();
        public HashSet<int> Reportable { get; } = new HashSet<int>();
        public HashSet<int> Failed { get; } = new HashSet<int>();
        public Dictionary<int, FailedTestLocation> FailureNavigation { get; } = new Dictionary<int, FailedTestLocation>();
        public Dictionary<int, BranchCoverage> Branches { get; } = new Dictionary<int, BranchCoverage>();
        public Dictionary<int, int> LineHits { get; } = new Dictionary<int, int>();
    }

    internal enum CoverageLineState { None, Covered, Uncovered, Failed }

    internal sealed class BranchCoverage
    {
        public BranchCoverage(int covered, int total)
        {
            Covered = covered;
            Total = total;
        }

        public int Covered { get; }
        public int Total { get; }
    }

    internal sealed class LineCoverageDetails
    {
        public LineCoverageDetails(int hits, int coveredBranches, int totalBranches)
        {
            Hits = hits;
            CoveredBranches = coveredBranches;
            TotalBranches = totalBranches;
        }

        public int Hits { get; }
        public int CoveredBranches { get; }
        public int TotalBranches { get; }
    }

    internal sealed class CoverageCategoryChangedEventArgs : EventArgs
    {
        public CoverageCategoryChangedEventArgs(IReadOnlyList<string> paths) => Paths = paths;
        public IReadOnlyList<string> Paths { get; }
    }

    internal sealed class FileCoverageSummary
    {
        public FileCoverageSummary(string path, int coveredLines, int totalLines, int coveredBranches, int totalBranches,
            int failureCount, bool isStale)
        {
            Path = path;
            CoveredLines = coveredLines;
            TotalLines = totalLines;
            CoveredBranches = coveredBranches;
            TotalBranches = totalBranches;
            FailureCount = failureCount;
            IsStale = isStale;
        }

        public string Path { get; }
        public int CoveredLines { get; }
        public int TotalLines { get; }
        public int CoveredBranches { get; }
        public int TotalBranches { get; }
        public int FailureCount { get; }
        public bool IsStale { get; }
    }

    internal sealed class FailedTestLocation
    {
        public FailedTestLocation(string testName, string failurePath, int failureLine, string testPath, int testLine)
        {
            TestName = testName;
            FailurePath = failurePath;
            FailureLine = failureLine;
            TestPath = testPath;
            TestLine = testLine;
        }

        public string TestName { get; }
        public string FailurePath { get; }
        public int FailureLine { get; }
        public string TestPath { get; }
        public int TestLine { get; }
    }
}
