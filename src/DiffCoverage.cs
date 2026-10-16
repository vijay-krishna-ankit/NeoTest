using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace CodeCoverage
{
    internal static class DiffCoverage
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, HashSet<int>> ChangedLines = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Hunk = new Regex(@"^@@ .*\+(?<start>\d+)(,(?<count>\d+))? @@", RegexOptions.Compiled);
        private static string statusSuffix = string.Empty;

        public static string StatusSuffix
        {
            get { lock (Gate) return statusSuffix; }
        }

        public static void Refresh(string testResultsDirectory, string baseReference)
        {
            var solutionDirectory = Directory.GetParent(testResultsDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(solutionDirectory))
            {
                SetStatus(" Git diff coverage is unavailable because the solution directory could not be determined.");
                return;
            }
            var updated = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var repositoryRoot = GetRepositoryRoot(solutionDirectory);
                if (string.IsNullOrWhiteSpace(repositoryRoot))
                {
                    SetStatus(" Git diff coverage is unavailable; open a solution inside a Git repository.");
                    return;
                }
                var info = new ProcessStartInfo("git", "-c core.quotepath=false diff --no-ext-diff --no-renames --unified=0 " + QuoteArgument(baseReference) + " --")
                {
                    WorkingDirectory = repositoryRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(info))
                {
                    if (process == null) throw new InvalidOperationException("Git could not be started.");
                    string path = null;
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (line.StartsWith("+++ /dev/null", StringComparison.Ordinal))
                        {
                            path = null;
                            continue;
                        }
                        if (line.StartsWith("+++ b/", StringComparison.Ordinal))
                        {
                            path = CoveragePathMapper.Normalize(Path.Combine(repositoryRoot, line.Substring(6).Replace('/', Path.DirectorySeparatorChar)));
                            continue;
                        }
                        var match = Hunk.Match(line);
                        if (!match.Success || string.IsNullOrWhiteSpace(path)) continue;
                        var start = int.Parse(match.Groups["start"].Value);
                        var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value) : 1;
                        if (count == 0) continue;
                        if (!updated.TryGetValue(path, out var lines)) updated[path] = lines = new HashSet<int>();
                        for (var number = start; number < start + count; number++) lines.Add(number);
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        SetStatus(" Git diff coverage is unavailable because the configured base reference was not found.");
                        return;
                    }
                }
            }
            catch (Exception)
            {
                SetStatus(" Git diff coverage is unavailable; install Git or open a solution inside a Git repository.");
                return;
            }
            lock (Gate)
            {
                ChangedLines.Clear();
                foreach (var entry in updated) ChangedLines[entry.Key] = entry.Value;
                statusSuffix = string.Empty;
            }
        }

        public static bool IsChangedLine(string path, int line)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (Gate) return ChangedLines.TryGetValue(CoveragePathMapper.Normalize(path), out var lines) && lines.Contains(line);
        }

        public static bool HasChangedLines(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (Gate) return ChangedLines.TryGetValue(CoveragePathMapper.Normalize(path), out var lines) && lines.Count > 0;
        }

        public static void Clear()
        {
            lock (Gate)
            {
                ChangedLines.Clear();
                statusSuffix = string.Empty;
            }
        }

        private static void SetStatus(string value)
        {
            lock (Gate)
            {
                ChangedLines.Clear();
                statusSuffix = value;
            }
        }

        private static string QuoteArgument(string value)
            => "\"" + (value ?? "HEAD").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string GetRepositoryRoot(string workingDirectory)
        {
            var info = new ProcessStartInfo("git", "rev-parse --show-toplevel")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null) return null;
                var root = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(root) ? root : null;
            }
        }
    }
}
