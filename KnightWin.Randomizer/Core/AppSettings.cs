using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnightWin.Core
{
    /// <summary>Persisted user preferences (%AppData%\KnightWin\Randomizer\settings.json).</summary>
    public sealed class AppSettings
    {
        // ---- Appearance -------------------------------------------------
        public string AppMode { get; set; } = "System";      // System | Light | Dark
        public string AccentKey { get; set; } = "System";    // System | Magenta | Purple | Matcha | Cyan | PastelPink | Beige | Red | Orange | Monochrome

        // ---- Random generation -----------------------------------------
        public int Length { get; set; } = 10;                // 6..16
        public bool UseLower { get; set; } = true;
        public bool UseUpper { get; set; } = true;
        public bool UseDigits { get; set; } = true;
        public bool UseSymbols { get; set; } = false;
        public bool AvoidLookAlikes { get; set; } = true;    // drop 0/O, 1/l/I, 5/S, 2/Z ...
        public string Separator { get; set; } = "_";
        public string DateFormat { get; set; } = "yyyy-MM-dd";

        // ---- Behaviour --------------------------------------------------
        public bool CopyToClipboard { get; set; } = true;
        public string CustomPrefix { get; set; } = "Knight";
        public bool ExpandFolders { get; set; } = true;   // picking or dropping a folder adds the files inside
        public bool FolderRecursive { get; set; } = false; // ...and walks subfolders too
        public bool SkipHidden { get; set; } = true;       // leave hidden/system files alone
        public double WindowWidth { get; set; } = 1180;
        public double WindowHeight { get; set; } = 780;
        public bool NavCollapsed { get; set; } = false;

        [JsonIgnore]
        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KnightWin", "Randomizer");

        [JsonIgnore]
        private static string FilePath => Path.Combine(Folder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                    if (s != null) return s.Sanitised();
                }
            }
            catch { /* corrupt or unreadable → defaults */ }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { /* never crash the shell over a settings write */ }
        }

        private AppSettings Sanitised()
        {
            Length = Math.Clamp(Length, 6, 16);
            if (!UseLower && !UseUpper && !UseDigits && !UseSymbols)
            {
                UseLower = true; UseUpper = true; UseDigits = true;
            }
            if (string.IsNullOrEmpty(Separator)) Separator = "_";
            if (Separator.Length > 3) Separator = Separator[..3];
            // "." and "" used to be offered as separators; both break the extension now that it
            // is always preserved, so retire the dot as a separator.
            if (Separator == ".") Separator = "_";
            if (string.IsNullOrEmpty(AppMode)) AppMode = "System";
            if (string.IsNullOrEmpty(AccentKey)) AccentKey = "System";
            if (WindowWidth < 900 || double.IsNaN(WindowWidth)) WindowWidth = 1180;
            if (WindowHeight < 620 || double.IsNaN(WindowHeight)) WindowHeight = 780;
            return this;
        }
    }
}
