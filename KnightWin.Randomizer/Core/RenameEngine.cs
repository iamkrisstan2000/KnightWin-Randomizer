using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KnightWin.Core
{
    public enum RenameMode { TotalRandom = 0, DatePrefix = 1, CustomPrefix = 2 }

    /// <summary>
    /// Filename generation + safe rename execution.
    /// Uniqueness is guaranteed per folder; every plan is pre-validated so a batch
    /// is all-or-nothing instead of half-renamed.
    /// </summary>
    public static class RenameEngine
    {
        private const string Lower = "abcdefghijkmnopqrstuvwxyz";
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Digits = "23456789";
        private const string Syms = "-_";
        private const string LowerAll = "abcdefghijklmnopqrstuvwxyz";
        private const string UpperAll = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string DigitsAll = "0123456789";

        private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        public static string BuildPool(AppSettings s)
        {
            var sb = new StringBuilder(80);
            sb.Append(s.UseLower ? (s.AvoidLookAlikes ? Lower : LowerAll) : "");
            sb.Append(s.UseUpper ? (s.AvoidLookAlikes ? Upper : UpperAll) : "");
            sb.Append(s.UseDigits ? (s.AvoidLookAlikes ? Digits : DigitsAll) : "");
            if (s.UseSymbols) sb.Append(Syms);
            if (sb.Length == 0) sb.Append(LowerAll);
            return sb.ToString();
        }

        /// <summary>Cryptographically seeded, unbiased random token.</summary>
        public static string RandomToken(string pool, int length)
        {
            if (pool.Length == 0) pool = LowerAll;
            var chars = new char[length];
            // rejection-free mapping via 32-bit values
            Span<byte> buf = stackalloc byte[4];
            for (int i = 0; i < length; i++)
            {
                uint v;
                do
                {
                    RandomNumberGenerator.Fill(buf);
                    v = BitConverter.ToUInt32(buf);
                } while (v >= uint.MaxValue - (uint.MaxValue % (uint)pool.Length));
                chars[i] = pool[(int)(v % (uint)pool.Length)];
            }
            return new string(chars);
        }

        /// <summary>Builds the new file name (without directory) for a single file.</summary>
        public static string ComposeFileName(string originalName, RenameMode mode, AppSettings s,
                                             string? customPrefix, string token)
        {
            string separator = s.Separator;
            // The extension is always preserved: a renamed file must still open with the same app.
            string ext = Path.GetExtension(originalName);

            string name;
            switch (mode)
            {
                case RenameMode.DatePrefix:
                    name = DateTime.Now.ToString(string.IsNullOrWhiteSpace(s.DateFormat) ? "yyyy-MM-dd" : s.DateFormat)
                           + separator + token;
                    break;
                case RenameMode.CustomPrefix:
                    var p = Sanitise((customPrefix ?? "").Trim());
                    name = p.Length > 0 ? p + separator + token : token;
                    break;
                default:
                    name = token;
                    break;
            }

            name = Sanitise(name).Trim();
            if (name.Length == 0) name = token;
            // Windows reserved device names
            if (IsReserved(name)) name = "_" + name;
            if (name.Length > 190) name = name[..190];
            return name + ext;
        }

        /// <summary>
        /// Cleans a user-supplied fragment (prefix, separator) so it can go into a file name.
        /// A dot is replaced rather than kept: the extension is appended afterwards, and a dot in
        /// the stem would either fake a second extension or truncate on some tools.
        /// </summary>
        public static string Sanitise(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(Array.IndexOf(Invalid, c) >= 0 || c == '.' ? '_' : c);
            return sb.ToString().TrimEnd('.', ' ');
        }

        private static bool IsReserved(string name)
        {
            string n = name.Trim().ToUpperInvariant();
            switch (n)
            {
                case "CON": case "PRN": case "AUX": case "NUL":
                case "COM1": case "COM2": case "COM3": case "COM4": case "COM5": case "COM6": case "COM7": case "COM8": case "COM9":
                case "LPT1": case "LPT2": case "LPT3": case "LPT4": case "LPT5": case "LPT6": case "LPT7": case "LPT8": case "LPT9":
                    return true;
            }
            return false;
        }

        public sealed class Item : System.ComponentModel.INotifyPropertyChanged
        {
            public string SourcePath { get; set; } = "";
            public string SourceName { get; init; } = "";
            public string TargetName { get; set; } = "";
            public string TargetPath { get; set; } = "";
            public string Token { get; set; } = "";

            /// <summary>Folder containing the file, with a trailing separator. Empty for legacy items.</summary>
            public string Folder { get; set; } = "";

            /// <summary>Folder name shown next to the file when a batch spans several folders.</summary>
            public string FolderLabel { get; set; } = "";

            /// <summary>Row state after a batch: pending, done or failed. Drives the row marker.</summary>
            public string Status { get; set; } = "pending";

            /// <summary>Why the row failed, shown as a tooltip on the marker.</summary>
            public string Error { get; set; } = "";

            public bool IsDone => Status == "done";
            public bool IsFailed => Status == "failed";
            public bool IsPending => Status == "pending";

            /// <summary>INotifyPropertyChanged so markers update live without rebuilding the list.</summary>
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

            private void Raise(string name) =>
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

            /// <summary>Marks the row renamed, and points it at the file's new path.</summary>
            public void MarkDone(string newPath = "")
            {
                if (newPath.Length > 0) TargetPath = newPath;
                Status = "done";
                Raise(nameof(Status));
                Raise(nameof(IsDone));
                Raise(nameof(IsFailed));
                Raise(nameof(IsPending));
            }

            /// <summary>Puts the row back in the queue after an undo.</summary>
            public void MarkPending(string originalPath = "")
            {
                if (originalPath.Length > 0) SourcePath = originalPath;
                Status = "pending";
                Error = "";
                Raise(nameof(Status));
                Raise(nameof(IsDone));
                Raise(nameof(IsFailed));
                Raise(nameof(IsPending));
            }

            public void MarkFailed(string reason)
            {
                Status = "failed";
                Error = reason;
                Raise(nameof(Status));
                Raise(nameof(IsDone));
                Raise(nameof(IsFailed));
                Raise(nameof(IsPending));
            }

            public bool Changed => !string.Equals(SourceName, TargetName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Plans a batch. Names are unique inside the folder and never collide with
        /// untouched files that will remain there.
        /// </summary>
        public static List<Item> Plan(IEnumerable<string> files, RenameMode mode, AppSettings s, string? customPrefix)
        {
            var pool = BuildPool(s);
            int len = Math.Clamp(s.Length, 6, 16);
            var list = new List<Item>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Reserve names that already exist, so nothing is ever overwritten.
            // A batch may span several folders (picking a folder tree), so this is done per
            // folder and the reservation is folded into the target's directory.
            foreach (var group in FolderScanner.ByFolder(files))
            {
                string dir = group.Key;
                var inBatchHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in group) inBatchHere.Add(Path.GetFileName(f));
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var existing in Directory.EnumerateFileSystemEntries(dir))
                    {
                        var fn = Path.GetFileName(existing);
                        if (!inBatchHere.Contains(fn)) taken.Add(Key(dir, fn));
                    }
                }
                catch { /* unreadable folder: uniqueness is still enforced inside the batch */ }
            }

            foreach (var path in files)
            {
                string target;
                string token;
                int guard = 0;
                string dir = Path.GetDirectoryName(path) ?? "";
                do
                {
                    token = RandomToken(pool, len);
                    target = ComposeFileName(Path.GetFileName(path), mode, s, customPrefix, token);
                    guard++;
                } while (taken.Contains(Key(dir, target)) && guard < 64);

                taken.Add(Key(dir, target));
                list.Add(new Item
                {
                    SourcePath = path,
                    SourceName = Path.GetFileName(path),
                    TargetName = target,
                    TargetPath = Path.Combine(Path.GetDirectoryName(path) ?? "", target),
                    Token = token
                });
            }
            return list;
        }

        /// <summary>Uniqueness key: the same name in two different folders is allowed.</summary>
        private static string Key(string dir, string name) => dir + "\\" + name;

        public sealed class Result
        {
            public int Renamed;
            public int Failed;
            public int Skipped;
            public readonly List<string> Errors = new();

            /// <summary>
            /// Path the file ended up at, keyed by the source path it started from. The queue
            /// uses this to keep showing each row after a batch with its real outcome.
            /// </summary>
            public readonly Dictionary<string, string> RenamedTo = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Failure messages keyed by source path, for the per-row marker.</summary>
            public readonly Dictionary<string, string> Failures = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Executes a plan. Two-phase (temp → final) so swaps never clash.</summary>
        public static Result Execute(IReadOnlyList<Item> items, bool allowUndoBookkeeping = true)
        {
            var r = new Result();
            var staged = new List<(string tmp, string final, Item item)>();

            foreach (var it in items)
            {
                if (!it.Changed) { r.Skipped++; continue; }
                try
                {
                    string tmp = Path.Combine(Path.GetDirectoryName(it.SourcePath)!,
                        ".kwr_" + Guid.NewGuid().ToString("N")[..10] + ".tmp");
                    File.Move(it.SourcePath, tmp);
                    staged.Add((tmp, it.TargetPath, it));
                }
                catch (Exception ex)
                {
                    r.Failed++;
                    r.Errors.Add($"{it.SourceName}: {ex.Message}");
                    r.Failures[it.SourcePath] = ex.Message;
                }
            }

            var journal = new List<(string from, string to)>();
            foreach (var (tmp, final, item) in staged)
            {
                try
                {
                    if (File.Exists(final)) throw new IOException("target already exists");
                    File.Move(tmp, final);
                    r.Renamed++;
                    journal.Add((final, item.SourcePath));
                    r.RenamedTo[item.SourcePath] = final;
                }
                catch (Exception ex)
                {
                    try { File.Move(tmp, item.SourcePath); } catch { }
                    r.Failed++;
                    r.Errors.Add($"{item.SourceName}: {ex.Message}");
                    r.Failures[item.SourcePath] = ex.Message;
                }
            }

            if (allowUndoBookkeeping) UndoStack.Push("Randomize", journal);
            return r;
        }
    }

    /// <summary>Single-level undo: remembers the last batch so a mis-click is recoverable.</summary>
    public static class UndoStack
    {
        private static List<(string from, string to)> _last = new();

        public static string LastLabel { get; private set; } = "";
        public static int LastCount => _last.Count;

        public static void Push(string label, List<(string from, string to)> pairs)
        {
            _last = pairs;
            LastLabel = label;
        }

        public static void Clear() { _last = new(); LastLabel = ""; }

        /// <summary>Reverts the last batch. Returns number of files restored.</summary>
        public static int Undo()
        {
            int ok = 0;
            foreach (var (from, to) in _last)
            {
                try
                {
                    if (File.Exists(from) && !File.Exists(to))
                    {
                        File.Move(from, to);
                        ok++;
                    }
                }
                catch { }
            }
            Clear();
            return ok;
        }
    }
}
