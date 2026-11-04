using System;
using System.IO;

namespace AgentTranslation.Core
{
    public sealed class SandboxViolationException : Exception
    {
        public SandboxViolationException(string message) : base(message) { }
    }

    /// <summary>
    /// Every write must go through here. Rejects absolute paths, drive letters, UNC, .., reserved device names,
    /// and any symbolic link or junction (reparse point) along the path.
    /// </summary>
    public static class PathSandbox
    {
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>Resolves a relative path under root and verifies it does not escape. Returns the full path.</summary>
        public static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new SandboxViolationException("Sandbox root is not set.");
            }
            if (string.IsNullOrEmpty(relative))
            {
                throw new SandboxViolationException("Relative path is empty.");
            }
            if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.StartsWith("\\\\", StringComparison.Ordinal) || relative.StartsWith("//", StringComparison.Ordinal))
            {
                throw new SandboxViolationException("Absolute paths are not allowed: " + relative);
            }

            var invalid = Path.GetInvalidFileNameChars();
            foreach (var part in relative.Split('/', '\\'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    throw new SandboxViolationException("Path segment not allowed: " + relative);
                }
                if (part.IndexOfAny(invalid) >= 0)
                {
                    throw new SandboxViolationException("Path contains invalid characters: " + relative);
                }
                if (part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal))
                {
                    throw new SandboxViolationException("Path segments must not end with a space or dot: " + relative);
                }
                var stem = part;
                var dot = stem.IndexOf('.');
                if (dot >= 0)
                {
                    stem = stem.Substring(0, dot);
                }
                foreach (var reserved in ReservedNames)
                {
                    if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SandboxViolationException("Reserved device name not allowed: " + relative);
                    }
                }
            }

            var rootFull = NormalizeRoot(root);
            var full = Path.GetFullPath(Path.Combine(rootFull, relative));
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new SandboxViolationException("Path escapes the sandbox: " + relative);
            }
            EnsureNoReparsePoints(rootFull, full);
            return full;
        }

        /// <summary>Verifies that root itself, if it exists, is not a reparse point.</summary>
        public static string NormalizeRoot(string root)
        {
            var rootFull = Path.GetFullPath(root);
            rootFull = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var trimmed = rootFull.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(trimmed) && IsReparsePoint(trimmed))
            {
                throw new SandboxViolationException("Sandbox root is a symbolic link or junction: " + trimmed);
            }
            return rootFull;
        }

        /// <summary>Walks existing directories and files from root downwards and rejects any reparse point.</summary>
        public static void EnsureNoReparsePoints(string rootFull, string full)
        {
            var rest = full.Substring(rootFull.Length);
            var current = rootFull.TrimEnd(Path.DirectorySeparatorChar);
            foreach (var part in rest.Split(Path.DirectorySeparatorChar))
            {
                if (part.Length == 0)
                {
                    continue;
                }
                current = Path.Combine(current, part);
                if ((Directory.Exists(current) || File.Exists(current)) && IsReparsePoint(current))
                {
                    throw new SandboxViolationException("Path goes through a symbolic link or junction: " + current);
                }
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}
