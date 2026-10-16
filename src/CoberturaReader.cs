using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace CodeCoverage
{
    internal static class CoberturaReader
    {
        private static readonly Regex BranchCounts = new Regex(@"\((?<covered>\d+)\/(?<total>\d+)\)", RegexOptions.Compiled);
        // Coverlet writes this standard Cobertura structure when invoked with
        // /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura.
        public static IDictionary<string, CoverageLineSet> Read(string reportPath)
        {
            var document = XDocument.Load(reportPath);
            var result = new Dictionary<string, CoverageLineSet>(System.StringComparer.OrdinalIgnoreCase);
            var sourceRoots = document.Descendants("source")
                .Select(source => source.Value.Trim())
                .Where(source => !string.IsNullOrWhiteSpace(source) && Path.IsPathRooted(source))
                .ToList();
            foreach (var sourceFile in document.Descendants("class"))
            {
                var filename = (string)sourceFile.Attribute("filename");
                if (string.IsNullOrWhiteSpace(filename)) continue;
                var reportableLines = sourceFile.Descendants("line")
                    .Select(line => (int?)line.Attribute("number"))
                    .Where(line => line.HasValue)
                    .Select(line => line.Value)
                    .ToHashSet();
                if (reportableLines.Count == 0) continue;
                var coveredLines = sourceFile.Descendants("line")
                    .Where(line => (int?)line.Attribute("hits") > 0)
                    .Select(line => (int?)line.Attribute("number"))
                    .Where(line => line.HasValue)
                    .Select(line => line.Value);
                var fullPath = ResolveSourcePath(filename, reportPath, sourceRoots);
                if (!result.TryGetValue(fullPath, out var existing)) result[fullPath] = existing = new CoverageLineSet();
                existing.Reportable.UnionWith(reportableLines);
                existing.Covered.UnionWith(coveredLines);
                foreach (var line in sourceFile.Descendants("line"))
                {
                    var lineNumber = (int?)line.Attribute("number");
                    var hits = (int?)line.Attribute("hits") ?? 0;
                    if (lineNumber.HasValue && (!existing.LineHits.TryGetValue(lineNumber.Value, out var currentHits) || hits > currentHits))
                        existing.LineHits[lineNumber.Value] = hits;
                }
                foreach (var line in sourceFile.Descendants("line"))
                {
                    if (!string.Equals((string)line.Attribute("branch"), "true", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var lineNumber = (int?)line.Attribute("number");
                    var counts = BranchCounts.Match((string)line.Attribute("condition-coverage") ?? string.Empty);
                    if (!lineNumber.HasValue || !counts.Success) continue;
                    var branch = new BranchCoverage(
                        int.Parse(counts.Groups["covered"].Value),
                        int.Parse(counts.Groups["total"].Value));
                    if (!existing.Branches.TryGetValue(lineNumber.Value, out var current)
                        || branch.Total > current.Total
                        || (branch.Total == current.Total && branch.Covered > current.Covered))
                        existing.Branches[lineNumber.Value] = branch;
                }
            }
            return result;
        }

        private static string ResolveSourcePath(string filename, string reportPath, IEnumerable<string> sourceRoots)
        {
            if (Path.IsPathRooted(filename)) return CoveragePathMapper.Normalize(filename);
            foreach (var root in sourceRoots)
            {
                var mappedRoot = CoveragePathMapper.Normalize(root);
                var candidate = Path.GetFullPath(Path.Combine(mappedRoot, filename));
                if (File.Exists(candidate)) return CoveragePathMapper.Normalize(candidate);
            }
            return CoveragePathMapper.Normalize(Path.Combine(Path.GetDirectoryName(reportPath), filename));
        }
    }
}
