using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CodeCoverage
{
    internal static class OpenCoverReader
    {
        public static IDictionary<string, CoverageLineSet> Read(string reportPath)
        {
            var document = XDocument.Load(reportPath);
            var files = document.Descendants().Where(element => element.Name.LocalName == "File")
                .Select(element => new { Id = (string)element.Attribute("uid"), Path = (string)element.Attribute("fullPath") })
                .Where(file => !string.IsNullOrWhiteSpace(file.Id) && !string.IsNullOrWhiteSpace(file.Path))
                .ToDictionary(file => file.Id, file => file.Path, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, CoverageLineSet>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in document.Descendants().Where(element => element.Name.LocalName == "SequencePoint" || element.Name.LocalName == "BranchPoint"))
            {
                var fileId = (string)point.Attribute("fileid");
                var line = (int?)point.Attribute("sl");
                var visits = (int?)point.Attribute("vc") ?? 0;
                if (!line.HasValue || !files.TryGetValue(fileId ?? string.Empty, out var path)) continue;
                var localPath = CoveragePathMapper.Normalize(path);
                if (string.IsNullOrWhiteSpace(localPath)) continue;
                if (!result.TryGetValue(localPath, out var lines)) result[localPath] = lines = new CoverageLineSet();
                if (point.Name.LocalName == "SequencePoint")
                {
                    lines.Reportable.Add(line.Value);
                    if (visits > 0) lines.Covered.Add(line.Value);
                    if (!lines.LineHits.TryGetValue(line.Value, out var currentVisits) || visits > currentVisits)
                        lines.LineHits[line.Value] = visits;
                }
                else
                {
                    if (!lines.Branches.TryGetValue(line.Value, out var branch)) branch = new BranchCoverage(0, 0);
                    lines.Branches[line.Value] = new BranchCoverage(branch.Covered + (visits > 0 ? 1 : 0), branch.Total + 1);
                }
            }
            return result;
        }
    }
}
