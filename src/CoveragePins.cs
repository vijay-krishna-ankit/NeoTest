using System;
using System.Collections.Generic;

namespace CodeCoverage
{
    // Pins are intentionally scoped to the current Visual Studio session. They are quick working-set
    // aids, and avoid writing solution-local user state from an editor extension.
    internal static class CoveragePins
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool Contains(string path)
        {
            lock (Gate) return !string.IsNullOrWhiteSpace(path) && Paths.Contains(path);
        }

        public static bool Toggle(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (Gate)
            {
                if (Paths.Remove(path)) return false;
                Paths.Add(path);
                return true;
            }
        }
    }
}
