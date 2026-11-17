using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KnightWin.Core
{
    /// <summary>
    /// Turns a picked folder into the list of files that should be renamed.
    /// Kept separate from the UI so the rules are testable and boring:
    ///   - top level only, or the whole tree when recursive is on
    ///   - hidden and system files are skipped by default (they are not "content")
    ///   - junk Windows artefacts (desktop.ini, Thumbs.db, .tmp) are always skipped
    ///   - unreadable subfolders are skipped instead of failing the whole scan
    /// </summary>
    public static class FolderScanner
    {
        private static readonly HashSet<string> AlwaysSkip = new(StringComparer.OrdinalIgnoreCase)
        {
            "desktop.ini", "thumbs.db", "ehthumbs.db", ".ds_store"
        };

        public sealed class Scan
        {
            public List<string> Files { get; } = new();
            public List<string> Folders { get; } = new();
            public int SkippedHidden;
            public int SkippedOther;
            public List<string> Errors { get; } = new();
        }

        /// <summary>Scans one or more roots and returns every file eligible for renaming.</summary>
        public static Scan ScanFolders(IEnumerable<string> roots, bool recursive, bool skipHidden)
        {
            var scan = new Scan();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) { scan.SkippedOther++; continue; }
                    scan.Folders.Add(root);
                    Walk(new DirectoryInfo(root), recursive, skipHidden, scan, seen);
                }
                catch (Exception ex)
                {
                    scan.Errors.Add($"{Path.GetFileName(root)}: {ex.Message}");
                }
            }
            return scan;
        }

        private static void Walk(DirectoryInfo dir, bool recursive, bool skipHidden, Scan scan,
                                 HashSet<string> seen)
        {
            IEnumerable<FileInfo> files;
            try
            {
                files = dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                scan.Errors.Add($"{dir.Name}: {ex.Message}");
                return;
            }

            foreach (var f in files)
            {
                try
                {
                    if (AlwaysSkip.Contains(f.Name)) { scan.SkippedOther++; continue; }
                    if (string.Equals(f.Extension, ".tmp", StringComparison.OrdinalIgnoreCase)
                        && f.Name.StartsWith(".kwr_", StringComparison.Ordinal)) { scan.SkippedOther++; continue; }
                    if (skipHidden &&
                        ((f.Attributes & FileAttributes.Hidden) != 0 ||
                         (f.Attributes & FileAttributes.System) != 0)) { scan.SkippedHidden++; continue; }

                    if (seen.Add(f.FullName)) scan.Files.Add(f.FullName);
                }
                catch { scan.SkippedOther++; }
            }

            if (!recursive) return;

            IEnumerable<DirectoryInfo> subs;
            try
            {
                subs = dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                scan.Errors.Add($"{dir.Name}: {ex.Message}");
                return;
            }

            foreach (var sub in subs)
            {
                try
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue; // no symlink loops
                    if (skipHidden && (sub.Attributes & FileAttributes.Hidden) != 0) { scan.SkippedHidden++; continue; }
                    Walk(sub, true, skipHidden, scan, seen);
                }
                catch (Exception ex)
                {
                    scan.Errors.Add($"{sub.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Groups file paths by the directory that contains them. Name collisions must be
        /// checked per folder, because two files in different folders may legally share a name.
        /// </summary>
        public static List<IGrouping<string, string>> ByFolder(IEnumerable<string> files) =>
            files.GroupBy(f => Path.GetDirectoryName(f) ?? "", StringComparer.OrdinalIgnoreCase)
                 .ToList();
    }
}
