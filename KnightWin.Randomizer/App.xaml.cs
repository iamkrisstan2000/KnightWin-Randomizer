using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using KnightWin.Core;
using KnightWin.Theme;

namespace KnightWin
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; set; } = new();

        /// <summary>
        /// Writes a one-line "which build am I" stamp next to the settings. Keeps the
        /// shipped binary identifiable even when the version badge is out of view.
        /// </summary>
        private static void WriteStartupStamp()
        {
            try
            {
                var v = typeof(App).Assembly.GetName().Version;
                Directory.CreateDirectory(AppSettings.Folder);
                File.WriteAllText(Path.Combine(AppSettings.Folder, "build.txt"),
                    $"KnightWin Randomizer v{v?.Major}.{v?.Minor}.{v?.Build}" + Environment.NewLine +
                    $"started {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"exe {Environment.ProcessPath}" + Environment.NewLine +
                    $"shell navigation-rail" + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Appends unexpected failures to error.log instead of vanishing silently.</summary>
        private static void LogCrash(string stage, Exception ex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppSettings.Folder);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppSettings.Folder, "error.log"),
                    $"[{DateTime.Now:u}] {stage}: {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            WriteStartupStamp();
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                LogCrash("AppDomain", args.ExceptionObject as Exception ?? new Exception("unknown"));
            DispatcherUnhandledException += (_, args) =>
            {
                LogCrash("Dispatcher", args.Exception);
                args.Handled = true;
                MessageBox.Show("KnightWin hit an unexpected problem and kept running.\n\n" +
                                args.Exception.Message, "KnightWin Randomizer",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            Settings = AppSettings.Load();
            var mode = Settings.AppMode switch
            {
                "Light" => AppMode.Light,
                "Dark" => AppMode.Dark,
                _ => AppMode.System
            };
            ThemeManager.Apply(mode, Settings.AccentKey);

            SystemEvents_ThemeChanged(null, EventArgs.Empty);
            // follow the OS when the user flips light/dark or changes the system accent
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_ThemeChanged;
        }

        private void SystemEvents_ThemeChanged(object? sender, EventArgs e)
        {
            if (Settings.AppMode != "System" && Settings.AccentKey != "System") return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThemeManager.Apply(Settings.AppMode switch
                {
                    "Light" => AppMode.Light,
                    "Dark" => AppMode.Dark,
                    _ => AppMode.System
                }, Settings.AccentKey);
                if (MainWindow is MainWindow mw)
                {
                    mw.RefreshBackdrop();
                    mw.RefreshAccentChoices();
                }
            }));
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_ThemeChanged;
            base.OnExit(e);
        }

        /// <summary>Colour the DWM/acrylic tint should use for the current theme.</summary>
        public static Color BackdropTint => ThemeManager.CurrentIsDark
            ? Color.FromRgb(0x0A, 0x0D, 0x16)
            : Color.FromRgb(0xF2, 0xF3, 0xF8);
    }
}
