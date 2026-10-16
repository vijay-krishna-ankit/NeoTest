using System;
using System.ComponentModel;
using System.Drawing;
using Microsoft.VisualStudio.Shell;

namespace CodeCoverage
{
    internal sealed class CoverageOptionsPage : DialogPage
    {
        [Category("Appearance")]
        [DisplayName("Covered line color")]
        [Description("Color used for the bar beside executed lines.")]
        public Color CoveredLineColor { get; set; } = Color.FromArgb(255, 46, 160, 67);

        [Category("Appearance")]
        [DisplayName("Uncovered line color")]
        [Description("Color used for reportable lines that were not executed.")]
        public Color UncoveredLineColor { get; set; } = Color.FromArgb(255, 128, 128, 128);

        [Category("Appearance")]
        [DisplayName("Failed test line color")]
        [Description("Color used for source lines identified in a failed test stack trace.")]
        public Color FailedTestLineColor { get; set; } = Color.FromArgb(255, 220, 53, 69);

        [Category("Coverage badge")]
        [DisplayName("Good coverage threshold")]
        [Description("Line coverage percentage at or above which the editor badge shows a green check. Valid range: 1–100.")]
        public int GoodCoverageThreshold { get; set; } = 80;

        [Category("Coverage badge")]
        [DisplayName("Warning coverage threshold")]
        [Description("Line coverage percentage at or above which the editor badge shows an amber warning. Lower values show a red exclamation. Valid range: 0–99.")]
        public int WarningCoverageThreshold { get; set; } = 50;

        [Category("Project filtering")]
        [DisplayName("Always include projects")]
        [Description("Semicolon-separated project names or wildcard patterns to keep when excluding test projects. These override automatic detection.")]
        public string AlwaysIncludeProjects { get; set; } = string.Empty;

        [Category("Project filtering")]
        [DisplayName("Always exclude projects")]
        [Description("Semicolon-separated project names or wildcard patterns to exclude when excluding test projects.")]
        public string AlwaysExcludeProjects { get; set; } = string.Empty;

        [Category("Project filtering")]
        [DisplayName("Exclude generated files")]
        [Description("Exclude conventional generated source files from editor markers, details, exports, and coverage totals.")]
        public bool ExcludeGeneratedFiles { get; set; } = true;

        [Category("Project filtering")]
        [DisplayName("Generated file patterns")]
        [Description("Semicolon-separated wildcard patterns used when generated-file exclusion is enabled.")]
        public string GeneratedFilePatterns { get; set; } = "*.g.cs;*.g.i.cs;*.designer.cs;*.generated.cs;AssemblyInfo.cs";

        [Category("Test execution")]
        [DisplayName("Test command")]
        [Description("Optional command run by Tools → Run Tests with Coverage. Example: dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=TestResults/coverage")]
        public string TestCommand { get; set; } = string.Empty;

        [Category("Test execution")]
        [DisplayName("Current file test command")]
        [Description("Optional command for Tools → Run Tests for Current File. Use {file} for the full file path and {name} for its filename.")]
        public string CurrentFileTestCommand { get; set; } = string.Empty;

        [Category("Test execution")]
        [DisplayName("Test command timeout (seconds)")]
        [Description("Maximum time Neo-Test waits for the configured test command. Valid range: 10–3600 seconds.")]
        public int TestCommandTimeoutSeconds { get; set; } = 600;

        [Category("Report discovery")]
        [DisplayName("Results directory")]
        [Description("Relative directory under the solution where Cobertura and TRX reports are written. Default: TestResults.")]
        public string ResultsDirectory { get; set; } = "TestResults";

        [Category("Report discovery")]
        [DisplayName("Parallel test result window (seconds)")]
        [Description("Time window used to combine TRX reports from concurrently completing test hosts. Valid range: 1–120 seconds.")]
        public int ParallelTestResultWindowSeconds { get; set; } = 15;

        [Category("Report discovery")]
        [DisplayName("Report path mappings")]
        [Description("Semicolon-separated report-to-local path mappings in reportPrefix=>localPrefix form. Use for containers, WSL, remote agents, or generated paths.")]
        public string ReportPathMappings { get; set; } = string.Empty;

        [Category("Report discovery")]
        [DisplayName("Retain missing results (seconds)")]
        [Description("How long to retain valid coverage after reports disappear during cleanup or replacement. Valid range: 0–300 seconds.")]
        public int RetainMissingResultsSeconds { get; set; } = 30;

        [Category("Notifications")]
        [DisplayName("Notify on coverage category drops")]
        [Description("Show a Visual Studio notification when an existing file falls from green to amber/red or from amber to red.")]
        public bool NotifyOnCoverageCategoryDrop { get; set; } = true;

        [Category("Coverage thresholds")]
        [DisplayName("Project thresholds")]
        [Description("Optional semicolon-separated projectPattern=>linePercent,branchPercent thresholds. Example: Core.*=>80,70.")]
        public string ProjectCoverageThresholds { get; set; } = string.Empty;

        [Category("Diff coverage")]
        [DisplayName("Diff base reference")]
        [Description("Git revision compared with the working tree for changed-line coverage. Leave as HEAD for the current branch tip.")]
        public string DiffBaseReference { get; set; } = "HEAD";

        [Category("Quality gates")]
        [DisplayName("Minimum solution line coverage")]
        [Description("Minimum solution line coverage percentage. Set to 0 to disable this gate.")]
        public int MinimumSolutionLineCoverage { get; set; }

        [Category("Quality gates")]
        [DisplayName("Minimum solution branch coverage")]
        [Description("Minimum solution branch coverage percentage. Set to 0 to disable this gate.")]
        public int MinimumSolutionBranchCoverage { get; set; }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            ApplyRuntime();
        }

        internal CoverageSettingsSnapshot CreateSnapshot() => new CoverageSettingsSnapshot
        {
            CoveredLineColor = CoveredLineColor, UncoveredLineColor = UncoveredLineColor, FailedTestLineColor = FailedTestLineColor,
            GoodCoverageThreshold = GoodCoverageThreshold, WarningCoverageThreshold = WarningCoverageThreshold,
            AlwaysIncludeProjects = AlwaysIncludeProjects, AlwaysExcludeProjects = AlwaysExcludeProjects,
            ExcludeGeneratedFiles = ExcludeGeneratedFiles, GeneratedFilePatterns = GeneratedFilePatterns,
            TestCommand = TestCommand, CurrentFileTestCommand = CurrentFileTestCommand, TestCommandTimeoutSeconds = TestCommandTimeoutSeconds,
            ResultsDirectory = ResultsDirectory, ParallelTestResultWindowSeconds = ParallelTestResultWindowSeconds,
            ReportPathMappings = ReportPathMappings, RetainMissingResultsSeconds = RetainMissingResultsSeconds,
            DiffBaseReference = DiffBaseReference, MinimumSolutionLineCoverage = MinimumSolutionLineCoverage,
            MinimumSolutionBranchCoverage = MinimumSolutionBranchCoverage, NotifyOnCoverageCategoryDrop = NotifyOnCoverageCategoryDrop,
            ProjectCoverageThresholds = ProjectCoverageThresholds
        };

        internal void ApplySnapshot(CoverageSettingsSnapshot snapshot)
        {
            CoveredLineColor = snapshot.CoveredLineColor; UncoveredLineColor = snapshot.UncoveredLineColor; FailedTestLineColor = snapshot.FailedTestLineColor;
            GoodCoverageThreshold = snapshot.GoodCoverageThreshold; WarningCoverageThreshold = snapshot.WarningCoverageThreshold;
            AlwaysIncludeProjects = snapshot.AlwaysIncludeProjects; AlwaysExcludeProjects = snapshot.AlwaysExcludeProjects;
            ExcludeGeneratedFiles = snapshot.ExcludeGeneratedFiles; GeneratedFilePatterns = snapshot.GeneratedFilePatterns;
            TestCommand = snapshot.TestCommand; CurrentFileTestCommand = snapshot.CurrentFileTestCommand; TestCommandTimeoutSeconds = snapshot.TestCommandTimeoutSeconds;
            ResultsDirectory = snapshot.ResultsDirectory; ParallelTestResultWindowSeconds = snapshot.ParallelTestResultWindowSeconds;
            ReportPathMappings = snapshot.ReportPathMappings; RetainMissingResultsSeconds = snapshot.RetainMissingResultsSeconds;
            DiffBaseReference = snapshot.DiffBaseReference; MinimumSolutionLineCoverage = snapshot.MinimumSolutionLineCoverage;
            MinimumSolutionBranchCoverage = snapshot.MinimumSolutionBranchCoverage; NotifyOnCoverageCategoryDrop = snapshot.NotifyOnCoverageCategoryDrop;
            ProjectCoverageThresholds = snapshot.ProjectCoverageThresholds;
            ApplyRuntime();
            SaveSettingsToStorage();
        }

        private void ApplyRuntime()
        {
            GoodCoverageThreshold = Math.Max(1, Math.Min(100, GoodCoverageThreshold));
            WarningCoverageThreshold = Math.Max(0, Math.Min(GoodCoverageThreshold - 1, WarningCoverageThreshold));
            CoverageStyle.SetOptions(CoveredLineColor, UncoveredLineColor, FailedTestLineColor,
                GoodCoverageThreshold, WarningCoverageThreshold, AlwaysIncludeProjects, AlwaysExcludeProjects);
            CoverageStyle.TestCommand = TestCommand ?? string.Empty;
            CoverageStyle.CurrentFileTestCommand = CurrentFileTestCommand ?? string.Empty;
            CoverageStyle.DiffBaseReference = string.IsNullOrWhiteSpace(DiffBaseReference) ? "HEAD" : DiffBaseReference.Trim();
            CoverageStyle.MinimumSolutionLineCoverage = Math.Max(0, Math.Min(100, MinimumSolutionLineCoverage));
            CoverageStyle.MinimumSolutionBranchCoverage = Math.Max(0, Math.Min(100, MinimumSolutionBranchCoverage));
            TestCommandTimeoutSeconds = Math.Max(10, Math.Min(3600, TestCommandTimeoutSeconds));
            ParallelTestResultWindowSeconds = Math.Max(1, Math.Min(120, ParallelTestResultWindowSeconds));
            CoverageStyle.TestCommandTimeoutSeconds = TestCommandTimeoutSeconds;
            CoverageStyle.ResultsDirectory = NormalizeResultsDirectory(ResultsDirectory);
            CoverageStyle.ParallelTestResultWindowSeconds = ParallelTestResultWindowSeconds;
            RetainMissingResultsSeconds = Math.Max(0, Math.Min(300, RetainMissingResultsSeconds));
            CoverageStyle.RetainMissingResultsSeconds = RetainMissingResultsSeconds;
            CoverageStyle.ExcludeGeneratedFiles = ExcludeGeneratedFiles;
            CoverageStyle.GeneratedFilePatterns = GeneratedFilePatterns ?? string.Empty;
            CoverageStyle.NotifyOnCoverageCategoryDrop = NotifyOnCoverageCategoryDrop;
            CoverageStyle.ProjectCoverageThresholds = ProjectCoverageThresholds ?? string.Empty;
            CoveragePathMapper.Configure(ReportPathMappings);
            CoverageStyle.NotifyChanged();
        }

        private static string NormalizeResultsDirectory(string value)
        {
            var directory = string.IsNullOrWhiteSpace(value) ? "TestResults" : value.Trim();
            if (directory == "." || System.IO.Path.IsPathRooted(directory)) return "TestResults";
            try
            {
                var segments = directory.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                return System.Linq.Enumerable.Any(segments, segment => segment == "." || segment == "..") ? "TestResults" : directory;
            }
            catch (System.ArgumentException) { return "TestResults"; }
            catch (System.NotSupportedException) { return "TestResults"; }
        }
    }

    internal sealed class CoverageSettingsSnapshot
    {
        public Color CoveredLineColor { get; set; }
        public Color UncoveredLineColor { get; set; }
        public Color FailedTestLineColor { get; set; }
        public int GoodCoverageThreshold { get; set; }
        public int WarningCoverageThreshold { get; set; }
        public string AlwaysIncludeProjects { get; set; }
        public string AlwaysExcludeProjects { get; set; }
        public bool ExcludeGeneratedFiles { get; set; }
        public string GeneratedFilePatterns { get; set; }
        public string TestCommand { get; set; }
        public string CurrentFileTestCommand { get; set; }
        public int TestCommandTimeoutSeconds { get; set; }
        public string ResultsDirectory { get; set; }
        public int ParallelTestResultWindowSeconds { get; set; }
        public string ReportPathMappings { get; set; }
        public int RetainMissingResultsSeconds { get; set; }
        public string DiffBaseReference { get; set; }
        public int MinimumSolutionLineCoverage { get; set; }
        public int MinimumSolutionBranchCoverage { get; set; }
        public bool NotifyOnCoverageCategoryDrop { get; set; }
        public string ProjectCoverageThresholds { get; set; }
    }

    internal static class CoverageStyle
    {
        private static Color coveredColor = Color.FromArgb(255, 46, 160, 67);
        private static Color uncoveredColor = Color.FromArgb(255, 128, 128, 128);
        private static Color failedColor = Color.FromArgb(255, 220, 53, 69);
        public static event EventHandler Changed;
        public static Color CoveredColor => coveredColor;
        public static Color UncoveredColor => uncoveredColor;
        public static Color FailedColor => failedColor;
        public static int GoodCoverageThreshold { get; private set; } = 80;
        public static int WarningCoverageThreshold { get; private set; } = 50;
        public static string AlwaysIncludeProjects { get; private set; } = string.Empty;
        public static string AlwaysExcludeProjects { get; private set; } = string.Empty;
        public static string TestCommand { get; set; } = string.Empty;
        public static string CurrentFileTestCommand { get; set; } = string.Empty;
        public static string DiffBaseReference { get; set; } = "HEAD";
        public static int MinimumSolutionLineCoverage { get; set; }
        public static int MinimumSolutionBranchCoverage { get; set; }
        public static int TestCommandTimeoutSeconds { get; set; } = 600;
        public static string ResultsDirectory { get; set; } = "TestResults";
        public static int ParallelTestResultWindowSeconds { get; set; } = 15;
        public static int RetainMissingResultsSeconds { get; set; } = 30;
        public static bool ExcludeGeneratedFiles { get; set; } = true;
        public static string GeneratedFilePatterns { get; set; } = "*.g.cs;*.g.i.cs;*.designer.cs;*.generated.cs;AssemblyInfo.cs";
        public static bool NotifyOnCoverageCategoryDrop { get; set; } = true;
        public static string ProjectCoverageThresholds { get; set; } = string.Empty;

        public static void SetColors(Color covered, Color uncovered, Color failed)
        {
            coveredColor = covered;
            uncoveredColor = uncovered;
            failedColor = failed;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void SetOptions(Color covered, Color uncovered, Color failed, int goodThreshold, int warningThreshold,
            string alwaysIncludeProjects, string alwaysExcludeProjects)
        {
            coveredColor = covered;
            uncoveredColor = uncovered;
            failedColor = failed;
            GoodCoverageThreshold = goodThreshold;
            WarningCoverageThreshold = warningThreshold;
            AlwaysIncludeProjects = alwaysIncludeProjects ?? string.Empty;
            AlwaysExcludeProjects = alwaysExcludeProjects ?? string.Empty;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);
    }
}
