using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeCoverage
{
    internal static class ProjectCoverageThresholds
    {
        public static string Evaluate(string projectName, int coveredLines, int totalLines, int coveredBranches, int totalBranches)
        {
            foreach (var entry in CoverageStyle.ProjectCoverageThresholds.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(new[] { "=>" }, StringSplitOptions.None);
                if (parts.Length != 2 || !Matches(projectName, parts[0].Trim())) continue;
                var thresholds = parts[1].Split(',').Select(value => value.Trim()).ToArray();
                TryPercent(thresholds, 0, out var lineThreshold);
                TryPercent(thresholds, 1, out var branchThreshold);
                if (lineThreshold == 0 && branchThreshold == 0) return string.Empty;
                var failures = new System.Collections.Generic.List<string>();
                if (lineThreshold > 0 && (totalLines == 0 || coveredLines * 100.0 / totalLines < lineThreshold)) failures.Add("line < " + lineThreshold.ToString("0.#") + "%");
                if (branchThreshold > 0 && (totalBranches == 0 || coveredBranches * 100.0 / totalBranches < branchThreshold)) failures.Add("branch < " + branchThreshold.ToString("0.#") + "%");
                return failures.Count == 0 ? "Target met" : "Target: " + string.Join(", ", failures);
            }
            return string.Empty;
        }

        private static bool TryPercent(string[] values, int index, out double value)
        {
            value = 0;
            return index < values.Length && double.TryParse(values[index], NumberStyles.Number, CultureInfo.InvariantCulture, out value)
                && value >= 0 && value <= 100;
        }

        private static bool Matches(string name, string pattern)
            => Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
    }
}
