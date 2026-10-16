using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodeCoverage
{
    internal static class CoverageNavigator
    {
        public static void MoveToNext(CoverageLineState desiredState)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            var document = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
            if (document == null) return;
            var path = dte.ActiveDocument.FullName;
            var start = document.Selection.ActivePoint.Line;
            var totalLines = document.EndPoint.Line;
            for (var offset = 1; offset <= totalLines; offset++)
            {
                var line = ((start - 1 + offset) % totalLines) + 1;
                if (CoverageStore.GetState(path, line) != desiredState) continue;
                document.Selection.GotoLine(line, true);
                CoverageStore.SetStatus("Moved to " + (desiredState == CoverageLineState.Failed ? "the next failed-test line." : "the next uncovered line."));
                return;
            }
            CoverageStore.SetStatus(desiredState == CoverageLineState.Failed
                ? "No failed-test lines are available in the active document."
                : "No uncovered lines are available in the active document.");
        }
    }
}
