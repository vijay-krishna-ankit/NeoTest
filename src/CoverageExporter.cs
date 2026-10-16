using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;

namespace CodeCoverage
{
    internal static class CoverageExporter
    {
        public static void Export()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new SaveFileDialog
            {
                Title = "Export Coverage Report",
                Filter = "CSV (*.csv)|*.csv|Markdown (*.md)|*.md|HTML (*.html)|*.html",
                FileName = "coverage-report",
                DefaultExt = ".csv",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var files = CoverageStore.GetFileSummaries();
                var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                File.WriteAllText(dialog.FileName, extension == ".md" ? Markdown(files) : extension == ".html" ? Html(files) : Csv(files), Encoding.UTF8);
                CoverageStore.SetStatus("Coverage report exported to " + dialog.FileName + ".");
            }
            catch (Exception exception)
            {
                CoverageStore.SetStatus("Coverage export failed: " + exception.Message);
            }
        }

        private static string Csv(System.Collections.Generic.IEnumerable<FileCoverageSummary> files)
        {
            var builder = new StringBuilder("File,Line coverage,Lines covered,Total lines,Branch coverage,Branches covered,Total branches\r\n");
            foreach (var file in files)
                builder.Append('"').Append(file.Path.Replace("\"", "\"\"")).Append("\",")
                    .Append(Percent(file.CoveredLines, file.TotalLines)).Append(',').Append(file.CoveredLines).Append(',').Append(file.TotalLines).Append(',')
                    .Append(Percent(file.CoveredBranches, file.TotalBranches)).Append(',').Append(file.CoveredBranches).Append(',').Append(file.TotalBranches).Append("\r\n");
            return builder.ToString();
        }

        private static string Markdown(System.Collections.Generic.IEnumerable<FileCoverageSummary> files)
        {
            var builder = new StringBuilder("# Coverage Report\r\n\r\n| File | Line coverage | Branch coverage |\r\n|---|---:|---:|\r\n");
            foreach (var file in files) builder.Append("| ").Append(file.Path.Replace("|", "\\|")).Append(" | ").Append(Percent(file.CoveredLines, file.TotalLines)).Append("% (").Append(file.CoveredLines).Append('/').Append(file.TotalLines).Append(") | ").Append(Percent(file.CoveredBranches, file.TotalBranches)).Append("% (").Append(file.CoveredBranches).Append('/').Append(file.TotalBranches).Append(") |\r\n");
            return builder.ToString();
        }

        private static string Html(System.Collections.Generic.IEnumerable<FileCoverageSummary> files)
        {
            var rows = string.Join("", files.Select(file => "<tr><td>" + System.Security.SecurityElement.Escape(file.Path) + "</td><td>" + Percent(file.CoveredLines, file.TotalLines) + "% (" + file.CoveredLines + "/" + file.TotalLines + ")</td><td>" + Percent(file.CoveredBranches, file.TotalBranches) + "% (" + file.CoveredBranches + "/" + file.TotalBranches + ")</td></tr>"));
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Coverage Report</title><style>body{font-family:Segoe UI,sans-serif;margin:2rem}table{border-collapse:collapse;width:100%}th,td{padding:.5rem;border-bottom:1px solid #ccc;text-align:left}</style></head><body><h1>Coverage Report</h1><table><thead><tr><th>File</th><th>Line coverage</th><th>Branch coverage</th></tr></thead><tbody>" + rows + "</tbody></table></body></html>";
        }

        private static string Percent(int covered, int total) => total == 0 ? "—" : (covered * 100.0 / total).ToString("0.0");
    }
}
