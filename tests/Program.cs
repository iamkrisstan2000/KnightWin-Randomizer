using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KnightWin.Core;

int fails = 0;
void Check(string label, bool cond, string? extra = null)
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {label}{(extra != null ? "  [" + extra + "]" : "")}");
    if (!cond) fails++;
}

var root = Path.Combine(Path.GetTempPath(), "kwr_test_" + Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(root);

// ---------- 1. random token properties ----------
var s = new AppSettings { Length = 10 };
var pool = RenameEngine.BuildPool(s);
Check("pool excludes look-alikes", !pool.Contains('0') && !pool.Contains('O') && !pool.Contains('1') && !pool.Contains('l'), pool);

var tokens = Enumerable.Range(0, 500).Select(_ => RenameEngine.RandomToken(pool, 12)).ToList();
Check("token length exact", tokens.All(t => t.Length == 12));
Check("tokens are varied", tokens.Distinct().Count() == 500);
Check("tokens use only pool chars", tokens.All(t => t.All(c => pool.Contains(c))));

// ---------- 2. three modes ----------
var files = new List<string>();
foreach (var n in new[] { "photo one.jpg", "PHOTO TWO.JPG", "notes.txt", "report.txt", "resume.pdf" })
{
    var p = Path.Combine(root, n);
    File.WriteAllText(p, "x");
    files.Add(p);
}

var st = new AppSettings { Length = 8, Separator = "_", DateFormat = "yyyy-MM-dd", CustomPrefix = "Knights" };
var planRandom = RenameEngine.Plan(files, RenameMode.TotalRandom, st, "Knights");
Check("random: 5 planned", planRandom.Count == 5);
Check("random: keeps extensions", planRandom.All(p => p.TargetName.EndsWith(Path.GetExtension(p.SourceName))));
Check("random: unique targets", planRandom.Select(p => p.TargetName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 5);
Check("random: stem length 8", planRandom.All(p => Path.GetFileNameWithoutExtension(p.TargetName).Length == 8), planRandom[0].TargetName);

var planDate = RenameEngine.Plan(files, RenameMode.DatePrefix, st, null);
string today = DateTime.Now.ToString("yyyy-MM-dd");
Check("date: prefix present", planDate.All(p => Path.GetFileNameWithoutExtension(p.TargetName).StartsWith(today + "_")), planDate[0].TargetName);

var planCustom = RenameEngine.Plan(files, RenameMode.CustomPrefix, st, "Knights");
Check("custom: prefix present", planCustom.All(p => Path.GetFileNameWithoutExtension(p.TargetName).StartsWith("Knights_")), planCustom[0].TargetName);

// length bounds: 6 and 16
foreach (int len in new[] { 6, 16 })
{
    var sl = new AppSettings { Length = len };
    var p1 = RenameEngine.Plan(new[] { Path.Combine(root, "notes.txt") }, RenameMode.TotalRandom, sl, null)[0];
    Check($"length {len} honoured", Path.GetFileNameWithoutExtension(p1.TargetName).Length == len, p1.TargetName);
}

// ---------- 3. execute + verify on disk ----------
var before = Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(x => x).ToList();
var res = RenameEngine.Execute(planRandom);
Check("execute: all renamed", res.Renamed == 5 && res.Failed == 0, $"renamed={res.Renamed} failed={res.Failed} {string.Join(";", res.Errors)}");

var after = Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(x => x).ToList();
Check("execute: file count unchanged", before.Count == after.Count, $"{before.Count} vs {after.Count}");
Check("execute: no temp files left", !after.Any(f => f!.StartsWith(".kwr_")));
Check("execute: data preserved", Directory.GetFiles(root).All(f => File.ReadAllText(f) == "x"));
Check("execute: extensions preserved",
    after.Count(f => f!.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) == 2
    && after.Count(f => f!.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) == 2
    && after.Count(f => f!.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) == 1,
    string.Join(",", after));

// ---------- 4. undo restores exact original names ----------
int restored = UndoStack.Undo();
var restoredNames = Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(x => x).ToList();
Check("undo: count restored", restored == 5, restored.ToString());
Check("undo: names identical to originals", before.SequenceEqual(restoredNames!), string.Join(",", restoredNames));

// ---------- 5. collision safety over repeated batches ----------
var r2 = RenameEngine.Execute(RenameEngine.Plan(Directory.GetFiles(root), RenameMode.CustomPrefix, st, "Same"));
var r3 = RenameEngine.Execute(RenameEngine.Plan(Directory.GetFiles(root), RenameMode.CustomPrefix, st, "Same"));
Check("two batches: no failures", r2.Failed == 0 && r3.Failed == 0, $"{r2.Failed}/{r3.Failed}");
Check("no file lost over batches", Directory.GetFiles(root).Length == 5, Directory.GetFiles(root).Length.ToString());

// ---------- 6. the extension is ALWAYS preserved (no opt-out any more) ----------
var st2 = new AppSettings { Length = 6, Separator = "" };
foreach (var original in new[] { "photo one.jpg", "archive.tar.gz", "no-extension", "UPPER.JPG", "dotted.name.v2.pdf" })
{
    var planned = RenameEngine.ComposeFileName(original, RenameMode.TotalRandom, st2, null,
                                               RenameEngine.RandomToken(RenameEngine.BuildPool(st2), 6));
    string wantExt = Path.GetExtension(original);
    bool ok = planned.EndsWith(wantExt, StringComparison.Ordinal) && planned.Length > wantExt.Length;
    Check($"ext preserved: {original}", ok, planned);
}
// the generated stem must never smuggle a dot in and look like an extension
var dotFree = RenameEngine.ComposeFileName("movie.mp4", RenameMode.CustomPrefix, st2, "a.b", "XXXXXX");
Check("prefix dots sanitised, real ext intact", dotFree.EndsWith(".mp4") && dotFree.Count(c => c == '.') == 1, dotFree);

// ---------- 7. scope of a batch ----------
var dir2 = Path.Combine(root, "sub"); Directory.CreateDirectory(dir2);
File.WriteAllText(Path.Combine(dir2, "A.txt"), "x");
File.WriteAllText(Path.Combine(dir2, "B.txt"), "x");
var planOne = RenameEngine.Plan(new[] { Path.Combine(dir2, "A.txt") }, RenameMode.CustomPrefix, st, "X");
Check("plan is scoped to the batch", planOne.Count == 1 && File.Exists(Path.Combine(dir2, "B.txt")));

// ---------- 8. empty alphabet fallback ----------
var empty = new AppSettings { UseLower = false, UseUpper = false, UseDigits = false, UseSymbols = false };
Check("empty alphabet falls back", RenameEngine.BuildPool(empty).Length > 0, RenameEngine.BuildPool(empty));

// ---------- 9. folder scan: top level only ----------
var tree = Path.Combine(root, "tree");
var deep = Path.Combine(tree, "deep");
Directory.CreateDirectory(deep);
File.WriteAllText(Path.Combine(tree, "one.txt"), "x");
File.WriteAllText(Path.Combine(tree, "two.jpg"), "x");
File.WriteAllText(Path.Combine(deep, "three.pdf"), "x");
File.WriteAllText(Path.Combine(tree, "desktop.ini"), "x");

var flat = FolderScanner.ScanFolders(new[] { tree }, recursive: false, skipHidden: true);
Check("scan top-level: 2 files", flat.Files.Count == 2, string.Join(",", flat.Files));
Check("scan top-level: skips desktop.ini", !flat.Files.Any(f => f.EndsWith("desktop.ini")));

// ---------- 10. folder scan: recursive ----------
var rec = FolderScanner.ScanFolders(new[] { tree }, recursive: true, skipHidden: true);
Check("scan recursive: 3 files", rec.Files.Count == 3, string.Join(",", rec.Files));
Check("scan recursive: includes subfolder file", rec.Files.Any(f => f.EndsWith("three.pdf")));

// ---------- 11. hidden files skipped ----------
var hid = Path.Combine(tree, ".hidden.txt");   // dotfile == Hidden attribute on Unix
File.WriteAllText(hid, "x");
if (OperatingSystem.IsWindows())
    File.SetAttributes(hid, File.GetAttributes(hid) | FileAttributes.Hidden);
Console.WriteLine("    (hidden attr reported: " + (File.GetAttributes(hid) & FileAttributes.Hidden) + ")");
var sk = FolderScanner.ScanFolders(new[] { tree }, recursive: true, skipHidden: true);
Check("scan: hidden file skipped", !sk.Files.Any(f => f.EndsWith(".hidden.txt")) && sk.SkippedHidden >= 1, sk.SkippedHidden.ToString());
var keep = FolderScanner.ScanFolders(new[] { tree }, recursive: true, skipHidden: false);
Check("scan: include hidden when asked", keep.Files.Any(f => f.EndsWith(".hidden.txt")));

// ---------- 12. folder rewrite over a whole tree ----------
var root2 = Path.Combine(Path.GetTempPath(), "kwr_tree_" + Guid.NewGuid().ToString("N")[..8]);
var a = Path.Combine(root2, "A"); var b = Path.Combine(root2, "B");
Directory.CreateDirectory(a); Directory.CreateDirectory(b);
for (int i = 0; i < 20; i++) File.WriteAllText(Path.Combine(a, $"file{i}.txt"), "x");
for (int i = 0; i < 20; i++) File.WriteAllText(Path.Combine(b, $"file{i}.txt"), "x");

var treeScan = FolderScanner.ScanFolders(new[] { root2 }, recursive: true, skipHidden: true);
Check("tree scan: 40 files", treeScan.Files.Count == 40, treeScan.Files.Count.ToString());
var treePlan = RenameEngine.Plan(treeScan.Files, RenameMode.TotalRandom, st, null);
var treeRes = RenameEngine.Execute(treePlan);
Check("tree rename: all 40 renamed", treeRes.Renamed == 40 && treeRes.Failed == 0, $"{treeRes.Renamed}/{treeRes.Failed}");
Check("tree rename: still 20 per folder",
    Directory.GetFiles(a).Length == 20 && Directory.GetFiles(b).Length == 20,
    $"{Directory.GetFiles(a).Length}/{Directory.GetFiles(b).Length}");
Check("tree rename: extensions kept",
    Directory.GetFiles(a).All(f => f.EndsWith(".txt")));
int treeUndo = UndoStack.Undo();
Check("tree undo: 40 restored", treeUndo == 40, treeUndo.ToString());
var want = Enumerable.Range(0, 20).Select(i => $"file{i}.txt").OrderBy(x => x, StringComparer.Ordinal).ToList();
var got = Directory.GetFiles(a).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList();
Check("tree undo: names back", want.SequenceEqual(got!), string.Join(",", got.Take(4)));
Directory.Delete(root2, true);

// ---------- 13. same generated name is allowed in two different folders ----------
var root3 = Path.Combine(Path.GetTempPath(), "kwr_uniq_" + Guid.NewGuid().ToString("N")[..8]);
var c1 = Path.Combine(root3, "C1"); var c2 = Path.Combine(root3, "C2");
Directory.CreateDirectory(c1); Directory.CreateDirectory(c2);
File.WriteAllText(Path.Combine(c1, "a.txt"), "x");
File.WriteAllText(Path.Combine(c2, "b.txt"), "x");
var fixed1 = new RenameEngine.Item { SourcePath = Path.Combine(c1, "a.txt"), SourceName = "a.txt", TargetName = "same.txt", TargetPath = Path.Combine(c1, "same.txt") };
var fixed2 = new RenameEngine.Item { SourcePath = Path.Combine(c2, "b.txt"), SourceName = "b.txt", TargetName = "same.txt", TargetPath = Path.Combine(c2, "same.txt") };
var fixRes = RenameEngine.Execute(new[] { fixed1, fixed2 });
Check("same name across folders: both succeed", fixRes.Renamed == 2 && fixRes.Failed == 0, $"{fixRes.Renamed}/{fixRes.Failed}");
Check("same name across folders: files exist", File.Exists(Path.Combine(c1, "same.txt")) && File.Exists(Path.Combine(c2, "same.txt")));
UndoStack.Undo();
Directory.Delete(root3, true);

Directory.Delete(root, true);
Console.WriteLine(fails == 0 ? "\nALL TESTS PASSED" : $"\n{fails} TEST(S) FAILED");
return fails == 0 ? 0 : 1;
