using System;
using System.IO;

namespace AgentTranslation.Core
{
    public sealed class SandboxViolationException : Exception
    {
        public SandboxViolationException(string message) : base(message) { }
    }

    /// <summary>
    /// 所有寫入都必須經過這裡。拒絕絕對路徑、磁碟代號、UNC、..、保留裝置名稱，
    /// 以及路徑上任何符號連結／junction（reparse point）。
    /// </summary>
    public static class PathSandbox
    {
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>把相對路徑解析到 root 底下，並驗證沒有逃逸。回傳完整路徑。</summary>
        public static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new SandboxViolationException("沙箱根目錄未設定。");
            }
            if (string.IsNullOrEmpty(relative))
            {
                throw new SandboxViolationException("相對路徑為空。");
            }
            if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.StartsWith("\\\\", StringComparison.Ordinal) || relative.StartsWith("//", StringComparison.Ordinal))
            {
                throw new SandboxViolationException("不允許絕對路徑：" + relative);
            }

            var invalid = Path.GetInvalidFileNameChars();
            foreach (var part in relative.Split('/', '\\'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    throw new SandboxViolationException("不允許的路徑片段：" + relative);
                }
                if (part.IndexOfAny(invalid) >= 0)
                {
                    throw new SandboxViolationException("路徑含不合法字元：" + relative);
                }
                if (part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal))
                {
                    throw new SandboxViolationException("路徑片段不可以空白或句點結尾：" + relative);
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
                        throw new SandboxViolationException("不允許的保留名稱：" + relative);
                    }
                }
            }

            var rootFull = NormalizeRoot(root);
            var full = Path.GetFullPath(Path.Combine(rootFull, relative));
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new SandboxViolationException("路徑逃逸出沙箱：" + relative);
            }
            EnsureNoReparsePoints(rootFull, full);
            return full;
        }

        /// <summary>確認 root 本身存在時不是 reparse point。</summary>
        public static string NormalizeRoot(string root)
        {
            var rootFull = Path.GetFullPath(root);
            rootFull = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var trimmed = rootFull.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(trimmed) && IsReparsePoint(trimmed))
            {
                throw new SandboxViolationException("沙箱根目錄是符號連結或 junction：" + trimmed);
            }
            return rootFull;
        }

        /// <summary>從 root 往下逐層檢查已存在的目錄與檔案，任何一層是 reparse point 就拒絕。</summary>
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
                    throw new SandboxViolationException("路徑經過符號連結或 junction：" + current);
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
