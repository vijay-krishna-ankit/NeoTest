using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace CodeCoverage
{
    [System.Runtime.InteropServices.Guid("E0D61B37-1E95-47B0-8B8B-0BF3D0D2F09A")]
    internal sealed class CoverageSettingsToolWindow : ToolWindowPane
    {
        public CoverageSettingsToolWindow() : base(null)
        {
            Caption = "Neo-Test Settings";
            Content = new CoverageSettingsControl();
        }
    }

    internal static class CoverageSettingsHost
    {
        public static Func<CoverageSettingsSnapshot> Read { get; set; }
        public static Action<CoverageSettingsSnapshot> Apply { get; set; }
    }

    internal sealed class CoverageSettingsControl : UserControl
    {
        private readonly Dictionary<string, TextBox> text = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
        private readonly TextBlock message = new TextBlock { Margin = new Thickness(8, 0, 8, 8), Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        private CoverageSettingsSnapshot current;

        public CoverageSettingsControl()
        {
            var root = new DockPanel();
            VisualStudioTheme.ApplyToolWindowSurface(root);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
            var reset = CreateButton("Reset", "Discard edits and reload saved Neo-Test settings");
            var apply = CreateButton("Apply", "Save these settings and update Neo-Test immediately");
            reset.Click += (_, __) => LoadSettings();
            apply.Click += (_, __) => ApplySettings();
            buttons.Children.Add(reset);
            buttons.Children.Add(apply);
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(message, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(message);

            var tabs = new TabControl { Margin = new Thickness(8, 8, 8, 0) };
            VisualStudioTheme.ApplyToolWindowControl(tabs);
            tabs.Items.Add(CreateAppearanceTab());
            tabs.Items.Add(CreateReportsTab());
            tabs.Items.Add(CreateExecutionTab());
            tabs.Items.Add(CreateCoverageTab());
            tabs.Items.Add(CreateFilteringTab());
            root.Children.Add(tabs);
            Content = root;
            Loaded += (_, __) => LoadSettings();
        }

        private TabItem CreateAppearanceTab()
        {
            var panel = CreatePanel();
            AddText(panel, "Covered color", "CoveredLineColor", "HTML color, for example #2EA043.");
            AddText(panel, "Uncovered color", "UncoveredLineColor", "HTML color, for example #808080.");
            AddText(panel, "Failed-test color", "FailedTestLineColor", "HTML color, for example #DC3545.");
            AddText(panel, "Good badge threshold", "GoodCoverageThreshold", "Line percentage for the green badge.");
            AddText(panel, "Warning badge threshold", "WarningCoverageThreshold", "Line percentage for the amber badge.");
            return CreateTab("Appearance", panel);
        }

        private TabItem CreateReportsTab()
        {
            var panel = CreatePanel();
            AddText(panel, "Results directory", "ResultsDirectory", "Relative to the solution. Default: TestResults.");
            AddText(panel, "Parallel result window", "ParallelTestResultWindowSeconds", "Seconds used to merge concurrently completed TRX reports.");
            AddText(panel, "Retain missing results", "RetainMissingResultsSeconds", "Seconds to retain results while reports are being replaced.");
            AddText(panel, "Report path mappings", "ReportPathMappings", "Semicolon-separated reportPrefix=>localPrefix mappings.", multiline: true);
            AddText(panel, "Diff base reference", "DiffBaseReference", "Git revision used for changed-line coverage.");
            return CreateTab("Reports", panel);
        }

        private TabItem CreateExecutionTab()
        {
            var panel = CreatePanel();
            AddText(panel, "Test command", "TestCommand", "Command run by Run Tests with Coverage.", multiline: true);
            AddText(panel, "Current-file test command", "CurrentFileTestCommand", "Use {file} and {name} placeholders.", multiline: true);
            AddText(panel, "Command timeout", "TestCommandTimeoutSeconds", "Seconds before Neo-Test stops the configured command.");
            return CreateTab("Test execution", panel);
        }

        private TabItem CreateCoverageTab()
        {
            var panel = CreatePanel();
            AddText(panel, "Minimum solution line coverage", "MinimumSolutionLineCoverage", "0 disables the local quality gate.");
            AddText(panel, "Minimum solution branch coverage", "MinimumSolutionBranchCoverage", "0 disables the local quality gate.");
            AddText(panel, "Project thresholds", "ProjectCoverageThresholds", "Example: Core.*=>80,70. These are displayed in Coverage Details.", multiline: true);
            AddCheck(panel, "Notify on category drops", "NotifyOnCoverageCategoryDrop", "Show a notification when an existing file drops badge category.");
            return CreateTab("Coverage", panel);
        }

        private TabItem CreateFilteringTab()
        {
            var panel = CreatePanel();
            AddText(panel, "Always include projects", "AlwaysIncludeProjects", "Semicolon-separated project names or wildcard patterns.");
            AddText(panel, "Always exclude projects", "AlwaysExcludeProjects", "Semicolon-separated project names or wildcard patterns.");
            AddCheck(panel, "Exclude generated files", "ExcludeGeneratedFiles", "Exclude generated files from markers, totals, and exports.");
            AddText(panel, "Generated file patterns", "GeneratedFilePatterns", "Semicolon-separated wildcard patterns.", multiline: true);
            return CreateTab("Filtering", panel);
        }

        private static TabItem CreateTab(string title, StackPanel panel)
            => new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };

        private static StackPanel CreatePanel() => new StackPanel { Margin = new Thickness(10) };

        private void AddText(Panel panel, string label, string key, string toolTip, bool multiline = false)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var title = new TextBlock { Text = label, Margin = new Thickness(0, 4, 8, 0), ToolTip = toolTip, TextWrapping = TextWrapping.Wrap };
            VisualStudioTheme.ApplyToolWindowText(title);
            var field = new TextBox { ToolTip = toolTip, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 46 : 25 };
            AutomationProperties.SetName(field, label);
            VisualStudioTheme.ApplyToolWindowControl(field);
            Grid.SetColumn(field, 1);
            row.Children.Add(title); row.Children.Add(field); panel.Children.Add(row); text[key] = field;
        }

        private void AddCheck(Panel panel, string label, string key, string toolTip)
        {
            var check = new CheckBox { Content = label, ToolTip = toolTip, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(check, label);
            VisualStudioTheme.ApplyToolWindowControl(check);
            panel.Children.Add(check); checks[key] = check;
        }

        private static Button CreateButton(string title, string toolTip)
        {
            var button = new Button { Content = title, ToolTip = toolTip, Margin = new Thickness(4, 0, 0, 0), MinWidth = 72 };
            AutomationProperties.SetName(button, title + " Neo-Test settings");
            VisualStudioTheme.ApplyToolWindowControl(button);
            return button;
        }

        private void LoadSettings()
        {
            current = CoverageSettingsHost.Read?.Invoke();
            if (current == null) { message.Text = "Neo-Test settings are not available yet."; return; }
            SetColor("CoveredLineColor", current.CoveredLineColor); SetColor("UncoveredLineColor", current.UncoveredLineColor); SetColor("FailedTestLineColor", current.FailedTestLineColor);
            Set("GoodCoverageThreshold", current.GoodCoverageThreshold); Set("WarningCoverageThreshold", current.WarningCoverageThreshold);
            Set("AlwaysIncludeProjects", current.AlwaysIncludeProjects); Set("AlwaysExcludeProjects", current.AlwaysExcludeProjects);
            Set("GeneratedFilePatterns", current.GeneratedFilePatterns); checks["ExcludeGeneratedFiles"].IsChecked = current.ExcludeGeneratedFiles;
            Set("TestCommand", current.TestCommand); Set("CurrentFileTestCommand", current.CurrentFileTestCommand); Set("TestCommandTimeoutSeconds", current.TestCommandTimeoutSeconds);
            Set("ResultsDirectory", current.ResultsDirectory); Set("ParallelTestResultWindowSeconds", current.ParallelTestResultWindowSeconds); Set("ReportPathMappings", current.ReportPathMappings); Set("RetainMissingResultsSeconds", current.RetainMissingResultsSeconds);
            Set("DiffBaseReference", current.DiffBaseReference); Set("MinimumSolutionLineCoverage", current.MinimumSolutionLineCoverage); Set("MinimumSolutionBranchCoverage", current.MinimumSolutionBranchCoverage);
            Set("ProjectCoverageThresholds", current.ProjectCoverageThresholds); checks["NotifyOnCoverageCategoryDrop"].IsChecked = current.NotifyOnCoverageCategoryDrop;
            message.Text = "Settings are saved with your Visual Studio profile.";
        }

        private void ApplySettings()
        {
            if (current == null || CoverageSettingsHost.Apply == null) return;
            try
            {
                var updated = new CoverageSettingsSnapshot
                {
                    CoveredLineColor = ColorValue("CoveredLineColor", current.CoveredLineColor), UncoveredLineColor = ColorValue("UncoveredLineColor", current.UncoveredLineColor), FailedTestLineColor = ColorValue("FailedTestLineColor", current.FailedTestLineColor),
                    GoodCoverageThreshold = IntValue("GoodCoverageThreshold", current.GoodCoverageThreshold), WarningCoverageThreshold = IntValue("WarningCoverageThreshold", current.WarningCoverageThreshold),
                    AlwaysIncludeProjects = Value("AlwaysIncludeProjects"), AlwaysExcludeProjects = Value("AlwaysExcludeProjects"), ExcludeGeneratedFiles = checks["ExcludeGeneratedFiles"].IsChecked == true, GeneratedFilePatterns = Value("GeneratedFilePatterns"),
                    TestCommand = Value("TestCommand"), CurrentFileTestCommand = Value("CurrentFileTestCommand"), TestCommandTimeoutSeconds = IntValue("TestCommandTimeoutSeconds", current.TestCommandTimeoutSeconds),
                    ResultsDirectory = Value("ResultsDirectory"), ParallelTestResultWindowSeconds = IntValue("ParallelTestResultWindowSeconds", current.ParallelTestResultWindowSeconds), ReportPathMappings = Value("ReportPathMappings"), RetainMissingResultsSeconds = IntValue("RetainMissingResultsSeconds", current.RetainMissingResultsSeconds),
                    DiffBaseReference = Value("DiffBaseReference"), MinimumSolutionLineCoverage = IntValue("MinimumSolutionLineCoverage", current.MinimumSolutionLineCoverage), MinimumSolutionBranchCoverage = IntValue("MinimumSolutionBranchCoverage", current.MinimumSolutionBranchCoverage),
                    NotifyOnCoverageCategoryDrop = checks["NotifyOnCoverageCategoryDrop"].IsChecked == true, ProjectCoverageThresholds = Value("ProjectCoverageThresholds")
                };
                CoverageSettingsHost.Apply(updated);
                current = CoverageSettingsHost.Read();
                LoadSettings();
                message.Text = "Settings applied and saved.";
            }
            catch (Exception exception) { message.Text = "Settings could not be applied: " + exception.Message; }
        }

        private void Set(string key, object value) => text[key].Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        private void SetColor(string key, Color color) => text[key].Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        private string Value(string key) => text[key].Text ?? string.Empty;
        private int IntValue(string key, int fallback) => int.TryParse(Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        private Color ColorValue(string key, Color fallback) { try { return ColorTranslator.FromHtml(Value(key)); } catch (ArgumentException) { return fallback; } }
    }
}
