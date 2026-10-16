using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeCoverage
{
    internal static class TrxReader
    {
        private static readonly Regex SourceLocation = new Regex(
            @"\s+in\s+(?<path>.+):line\s+(?<line>\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // TRX failure records contain the exception stack trace. This maps the actual assertion/throw
        // site back to source without marking unrelated lines that merely belong to a failed test assembly.
        public static IEnumerable<FailedTestLocation> ReadFailedLocations(string reportPath)
        {
            var document = XDocument.Load(reportPath);
            var result = new List<FailedTestLocation>();
            foreach (var testResult in document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult"
                && string.Equals((string)element.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase)))
            {
                var testName = (string)testResult.Attribute("testName") ?? "Failed test";
                foreach (var stackTrace in testResult.Descendants().Where(element => element.Name.LocalName == "StackTrace"))
                {
                    var locations = new List<Tuple<string, int>>();
                    foreach (Match match in SourceLocation.Matches(stackTrace.Value))
                    {
                        var path = match.Groups["path"].Value.Trim();
                        var localPath = CoveragePathMapper.Normalize(path);
                        if (!int.TryParse(match.Groups["line"].Value, out var line)
                            || string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath)) continue;
                        locations.Add(Tuple.Create(localPath, line));
                    }
                    if (locations.Count == 0) continue;

                    // Stack traces end at the invoking test method, so the last source frame is the
                    // destination when a developer clicks a red production-code marker.
                    var testLocation = locations[locations.Count - 1];
                    foreach (var location in locations)
                    {
                        result.Add(new FailedTestLocation(testName, location.Item1, location.Item2,
                            testLocation.Item1, testLocation.Item2));
                    }
                }
            }
            return result;
        }
    }
}
