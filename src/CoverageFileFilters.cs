using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeCoverage
{
    internal static class CoverageFileFilters
    {
        public static bool ShouldInclude(string path)
        {
            if (!CoverageStyle.ExcludeGeneratedFiles || string.IsNullOrWhiteSpace(path)) return true;
            var fileName = Path.GetFileName(path);
            return !CoverageStyle.GeneratedFilePatterns.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(pattern => pattern.Trim())
                .Any(pattern => Regex.IsMatch(fileName, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase));
        }
    }
}
