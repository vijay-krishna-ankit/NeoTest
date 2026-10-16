using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCoverage
{
    internal static class CoveragePathMapper
    {
        private static readonly object Gate = new object();
        private static IReadOnlyList<Tuple<string, string>> mappings = Array.Empty<Tuple<string, string>>();

        public static void Configure(string value)
        {
            var parsed = new List<Tuple<string, string>>();
            foreach (var item in (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = item.IndexOf("=>", StringComparison.Ordinal);
                if (separator <= 0 || separator >= item.Length - 2) continue;
                var source = item.Substring(0, separator).Trim().TrimEnd('/', '\\');
                var destination = item.Substring(separator + 2).Trim().TrimEnd('/', '\\');
                if (source.Length > 0 && destination.Length > 0)
                    parsed.Add(Tuple.Create(source, destination));
            }
            lock (Gate) mappings = parsed.OrderByDescending(item => item.Item1.Length).ToList();
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var mapped = path.Trim();
            lock (Gate)
            {
                foreach (var mapping in mappings)
                {
                    if (!StartsWithPath(mapped, mapping.Item1)) continue;
                    mapped = mapping.Item2 + mapped.Substring(mapping.Item1.Length);
                    break;
                }
            }
            try { return Path.GetFullPath(mapped.Replace('/', Path.DirectorySeparatorChar)); }
            catch (ArgumentException) { return mapped; }
            catch (NotSupportedException) { return mapped; }
        }

        private static bool StartsWithPath(string path, string prefix)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            return path.Length == prefix.Length
                || (path.Length > prefix.Length && (path[prefix.Length] == '/' || path[prefix.Length] == '\\'));
        }
    }
}
