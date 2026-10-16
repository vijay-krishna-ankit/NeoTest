using System.Linq;

namespace CodeCoverage
{
    internal static class QualityGateEvaluator
    {
        public static string Evaluate()
        {
            var files = CoverageStore.GetFileSummaries();
            var coveredLines = files.Sum(file => file.CoveredLines);
            var totalLines = files.Sum(file => file.TotalLines);
            var coveredBranches = files.Sum(file => file.CoveredBranches);
            var totalBranches = files.Sum(file => file.TotalBranches);
            var failures = new System.Collections.Generic.List<string>();
            if (CoverageStyle.MinimumSolutionLineCoverage > 0)
            {
                if (totalLines == 0) failures.Add("line coverage is unavailable");
                else if (coveredLines * 100.0 / totalLines < CoverageStyle.MinimumSolutionLineCoverage)
                    failures.Add("line " + (coveredLines * 100.0 / totalLines).ToString("0.0") + "% < " + CoverageStyle.MinimumSolutionLineCoverage + "%");
            }
            if (CoverageStyle.MinimumSolutionBranchCoverage > 0)
            {
                if (totalBranches == 0) failures.Add("branch coverage is unavailable");
                else if (coveredBranches * 100.0 / totalBranches < CoverageStyle.MinimumSolutionBranchCoverage)
                    failures.Add("branch " + (coveredBranches * 100.0 / totalBranches).ToString("0.0") + "% < " + CoverageStyle.MinimumSolutionBranchCoverage + "%");
            }
            if (CoverageStyle.MinimumSolutionLineCoverage == 0 && CoverageStyle.MinimumSolutionBranchCoverage == 0)
                return "Quality gates are disabled.";
            return failures.Count == 0 ? "Quality gates passed." : "Quality gates failed: " + string.Join("; ", failures) + ".";
        }
    }
}
