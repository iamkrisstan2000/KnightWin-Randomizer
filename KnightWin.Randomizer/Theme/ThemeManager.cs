using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KnightWin.Theme
{
    public enum AppMode { System, Light, Dark }

    public sealed class AccentDef
    {
        public string Key { get; init; } = "Cyan";
        public string Label { get; init; } = "Cyan";
        public Color Base { get; init; }
        public Color Hover { get; init; }
        public Color Pressed { get; init; }
        public Color Text { get; init; }          // readable text on top of the base color

        public SolidColorBrush BrushBase => ThemeManager.MakeBrush(Base);
        public SolidColorBrush BrushHover => ThemeManager.MakeBrush(Hover);
    }

    /// <summary>Central palette + accent table. Everything is swapped at runtime via DynamicResource keys.</summary>
    public static class ThemeManager
    {
        public static readonly AccentDef[] Accents =
        {
            new AccentDef { Key = "Magenta",    Label = "Magenta",    Base = Hex("#E3008C"), Hover = Hex("#F2279C"), Pressed = Hex("#BF0076"), Text = Colors.White },
            new AccentDef { Key = "Purple",     Label = "Purple",     Base = Hex("#8A5CF5"), Hover = Hex("#9C74F7"), Pressed = Hex("#7048D6"), Text = Colors.White },
            new AccentDef { Key = "Matcha",     Label = "Matcha",     Base = Hex("#86B33F"), Hover = Hex("#97C44C"), Pressed = Hex("#6F9930"), Text = Hex("#0E1A02") },
            new AccentDef { Key = "Cyan",       Label = "Cyan",       Base = Hex("#0FA3E0"), Hover = Hex("#27B4EF"), Pressed = Hex("#0B87BC"), Text = Colors.White },
            new AccentDef { Key = "PastelPink", Label = "Pastel Pink",Base = Hex("#FFB3D1"), Hover = Hex("#FFC7DE"), Pressed = Hex("#F09BBF"), Text = Hex("#3A0E20") },
            new AccentDef { Key = "Beige",      Label = "Beige",      Base = Hex("#DCC7A8"), Hover = Hex("#E8D6BC"), Pressed = Hex("#C7B08D"), Text = Hex("#2A2113") },
            new AccentDef { Key = "Red",        Label = "Red",        Base = Hex("#D93639"), Hover = Hex("#EB4A4C"), Pressed = Hex("#B9272C"), Text = Colors.White },
            new AccentDef { Key = "Orange",     Label = "Orange",     Base = Hex("#F0802C"), Hover = Hex("#FF963D"), Pressed = Hex("#CF681C"), Text = Hex("#251507") },
            // Resolved at runtime: Black in Light mode, White in Dark mode.
            new AccentDef { Key = "Monochrome", Label = "Black",      Base = Colors.Black, Hover = Hex("#292929"), Pressed = Hex("#111111"), Text = Colors.White },
        };

        public static AccentDef FindAccent(string? key) =>
            Array.Find(Accents, a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Accents[3];

        public static Color Hex(string s)
        {
            s = s.TrimStart('#');
            if (s.Length == 6) s = "FF" + s;
            return (Color)ColorConverter.ConvertFromString("#" + s);
        }

        public static SolidColorBrush MakeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static bool IsSystemDark()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (k?.GetValue("AppsUseLightTheme") is int v) return v == 0;
            }
            catch { /* fall through */ }
            return true;
        }

        public static Color? SystemAccent()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (k?.GetValue("ColorizationColor") is int v)
                {
                    // DWM ColorizationColor is an ARGB DWORD (0xAARRGGBB). Do not swap
                    // channels: disabled-button opacity, not the registry, caused the muddy
                    // colours seen over the user's wallpaper.
                    return System.Windows.Media.Color.FromArgb(255,
                        (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
                }
            }
            catch { /* ignore */ }
            return null;
        }

        /// <summary>Resolves "System" against the live OS theme, otherwise uses the named palette.</summary>
        public static AccentDef ResolveAccent(string? key)
        {
            if (string.Equals(key, "Monochrome", StringComparison.OrdinalIgnoreCase))
            {
                bool dark = CurrentIsDark;
                return new AccentDef
                {
                    Key = "Monochrome",
                    Label = dark ? "White" : "Black",
                    Base = dark ? Colors.White : Colors.Black,
                    Hover = dark ? Hex("#EDEDED") : Hex("#292929"),
                    Pressed = dark ? Hex("#D4D4D4") : Hex("#111111"),
                    Text = dark ? Hex("#111113") : Colors.White
                };
            }
            if (string.IsNullOrEmpty(key) || key == "System")
            {
                var sys = SystemAccent() ?? Hex("#0FA3E0");
                // keep the app's identity: fall back to Cyan when the OS accent is near-neutral
                int mx = Math.Max(sys.R, Math.Max(sys.G, sys.B)), mn = Math.Min(sys.R, Math.Min(sys.G, sys.B));
                if (mx - mn < 28) sys = Hex("#0FA3E0");
                bool dark = IsDarkLike(sys);
                return new AccentDef
                {
                    Key = "System",
                    Label = "System",
                    Base = sys,
                    Hover = Shift(sys, dark ? 22 : -18),
                    Pressed = Shift(sys, dark ? -22 : -38),
                    Text = dark ? Colors.White : Hex("#101010")
                };
            }
            return FindAccent(key);
        }

        static bool IsDarkLike(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 150;

        static Color Shift(Color c, int d) => Color.FromArgb(c.A,
            (byte)Math.Clamp(c.R + d, 0, 255),
            (byte)Math.Clamp(c.G + d, 0, 255),
            (byte)Math.Clamp(c.B + d, 0, 255));

        public static void Apply(AppMode mode, string? accentKey)
        {
            bool dark = mode switch
            {
                AppMode.Dark => true,
                AppMode.Light => false,
                _ => IsSystemDark()
            };
            // Resolve the theme-dependent Black/White accent against the NEW mode,
            // not the previous palette while switching Light <-> Dark.
            CurrentIsDark = dark;
            var acc = ResolveAccent(accentKey);
            var dict = new ResourceDictionary();
            foreach (var kv in Palette(dark, acc)) dict[kv.Key] = kv.Value;
            dict["AccentBaseColor"] = acc.Base;
            dict["AccentHoverColor"] = acc.Hover;
            dict["AccentPressedColor"] = acc.Pressed;
            dict["AccentText"] = MakeBrush(acc.Text);
            // Solid, not translucent: an accent button that lets the wallpaper through loses
            // its label completely. The accent colour itself is already fully opaque.
            dict["Accent"] = acc.BrushBase;
            dict["AccentHover"] = acc.BrushHover;
            dict["AccentPressed"] = MakeBrush(acc.Pressed);
            var tint = acc.Base;
            dict["AccentGlow"] = MakeBrush(Color.FromArgb(64, tint.R, tint.G, tint.B));
            var soft = Color.FromArgb(dark ? (byte)38 : (byte)30, tint.R, tint.G, tint.B);
            dict["AccentSoft"] = MakeBrush(soft);
            dict["AccentSoftColor"] = soft;
            // Approximately 1% of the selected accent, in both the title and rail.
            var chromeTint = Color.FromArgb(3, tint.R, tint.G, tint.B);
            dict["ChromeTint"] = MakeBrush(chromeTint);
            dict["ChromeTintColor"] = chromeTint;
            dict["PaneBgSolid"] = MakeBrush(dark ? Hex("#171719") : Hex("#F4F4F6"));
            dict["ShadowColor"] = dark ? Colors.Black : Hex("#5A5A66");

            dict["PaletteMarker"] = "KnightWin.Palette";
            var app = Application.Current;
            if (app == null) { _palette = dict; return; }

            // replace only OUR palette dictionary — never the styles dictionary
            var merged = app.Resources.MergedDictionaries;
            ResourceDictionary? existing = null;
            foreach (var rd in merged)
            {
                if (rd.Contains("PaletteMarker")) { existing = rd; break; }
            }
            if (existing != null)
            {
                int idx = merged.IndexOf(existing);
                merged.RemoveAt(idx);
                merged.Insert(idx, dict);
            }
            else
            {
                merged.Insert(0, dict);
            }
            _palette = dict;
            CurrentIsDark = dark;
        }

        public static bool CurrentIsDark { get; private set; } = true;

        private static ResourceDictionary? _palette;

        static Dictionary<string, object> Palette(bool dark, AccentDef acc)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            void B(string k, string hex) => d[k] = MakeBrush(Hex(hex));

            if (dark)
            {
                // 12% wallpaper contribution: 224/255 paint opacity, not 12% opacity.
                // Neutral grey-black rather than navy. Root is the ONLY page base; chrome
                // overlays its L-shaped area to reach about 4% wallpaper contribution.
                B("WindowBg",        "#E0171719");
                B("WindowBgSolid",   "#FF171719");
                B("PaneBg",          "#AB121214");
                B("PaneBgSoft",      "#551E1E20");
                // Buttons are their own opaque surface. Letting the acrylic show through a
                // button made the wallpaper bleed into the label and turned every accent
                // button into an unreadable olive smear.
                B("LayerBg",         "#FF29292C");
                B("LayerBgHover",    "#FF343438");
                B("LayerBgPressed",  "#FF202023");
                B("ButtonStroke",    "#33FFFFFF");
                B("AccentButtonStroke", "#26FFFFFF");
                B("AccentSoftStroke", "#1AFFFFFF");
                B("DisabledButtonBg", "#FF28282B");
                B("DisabledButtonStroke", "#1AFFFFFF");
                // Card paint contributes 33% over the 88% base, leaving the wallpaper
                // present (~8%) while giving cards a distinct grey step and legible text.
                B("CardBg",          "#55545457");
                B("CardStroke",      "#0DFFFFFF");
                B("CardStrokeTop",   "#33FFFFFF");
                B("Divider",         "#0AFFFFFF");
                B("TextPrimary",     "#FFFFFFFF");
                B("TextSecondary",   "#CCFFFFFF");
                B("TextTertiary",    "#A1FFFFFF");
                B("TextOnAccent",    acc.Text == Colors.White ? "#FFFFFFFF" : "#FF101010");
                B("FieldBg",         "#0EFFFFFF");
                B("FieldBgHover",    "#17FFFFFF");
                B("FieldStroke",     "#1AFFFFFF");
                B("FieldStrokeBot",  "#3DFFFFFF");
                B("CloseHover",      "#E81123");
                B("Scrim",           "#99000000");
                B("ChipBg",          "#14FFFFFF");
                B("Success",         "#4CC38A");
                B("Danger",          "#F1707A");
                B("ListHover",       "#1FFFFFFF");
                B("SheetBg",         "#88252528");
                // 171/255 overlay over the root's 224/255 gives ~96% title/rail opacity.
                B("ChromeBg",        "#AB121214");
                B("WinBorder",       "#00FFFFFF");
            }
            else
            {
                // 5% wallpaper contribution: 242/255 paint opacity.
                B("WindowBg",        "#F2F4F4F6");
                B("WindowBgSolid",   "#FFF4F4F6");
                B("PaneBg",          "#9BFFFFFF");
                B("PaneBgSoft",      "#77FFFFFF");
                B("LayerBg",         "#FFFFFFFF");
                B("LayerBgHover",    "#FFF3F4F8");
                B("LayerBgPressed",  "#FFE9EAEF");
                B("ButtonStroke",    "#1F000000");
                B("AccentButtonStroke", "#3A000000");
                B("AccentSoftStroke", "#18000000");
                B("DisabledButtonBg", "#FFF0F1F4");
                B("DisabledButtonStroke", "#1A000000");
                // Cards remain translucent (~4% wallpaper contribution after stacking).
                B("CardBg",          "#3DFFFFFF");
                B("CardStroke",      "#0A000000");
                B("CardStrokeTop",   "#33FFFFFF");
                B("Divider",         "#08000000");
                B("TextPrimary",     "#FF1A1A1E");
                B("TextSecondary",   "#A61A1A1E");
                B("TextTertiary",    "#911A1A1E");
                B("TextOnAccent",    acc.Text == Colors.White ? "#FFFFFFFF" : "#FF101010");
                B("FieldBg",         "#B8FFFFFF");
                B("FieldBgHover",    "#E6FFFFFF");
                B("FieldStroke",     "#14000000");
                B("FieldStrokeBot",  "#33000000");
                B("CloseHover",      "#E81123");
                B("Scrim",           "#66FFFFFF");
                B("ChipBg",          "#0F000000");
                B("Success",         "#1E9E5A");
                B("Danger",          "#C42B3A");
                B("ListHover",       "#0F000000");
                B("SheetBg",         "#FFFAFBFE");
                // Fluent's navigation pane sits slightly GREYER than the content surface. In light
                // mode a whiter chrome vanishes into the page, taking the merged L with it.
                // 155/255 overlay over the root's 242/255 gives ~98% title/rail opacity.
                B("ChromeBg",        "#9BFFFFFF");
                B("WinBorder",       "#00000000");
            }
            return d;
        }
    }
}
