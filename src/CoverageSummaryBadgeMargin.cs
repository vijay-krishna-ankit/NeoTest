using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace CodeCoverage
{
    // The Top margin spans the editor and scrollbar together, letting the badge sit directly above
    // the vertical scrollbar instead of floating inside the text viewport.
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(CoverageSummaryBadgeMargin.MarginName)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class CoverageSummaryBadgeMarginProvider : IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost host, IWpfTextViewMargin marginContainer)
            => new CoverageSummaryBadgeMargin(host.TextView);
    }

    internal sealed class CoverageSummaryBadgeMargin : IWpfTextViewMargin
    {
        internal const string MarginName = "CodeCoverageSummaryBadgeMargin";
        private readonly IWpfTextView view;
        private readonly Grid visual = new Grid { Height = 24 };
        private readonly Border badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0),
            Visibility = Visibility.Collapsed
        };
        private bool disposed;

        public CoverageSummaryBadgeMargin(IWpfTextView view)
        {
            this.view = view;
            badge.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            AutomationProperties.SetName(badge, "Coverage summary");
            visual.Children.Add(badge);
            CoverageStore.Changed += OnCoverageChanged;
            CoverageStyle.Changed += OnCoverageChanged;
            Refresh();
        }

        public FrameworkElement VisualElement => visual;
        public double MarginSize => 24;
        public bool Enabled => !disposed;
        public ITextViewMargin GetTextViewMargin(string marginName) => marginName == MarginName ? this : null;

        private void OnCoverageChanged(object sender, EventArgs e)
        {
            if (disposed) return;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!disposed) Refresh();
            });
        }

        private void Refresh()
        {
            if (disposed || !view.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document)) return;
            var summary = CoverageStore.GetFileSummary(document.FilePath);
            if (summary == null || summary.TotalLines == 0)
            {
                badge.Visibility = Visibility.Collapsed;
                return;
            }

            var percent = summary.CoveredLines * 100.0 / summary.TotalLines;
            var isStale = summary.IsStale;
            var glyph = percent >= CoverageStyle.GoodCoverageThreshold ? "✓" : percent >= CoverageStyle.WarningCoverageThreshold ? "⚠" : "!";
            var color = percent >= CoverageStyle.GoodCoverageThreshold
                ? Color.FromRgb(46, 160, 67)
                : percent >= CoverageStyle.WarningCoverageThreshold ? Color.FromRgb(224, 157, 0) : Color.FromRgb(220, 53, 69);
            // Status hues are intentionally fixed by the coverage thresholds; surrounding chrome is themed.
            badge.Background = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(color);
            badge.ToolTip = $"Line coverage: {percent:0.0}% ({summary.CoveredLines}/{summary.TotalLines})"
                + (isStale ? "\nCoverage is stale because this file changed after the test run." : string.Empty);
            var content = new Grid();
            var indicator = new TextBlock
            {
                Text = glyph,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            if (SystemParameters.HighContrast) indicator.Foreground = SystemColors.HighlightTextBrush;
            else VisualStudioTheme.ApplyToolWindowText(indicator);
            content.Children.Add(indicator);
            if (isStale)
            {
                var stale = new TextBlock
                {
                    Text = "◷",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -2, -2)
                };
                VisualStudioTheme.ApplyToolWindowText(stale);
                content.Children.Add(stale);
            }
            badge.Child = content;
            AutomationProperties.SetName(badge, $"Line coverage {percent:0.0} percent. "
                + (isStale ? "Coverage result is stale." : "Coverage result is current."));
            badge.Visibility = Visibility.Visible;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CoverageStore.Changed -= OnCoverageChanged;
            CoverageStyle.Changed -= OnCoverageChanged;
        }
    }
}
