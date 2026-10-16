using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using EnvDTE;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace CodeCoverage
{
    [System.Runtime.InteropServices.Guid("EEF5060F-9DD5-4357-9DD4-E0C26DD7B113")]
    internal sealed class CoverageDetailsToolWindow : ToolWindowPane
    {
        public CoverageDetailsToolWindow() : base(null)
        {
            Caption = "Neo-Test Coverage Details";
            Content = new CoverageDetailsControl();
        }
    }

    internal sealed class CoverageDetailsControl : UserControl
    {
        private readonly ObservableCollection<CoverageTreeNode> projects = new ObservableCollection<CoverageTreeNode>();
        private readonly TreeView tree = new TreeView { Margin = new Thickness(8, 0, 8, 8) };
        private readonly List<CoverageTreeNode> allProjects = new List<CoverageTreeNode>();
        private readonly TextBox search = new TextBox { Margin = new Thickness(8, 8, 8, 0), MinWidth = 180, ToolTip = "Search projects and files" };
        private readonly ToggleButton excludeTestProjects = new ToggleButton { Content = "Exclude test projects", Margin = new Thickness(0, 8, 8, 0), ToolTip = "Exclude projects with test-oriented names from the solution totals" };
        private readonly ToggleButton failedOnly = new ToggleButton { Content = "Failed", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Show files associated with failed tests" };
        private readonly ToggleButton belowTarget = new ToggleButton { Content = "Below 50%", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Show files with line coverage below 50%" };
        private readonly ToggleButton staleOnly = new ToggleButton { Content = "Stale", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Show files changed since the coverage result was produced" };
        private readonly ToggleButton changedOnly = new ToggleButton { Content = "Changed", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Show files with changed source lines" };
        private readonly ToggleButton pinnedOnly = new ToggleButton { Content = "Pinned", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Show only files pinned for this Visual Studio session" };
        private readonly Button pinSelected = new Button { Content = "Pin selected", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Pin or unpin the selected file" };
        private readonly Button copySummary = new Button { Content = "Copy summary", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Copy the currently visible coverage summary" };
        private readonly Button refresh = new Button { Content = "Refresh", Margin = new Thickness(0, 8, 6, 0), ToolTip = "Rescan coverage reports now" };
        private readonly ComboBox sort = new ComboBox { Margin = new Thickness(0, 8, 6, 0), MinWidth = 130, ToolTip = "Sort coverage results" };
        private readonly TextBlock emptyState = new TextBlock { Text = "Run tests with coverage to populate this view.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        private readonly TextBlock status = new TextBlock { Margin = new Thickness(8, 3, 8, 3), Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock searchWatermark = new TextBlock { Text = "Search projects or files", IsHitTestVisible = false, Margin = new Thickness(14, 9, 8, 0), Opacity = 0.65 };

        public CoverageDetailsControl()
        {
            var layout = new Grid();
            VisualStudioTheme.ApplyToolWindowSurface(layout);
            VisualStudioTheme.ApplyToolWindowControl(search);
            VisualStudioTheme.ApplyToolWindowControl(excludeTestProjects);
            VisualStudioTheme.ApplyToolWindowControl(failedOnly);
            VisualStudioTheme.ApplyToolWindowControl(belowTarget);
            VisualStudioTheme.ApplyToolWindowControl(staleOnly);
            VisualStudioTheme.ApplyToolWindowControl(changedOnly);
            VisualStudioTheme.ApplyToolWindowControl(pinnedOnly);
            VisualStudioTheme.ApplyToolWindowControl(pinSelected);
            VisualStudioTheme.ApplyToolWindowControl(copySummary);
            VisualStudioTheme.ApplyToolWindowControl(refresh);
            VisualStudioTheme.ApplyToolWindowControl(sort);
            VisualStudioTheme.ApplyToolWindowControl(tree);
            VisualStudioTheme.ApplyToolWindowText(emptyState);
            VisualStudioTheme.ApplyToolWindowText(status);
            VisualStudioTheme.ApplyToolWindowText(searchWatermark);
            AutomationProperties.SetName(search, "Search coverage projects and files");
            AutomationProperties.SetName(excludeTestProjects, "Exclude test projects");
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var toolbar = new DockPanel();
            var searchHost = new Grid();
            searchHost.Children.Add(search);
            searchHost.Children.Add(searchWatermark);
            search.TextChanged += (_, __) => { UpdateSearchWatermark(); ApplyFilter(); };
            search.GotKeyboardFocus += (_, __) => UpdateSearchWatermark();
            search.LostKeyboardFocus += (_, __) => UpdateSearchWatermark();
            excludeTestProjects.Checked += (_, __) => ApplyFilter();
            excludeTestProjects.Unchecked += (_, __) => ApplyFilter();
            foreach (var filter in new[] { failedOnly, belowTarget, staleOnly, changedOnly, pinnedOnly })
            {
                filter.Checked += (_, __) => ApplyFilter();
                filter.Unchecked += (_, __) => ApplyFilter();
            }
            sort.Items.Add("Name");
            sort.Items.Add("Lowest line coverage");
            sort.Items.Add("Lowest branch coverage");
            sort.SelectedIndex = 0;
            sort.SelectionChanged += (_, __) => ApplyFilter();
            pinSelected.Click += (_, __) => ToggleSelectedPin();
            copySummary.Click += (_, __) => CopyVisibleSummary();
            refresh.Click += (_, __) => CoverageStore.RequestRefresh();
            DockPanel.SetDock(excludeTestProjects, Dock.Right);
            DockPanel.SetDock(sort, Dock.Right);
            DockPanel.SetDock(staleOnly, Dock.Right);
            DockPanel.SetDock(changedOnly, Dock.Right);
            DockPanel.SetDock(belowTarget, Dock.Right);
            DockPanel.SetDock(failedOnly, Dock.Right);
            DockPanel.SetDock(pinnedOnly, Dock.Right);
            DockPanel.SetDock(pinSelected, Dock.Right);
            DockPanel.SetDock(copySummary, Dock.Right);
            DockPanel.SetDock(refresh, Dock.Right);
            toolbar.Children.Add(excludeTestProjects);
            toolbar.Children.Add(sort);
            toolbar.Children.Add(staleOnly);
            toolbar.Children.Add(changedOnly);
            toolbar.Children.Add(belowTarget);
            toolbar.Children.Add(failedOnly);
            toolbar.Children.Add(pinnedOnly);
            toolbar.Children.Add(pinSelected);
            toolbar.Children.Add(copySummary);
            toolbar.Children.Add(refresh);
            toolbar.Children.Add(searchHost);
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var headers = CreateHeaders();
            Grid.SetRow(headers, 0);
            Grid.SetRow(tree, 1);
            content.Children.Add(headers);
            content.Children.Add(tree);
            content.Children.Add(emptyState);
            Grid.SetRow(toolbar, 0);
            Grid.SetRow(status, 1);
            Grid.SetRow(content, 3);
            layout.Children.Add(toolbar);
            layout.Children.Add(status);
            layout.Children.Add(content);
            Content = layout;
            tree.ItemsSource = projects;
            tree.ItemTemplate = CreateTemplate();
            tree.MouseDoubleClick += OnTreeMouseDoubleClick;
            Loaded += (_, __) =>
            {
                CoverageStore.Changed += OnCoverageChanged;
                ThreadHelper.ThrowIfNotOnUIThread();
                Refresh();
            };
            Unloaded += (_, __) => CoverageStore.Changed -= OnCoverageChanged;
        }

        private void OnCoverageChanged(object sender, EventArgs e)
        {
            if (!IsLoaded) return;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (IsLoaded) Refresh();
            });
        }

        private void OnTreeMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && !(current is TreeViewItem)) current = VisualTreeHelper.GetParent(current);
            if (!(current is TreeViewItem item) || !(item.DataContext is CoverageTreeNode node)
                || string.IsNullOrWhiteSpace(node.FilePath)) return;

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            dte?.ItemOperations.OpenFile(node.FilePath)?.Activate();
            e.Handled = true;
        }

        private void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            status.Text = CoverageStore.Status + FormatLastRefresh();
            var projectFiles = GetProjectFiles();
            var grouped = CoverageStore.GetFileSummaries()
                .GroupBy(summary => projectFiles.TryGetValue(summary.Path, out var project) ? project : "Other files")
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            allProjects.Clear();
            foreach (var group in grouped)
            {
                var project = new CoverageTreeNode(group.Key, group.Sum(file => file.CoveredLines), group.Sum(file => file.TotalLines),
                    group.Sum(file => file.CoveredBranches), group.Sum(file => file.TotalBranches), IsTestProjectName(group.Key),
                    failureCount: group.Sum(file => file.FailureCount), isStale: group.Any(file => file.IsStale));
                foreach (var file in group.OrderBy(summary => summary.Path, StringComparer.OrdinalIgnoreCase))
                    project.Children.Add(new CoverageTreeNode(Path.GetFileName(file.Path), file.CoveredLines, file.TotalLines,
                        file.CoveredBranches, file.TotalBranches, filePath: file.Path, failureCount: file.FailureCount, isStale: file.IsStale));
                allProjects.Add(project);
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (projects == null) return;
            var query = search.Text.Trim();
            var visibleProjects = new List<CoverageTreeNode>();
            foreach (var project in allProjects)
            {
                if (excludeTestProjects.IsChecked == true && ShouldExcludeProject(project.Name)) continue;
                var matchingFiles = project.Children
                    .Where(file => MatchesFilters(file, query, project.Name))
                    .ToList();
                if (matchingFiles.Count == 0) continue;
                var filteredProject = new CoverageTreeNode(project.Name,
                    matchingFiles.Sum(file => file.CoveredLines), matchingFiles.Sum(file => file.TotalLines),
                    matchingFiles.Sum(file => file.CoveredBranches), matchingFiles.Sum(file => file.TotalBranches),
                    project.IsTestProject, failureCount: matchingFiles.Sum(file => file.FailureCount), isStale: matchingFiles.Any(file => file.IsStale),
                    thresholdStatus: ProjectCoverageThresholds.Evaluate(project.Name, matchingFiles.Sum(file => file.CoveredLines), matchingFiles.Sum(file => file.TotalLines), matchingFiles.Sum(file => file.CoveredBranches), matchingFiles.Sum(file => file.TotalBranches)));
                foreach (var file in Sort(matchingFiles)) filteredProject.Children.Add(file);
                visibleProjects.Add(filteredProject);
            }

            projects.Clear();
            if (visibleProjects.Count == 0)
            {
                emptyState.Text = allProjects.Count == 0
                    ? "Run tests with coverage to populate this view."
                    : "No coverage files match the current filters.";
                emptyState.Visibility = Visibility.Visible;
                return;
            }

            var orderedProjects = Sort(visibleProjects).ToList();
            var solution = new CoverageTreeNode("Solution", orderedProjects.Sum(project => project.CoveredLines),
                visibleProjects.Sum(project => project.TotalLines), visibleProjects.Sum(project => project.CoveredBranches),
                visibleProjects.Sum(project => project.TotalBranches), failureCount: orderedProjects.Sum(project => project.FailureCount), isStale: orderedProjects.Any(project => project.IsStale));
            foreach (var project in orderedProjects) solution.Children.Add(project);
            projects.Add(solution);
            emptyState.Visibility = Visibility.Collapsed;
        }

        private bool MatchesFilters(CoverageTreeNode file, string query, string projectName)
            => (string.IsNullOrEmpty(query)
                    || projectName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || file.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                && (failedOnly.IsChecked != true || file.FailureCount > 0)
                && (belowTarget.IsChecked != true || file.LinePercent < 50)
                && (staleOnly.IsChecked != true || file.IsStale)
                && (changedOnly.IsChecked != true || DiffCoverage.HasChangedLines(file.FilePath))
                && (pinnedOnly.IsChecked != true || CoveragePins.Contains(file.FilePath));

        private void ToggleSelectedPin()
        {
            if (!(tree.SelectedItem is CoverageTreeNode node) || string.IsNullOrWhiteSpace(node.FilePath))
            {
                CoverageStore.SetStatus("Select a file before pinning it.");
                return;
            }
            var pinned = CoveragePins.Toggle(node.FilePath);
            CoverageStore.SetStatus((pinned ? "Pinned " : "Unpinned ") + node.Name + " for this Visual Studio session.");
            ApplyFilter();
        }

        private void CopyVisibleSummary()
        {
            var files = projects.SelectMany(solution => solution.Children).SelectMany(project => project.Children)
                .Where(file => !string.IsNullOrWhiteSpace(file.FilePath)).ToList();
            var coveredLines = files.Sum(file => file.CoveredLines);
            var totalLines = files.Sum(file => file.TotalLines);
            var coveredBranches = files.Sum(file => file.CoveredBranches);
            var totalBranches = files.Sum(file => file.TotalBranches);
            Clipboard.SetText("Neo-Test coverage: line " + FormatPercent(coveredLines, totalLines) + "% (" + coveredLines + "/" + totalLines
                + "), branch " + FormatPercent(coveredBranches, totalBranches) + "% (" + coveredBranches + "/" + totalBranches + ").");
            CoverageStore.SetStatus("Copied the visible coverage summary to the clipboard.");
        }

        private static string FormatLastRefresh()
        {
            var generated = CoverageStore.ResultsGeneratedUtc;
            return generated == DateTime.MinValue ? string.Empty : " Last coverage result: " + generated.ToLocalTime().ToString("g") + ".";
        }

        private static string FormatPercent(int covered, int total)
            => total == 0 ? "—" : (covered * 100.0 / total).ToString("0.0");

        private IEnumerable<CoverageTreeNode> Sort(IEnumerable<CoverageTreeNode> nodes)
        {
            switch (sort.SelectedIndex)
            {
                case 1: return nodes.OrderBy(node => node.LinePercent).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
                case 2: return nodes.OrderBy(node => node.BranchPercent).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
                default: return nodes.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
            }
        }

        private void UpdateSearchWatermark()
            => searchWatermark.Visibility = string.IsNullOrEmpty(search.Text) && !search.IsKeyboardFocused
                ? Visibility.Visible : Visibility.Collapsed;

        private static bool ShouldExcludeProject(string projectName)
        {
            if (MatchesProjectPattern(projectName, CoverageStyle.AlwaysIncludeProjects)) return false;
            return MatchesProjectPattern(projectName, CoverageStyle.AlwaysExcludeProjects) || IsTestProjectName(projectName);
        }

        private static bool MatchesProjectPattern(string projectName, string patterns)
        {
            return patterns.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(pattern => pattern.Trim())
                .Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(projectName,
                    "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        private static bool IsTestProjectName(string projectName)
            => projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || projectName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
                || projectName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;

        private static Dictionary<string, string> GetProjectFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte?.Solution == null) return map;
            foreach (Project project in dte.Solution.Projects) AddProjectFiles(project, map, project.Name);
            return map;
        }

        private static void AddProjectFiles(Project project, IDictionary<string, string> map, string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) return;
            try
            {
                foreach (ProjectItem item in project.ProjectItems)
                {
                    AddProjectItem(item, map, projectName);
                    if (item.SubProject != null) AddProjectFiles(item.SubProject, map, item.SubProject.Name);
                }
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private static void AddProjectItem(ProjectItem item, IDictionary<string, string> map, string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var path = item.FileNames[1];
                if (File.Exists(path)) map[Path.GetFullPath(path)] = projectName;
            }
            catch (ArgumentException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            try
            {
                foreach (ProjectItem child in item.ProjectItems) AddProjectItem(child, map, projectName);
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private static HierarchicalDataTemplate CreateTemplate()
        {
            var template = new HierarchicalDataTemplate(typeof(CoverageTreeNode)) { ItemsSource = new System.Windows.Data.Binding("Children") };
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.AppendChild(CreateTextBlock("Name", 210, TextAlignment.Left));
            panel.AppendChild(CreateTextBlock("LineCoverage", 120, TextAlignment.Right));
            panel.AppendChild(CreateTextBlock("BranchCoverage", 130, TextAlignment.Right));
            panel.AppendChild(CreateTextBlock("ThresholdStatus", 150, TextAlignment.Left));
            template.VisualTree = panel;
            return template;
        }

        private static Grid CreateHeaders()
        {
            var headers = new Grid { Margin = new Thickness(27, 8, 8, 3) };
            headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            foreach (var header in new[] { CreateHeader("Name", 0, TextAlignment.Left), CreateHeader("Line coverage", 1, TextAlignment.Right), CreateHeader("Branch coverage", 2, TextAlignment.Right), CreateHeader("Project target", 3, TextAlignment.Left) }) headers.Children.Add(header);
            return headers;
        }

        private static TextBlock CreateHeader(string text, int column, TextAlignment alignment)
        {
            var header = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, TextAlignment = alignment };
            header.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetColumn(header, column);
            return header;
        }

        private static FrameworkElementFactory CreateTextBlock(string binding, double width, TextAlignment alignment)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(FrameworkElement.WidthProperty, width);
            text.SetValue(TextBlock.TextAlignmentProperty, alignment);
            text.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(binding));
            text.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding("FontWeight"));
            return text;
        }
    }

    internal sealed class CoverageTreeNode
    {
        public CoverageTreeNode(string name, int coveredLines, int totalLines, int coveredBranches, int totalBranches,
            bool isTestProject = false, string filePath = null, int failureCount = 0, bool isStale = false, string thresholdStatus = "")
        {
            Name = name;
            CoveredLines = coveredLines;
            TotalLines = totalLines;
            CoveredBranches = coveredBranches;
            TotalBranches = totalBranches;
            IsTestProject = isTestProject;
            FilePath = filePath;
            FailureCount = failureCount;
            IsStale = isStale;
            ThresholdStatus = thresholdStatus;
            LineCoverage = FormatCoverage(coveredLines, totalLines);
            BranchCoverage = FormatCoverage(coveredBranches, totalBranches);
        }

        public string Name { get; }
        public int CoveredLines { get; }
        public int TotalLines { get; }
        public int CoveredBranches { get; }
        public int TotalBranches { get; }
        public bool IsTestProject { get; }
        public string FilePath { get; }
        public string LineCoverage { get; }
        public string BranchCoverage { get; }
        public int FailureCount { get; }
        public bool IsStale { get; }
        public string ThresholdStatus { get; }
        public double LinePercent => TotalLines == 0 ? 0 : CoveredLines * 100.0 / TotalLines;
        public double BranchPercent => TotalBranches == 0 ? 0 : CoveredBranches * 100.0 / TotalBranches;
        public FontWeight FontWeight => string.IsNullOrEmpty(FilePath) ? FontWeights.SemiBold : FontWeights.Normal;
        public ObservableCollection<CoverageTreeNode> Children { get; } = new ObservableCollection<CoverageTreeNode>();

        private static string FormatCoverage(int covered, int total)
            => total == 0
                ? "— (0/0)"
                : $"{covered * 100.0 / total:0.0}% ({covered}/{total})";
    }
}
