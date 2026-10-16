using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Shell;

namespace CodeCoverage
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(CoverageMargin.MarginName)]
    [Order(After = PredefinedMarginNames.LineNumber)]
    [MarginContainer(PredefinedMarginNames.LeftSelection)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class CoverageMarginProvider : IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost host, IWpfTextViewMargin marginContainer)
            => new CoverageMargin(host.TextView);
    }

    internal sealed class CoverageMargin : IWpfTextViewMargin
    {
        internal const string MarginName = "CodeCoverageMargin";
        private const double BarWidth = 4;
        private readonly IWpfTextView view;
        private readonly Canvas canvas = new Canvas { Width = BarWidth, ClipToBounds = true };
        private bool disposed;

        public CoverageMargin(IWpfTextView view)
        {
            this.view = view;
            AutomationProperties.SetName(canvas, "Code coverage markers");
            view.LayoutChanged += OnLayoutChanged;
            canvas.MouseLeftButtonUp += OnMouseLeftButtonUp;
            CoverageStore.Changed += OnCoverageChanged;
            CoverageStyle.Changed += OnStyleChanged;
        }

        public FrameworkElement VisualElement => canvas;
        public double MarginSize => BarWidth;
        public bool Enabled => !disposed;
        public ITextViewMargin GetTextViewMargin(string marginName) => marginName == MarginName ? this : null;

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) => Redraw();
        private void OnCoverageChanged(object sender, EventArgs e) => ScheduleRedraw();
        private void OnStyleChanged(object sender, EventArgs e) => ScheduleRedraw();

        private void ScheduleRedraw()
        {
            if (disposed) return;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!disposed) Redraw();
            });
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var position = e.GetPosition(canvas);
            foreach (var textLine in view.TextViewLines)
            {
                if (position.Y < textLine.Top || position.Y > textLine.Top + textLine.Height) continue;
                var lineNumber = textLine.Start.GetContainingLine().LineNumber + 1;
                if (CoverageStore.GetState(GetDocumentPath(), lineNumber) != CoverageLineState.Failed) return;
                var test = CoverageStore.GetFirstFailedTest(GetDocumentPath(), lineNumber);
                if (test != null) FailedTestNavigator.Open(test);
                e.Handled = true;
                return;
            }
        }

        private void Redraw()
        {
            if (disposed || !view.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document)) return;
            canvas.Children.Clear();
            var coveredBrush = CreateBrush(CoverageStyle.CoveredColor);
            var uncoveredBrush = CreateBrush(CoverageStyle.UncoveredColor);
            var failedBrush = CreateBrush(CoverageStyle.FailedColor);
            var highContrast = SystemParameters.HighContrast;
            var isStale = CoverageStore.IsStale(document.FilePath);
            Border activeBar = null;
            var activeBarStart = 0d;
            var previousCoveredLine = -2;
            CoverageLineState activeBarState = CoverageLineState.None;
            var activeBarIsChanged = false;
            foreach (var textLine in view.TextViewLines)
            {
                var lineNumber = textLine.Start.GetContainingLine().LineNumber + 1;
                var state = CoverageStore.GetState(document.FilePath, lineNumber);
                var isChanged = CoverageStore.IsChangedLine(document.FilePath, lineNumber);
                if (state == CoverageLineState.None)
                {
                    activeBar = null;
                    previousCoveredLine = -2;
                    activeBarState = CoverageLineState.None;
                    activeBarIsChanged = false;
                    continue;
                }

                // Consecutive covered source lines intentionally share one uninterrupted marker.
                // A wrapped display line has the same source line number and therefore remains in the run.
                if (activeBar != null
                    && activeBarState == state
                    && activeBarIsChanged == isChanged
                    && (lineNumber == previousCoveredLine || lineNumber == previousCoveredLine + 1))
                {
                    activeBar.Height = Math.Max(1, textLine.Top + textLine.Height - activeBarStart);
                }
                else
                {
                    activeBarStart = textLine.Top;
                    activeBar = new Border
                    {
                        Width = BarWidth,
                        Height = Math.Max(1, textLine.Height),
                        Background = state == CoverageLineState.Failed
                            ? failedBrush
                            : state == CoverageLineState.Covered ? coveredBrush : uncoveredBrush,
                        CornerRadius = new CornerRadius(1),
                        // Source changed after this result was created: preserve the state color, but mute it.
                        Opacity = isStale ? 0.42 : 1,
                        ToolTip = CreateToolTip(document.FilePath, lineNumber, state, isStale),
                        BorderThickness = highContrast ? new Thickness(1)
                            : state == CoverageLineState.Uncovered && isChanged ? new Thickness(1, 0, 0, 0) : new Thickness(0),
                        BorderBrush = highContrast ? SystemColors.WindowTextBrush
                            : state == CoverageLineState.Uncovered && isChanged ? failedBrush : state == CoverageLineState.Failed ? uncoveredBrush : null
                    };
                    AutomationProperties.SetName(activeBar, CreateAccessibleName(document.FilePath, lineNumber, state, isStale));
                    Canvas.SetTop(activeBar, activeBarStart);
                    canvas.Children.Add(activeBar);
                }
                previousCoveredLine = lineNumber;
                activeBarState = state;
                activeBarIsChanged = isChanged;
            }
        }

        private static SolidColorBrush CreateBrush(System.Drawing.Color color)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static string CreateToolTip(string path, int lineNumber, CoverageLineState state, bool isStale)
        {
            var description = state == CoverageLineState.Failed
                ? "A related test failed. Click to open the first failing test."
                : state == CoverageLineState.Covered ? "Covered by the latest test run." : "Not covered by the latest test run.";
            var details = CoverageStore.GetLineDetails(path, lineNumber);
            if (details != null)
            {
                description += " Hits: " + details.Hits + ".";
                if (details.TotalBranches > 0)
                    description += " Branches: " + details.CoveredBranches + "/" + details.TotalBranches + ".";
            }
            if (CoverageStore.IsChangedLine(path, lineNumber)) description += " This is a changed source line.";
            return isStale ? description + " Coverage is stale because the source changed after the run." : description;
        }

        private static string CreateAccessibleName(string path, int lineNumber, CoverageLineState state, bool isStale)
            => "Line " + lineNumber + ". " + CreateToolTip(path, lineNumber, state, isStale);

        private string GetDocumentPath()
            => view.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) ? document.FilePath : null;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            view.LayoutChanged -= OnLayoutChanged;
            canvas.MouseLeftButtonUp -= OnMouseLeftButtonUp;
            CoverageStore.Changed -= OnCoverageChanged;
            CoverageStyle.Changed -= OnStyleChanged;
        }
    }

    internal static class FailedTestNavigator
    {
        public static void Open(FailedTestLocation failure)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte == null || string.IsNullOrWhiteSpace(failure.TestPath)) return;

            var window = dte.ItemOperations.OpenFile(failure.TestPath);
            window.Activate();
            if (dte.ActiveDocument?.Object("TextDocument") is TextDocument document)
                document.Selection.GotoLine(failure.TestLine, true);
        }
    }
}
