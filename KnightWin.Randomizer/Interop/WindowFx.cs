using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KnightWin.Interop
{
    /// <summary>
    /// Thin, allocation-free P/Invoke layer: acrylic/mica backdrops, rounded corners,
    /// native drag+snap, and DWM frame extension. No external UI framework involved.
    /// </summary>
    internal static class WindowFx
    {
        // ---------- SetWindowCompositionAttribute ----------
        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_ENABLE_HOSTBACKDROP = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public uint GradientColor;   // 0xAABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private const int WCA_ACCENT_POLICY = 19;

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Tells DWM to composite the window from the ALPHA CHANNEL of its redirection surface
        // instead of treating it as opaque. This is the piece WPF needs: a normal (non-layered)
        // WPF window gets an opaque redirection surface, so any XAML left transparent is painted
        // out and the system backdrop material behind it never shows. Passing an EMPTY blur region
        // enables per-pixel alpha without asking for a blur of its own.
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_BLURBEHIND
        {
            public uint dwFlags;          // DWM_BB_ENABLE
            public bool fEnable;
            public IntPtr hRgnBlur;       // null = whole window
            public bool fTransitionOnMaximized;
        }
        private const uint DWM_BB_ENABLE = 0x1;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        // Win11 22H2+: thickness of the frame line DWM paints over the window edge.
        // This is the attribute that removes the thick border around a borderless window.
        private const int DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37;
        // Win11 22H2+: the modern backdrop API (Mica / Acrylic) that cooperates with
        // rounded corners and the frame line, instead of the legacy accent policy.
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_AUTO = 0;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;      // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

        // DWMWCP_DEFAULT 0, DWMWCP_DONOTROUND 1, DWMWCP_ROUND 2, DWMWCP_ROUNDSMALL 3
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;

        // DWMWA_COLOR_NONE disables the DWM-drawn frame line entirely.
        private const int DWM_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int MONITOR_DEFAULTTONEAREST = 2;
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_DLGFRAME = 0x00400000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSIZEFRAME = 32;
        private const int SM_CXPADDEDBORDER = 92;

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        /// <summary>Win11 22H2 (build 22621) and later support the modern backdrop attributes.</summary>
        public static bool HasModernBackdrop => Environment.OSVersion.Version.Build >= 22621;

        /// <summary>
        /// Clip the HWND to the same rounded outline as the WPF shell. A non-layered WPF
        /// window has a rectangular redirection surface even if its XAML Border has rounded
        /// corners. Without a real HWND region, those untouched pixels appear as dark wedges
        /// outside the light theme at the four window corners.
        /// </summary>
        public static string LastRegion { get; private set; } = "not-applied";

        public static void ApplyRoundedRegion(Window window, double radius)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Maximize first: an HWND region must not clip the monitor work area. In the
            // system-backdrop path let DWM square the corners for a maximized window.
            if (IsZoomed(hwnd))
            {
                if (LastRegion == "rounded") SetWindowRgn(hwnd, IntPtr.Zero, true);
                if (HasModernBackdrop && LastBackdrop == "system-acrylic")
                {
                    DwmSet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
                    LastRegion = "dwm-square";
                }
                else LastRegion = SetWindowRgn(hwnd, IntPtr.Zero, true) != 0 ? "rect" : "failed";
                return;
            }

            // A manual region and DWMWCP_ROUND round different silhouettes. DWM draws the
            // acrylic and its shadow using ITS corner, so the region exposed an extra tinted
            // crescent outside the light theme. For a confirmed Windows 11 acrylic backdrop,
            // let DWM own the rounding and shadow as one shape. This also avoids disabling
            // system acrylic by changing the non-client rendering policy.
            if (HasModernBackdrop && LastBackdrop == "system-acrylic")
            {
                if (LastRegion == "rounded" || LastRegion == "rect")
                    SetWindowRgn(hwnd, IntPtr.Zero, true);
                LastRegion = "dwm";
                DwmSet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
                return;
            }

            if (!GetWindowRect(hwnd, out RECT r)) return;
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi == 0 ? 1.0 : dpi / 96.0;
            int rad = (int)Math.Round(radius * scale);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return;

            // Region coordinates are HWND-relative, right/bottom exclusive. The OS owns a
            // successful region; delete it only when SetWindowRgn fails.
            IntPtr rgn = CreateRoundRectRgn(0, 0, w, h, rad * 2, rad * 2);
            if (rgn == IntPtr.Zero) { LastRegion = "failed"; return; }
            if (SetWindowRgn(hwnd, rgn, true) == 0)
            {
                LastRegion = "failed";
                DeleteObject(rgn);
            }
            else LastRegion = "rounded";
        }

        /// <summary>
        /// Read-only frame diagnostic. Never change DWM attributes while logging: doing so
        /// used to set DWMSBT_NONE and silently turned the acrylic off after ApplyBackdrop.
        /// </summary>
        public static string DescribeFrame(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return "frame hwnd=none";

            int build = Environment.OSVersion.Version.Build;
            int backdrop = -1;
            try
            {
                if (HasModernBackdrop)
                    DwmGetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                        out backdrop, sizeof(int));
            }
            catch { /* unavailable on older Windows */ }

            int style = GetWindowLong(hwnd, GWL_STYLE);
            bool frameStyles = (style & (WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME)) != 0;
            var clear = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.BackgroundColor.ToString() ?? "unavailable";
            return $"frame build={build} dpi={GetDpiForWindow(hwnd)} backdrop={LastBackdrop} " +
                   $"backdropReadback={backdrop} targetClear={clear} frameExtend={LastFrameExtension} " +
                   $"blur={LastBlur} nativeFrameStyles={frameStyles} region={LastRegion} maximised={IsZoomed(hwnd)}";
        }

        /// <summary>Sets a DWORD DWM attribute, ignoring platforms that do not know it.</summary>
        private static bool DwmSet(IntPtr hwnd, int attr, int value)
        {
            try
            {
                return DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)) == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Applies real acrylic behind the window.
        ///
        /// Two things make this work, and the app was missing both:
        ///
        ///  1. The DWM frame is extended over the entire client area. Without it the client area is
        ///     an opaque island that Windows composites nothing behind, so the accent policy below
        ///     has nothing to blur - the window just renders its own tint and reads as a flat
        ///     navy/white rectangle, which is exactly what a "translucent" window looks like when
        ///     the backdrop never engages.
        ///  2. ACCENT_ENABLE_ACRYLICBLURBEHIND wants a tint with a *low* alpha. The alpha in
        ///     GradientColor is mixed into the blurred backdrop by the compositor, so passing
        ///     anything near opaque paints the blur out again.
        ///
        /// The visual tint the user perceives must therefore come from the XAML surface, and that
        /// surface has to be translucent - see RootBorder's 12% dark / 6% light fills.
        /// </summary>
        public static void ApplyAcrylic(Window window, Color tint, byte alpha, bool dark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // 1. DWM: dark frame, rounded corners, no frame line.
            int darkFlag = dark ? 1 : 0;
            DwmSet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, darkFlag);
            if (IsWindows11)
            {
                DwmSet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
                if (HasModernBackdrop)
                    DwmSet(hwnd, DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, 0);
                DwmSet(hwnd, DWMWA_BORDER_COLOR, DWM_COLOR_NONE);
            }

            // No system backdrop on this path: the layered window draws its own per-pixel alpha,
            // and the legacy accent policy frosts what is behind it.
            if (IsWindows11)
                DwmSet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);

            // 3. Blur-behind with a translucent tint. `alpha` is the *backdrop* alpha, kept far
            //    below the surface opacity so the frost reads through; the window's own tint is
            //    the XAML fill on top of it.
            byte backdropAlpha = (byte)Math.Clamp((int)alpha, 0, 255);
            uint gradient = ((uint)backdropAlpha << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
            var policy = new AccentPolicy
            {
                AccentState = HasModernBackdrop
                    ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
                    : AccentState.ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 2 | 0x20,
                GradientColor = gradient,
                AnimationId = 0
            };
            WriteAccent(hwnd, policy);
        }

        /// <summary>
        /// True when the OS can paint a real acrylic backdrop for us (Win11 22H2+, build 22621).
        /// When this is available the window must NOT be layered: DWM refuses to blur behind a
        /// WS_EX_LAYERED window, which is exactly why the app looked like a flat navy slab.
        /// </summary>
        public static bool SystemAcrylicAvailable
        {
            get
            {
                if (!HasModernBackdrop) return false;
                try { return !SystemParameters_HighContrast(); } catch { return true; }
            }
        }

        private static bool SystemParameters_HighContrast() =>
            System.Windows.SystemParameters.HighContrast;

        /// <summary>
        /// Applies the real Win11 acrylic material (DWMSBT_TRANSIENTWINDOW) and records the result.
        ///
        /// This is the path the app takes on Windows 11 22H2 and later. The window must be a normal
        /// (non-layered) window with a transparent WPF background; DWM then composites its acrylic
        /// material behind the client area, which is genuine blur of whatever is behind the window,
        /// not a painted colour. Rounded corners come from DWMWCP_ROUND, and the frame band stays
        /// gone because the client area still covers the whole window (WM_NCCALCSIZE hook).
        /// </summary>
        public static bool ApplySystemAcrylic(Window window, bool dark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            DwmSet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
            bool corners = DwmSet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            DwmSet(hwnd, DWMWA_BORDER_COLOR, DWM_COLOR_NONE);
            DwmSet(hwnd, DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, 0);

            // On a non-layered WPF HWND, extending the DWM frame under the WHOLE client
            // area makes the system acrylic material available behind WPF's content.
            // The previous attempt looked opaque because WPF's HwndTarget still cleared to
            // black; MainWindow now sets CompositionTarget.BackgroundColor=Transparent
            // BEFORE requesting this material. Avoid the 1-pixel margins that draw a DWM
            // line across the custom frame. DWM border colour remains explicitly NONE.
            var fullFrame = new MARGINS { Left = -1, Top = -1, Right = -1, Bottom = -1 };
            LastFrameExtension = DwmExtendFrameIntoClientArea(hwnd, ref fullFrame) == 0 ? "full" : "failed";
            var blur = new DWM_BLURBEHIND
            {
                dwFlags = DWM_BB_ENABLE,
                fEnable = true,
                hRgnBlur = IntPtr.Zero,     // whole window
                fTransitionOnMaximized = true
            };
            try { LastBlur = DwmEnableBlurBehindWindow(hwnd, ref blur) == 0 ? "enabled" : "failed"; }
            catch { LastBlur = "unavailable"; }

            bool acrylic = DwmSet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);

            // Read the material back: this distinguishes "we asked" from "Windows agreed", which is
            // the only honest way to report the backdrop state in frame.txt.
            int readback = -1;
            try { DwmGetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, out readback, sizeof(int)); } catch { }
            LastBackdrop = readback == DWMSBT_TRANSIENTWINDOW
                ? "system-acrylic"
                : acrylic ? "system-acrylic(unconfirmed)" : "none";
            return acrylic && corners;
        }

        /// <summary>Which backdrop actually engaged on this machine; written to frame.txt.</summary>
        public static string LastBackdrop { get; private set; } = "unknown";
        public static string LastFrameExtension { get; private set; } = "unknown";
        public static string LastBlur { get; private set; } = "unknown";

        /// <summary>
        /// Updates only the accent tint of the live backdrop, without touching the frame.
        /// Used when the user changes theme or accent colour while the window is open.
        /// </summary>
        public static void RetintAcrylic(Window window, Color tint, byte alpha)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            uint gradient = ((uint)alpha << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
            var policy = new AccentPolicy
            {
                AccentState = HasModernBackdrop
                    ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
                    : AccentState.ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 2 | 0x20,
                GradientColor = gradient,
                AnimationId = 0
            };
            WriteAccent(hwnd, policy);
        }

        private static void WriteAccent(IntPtr hwnd, AccentPolicy policy)
        {
            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        /// <summary>Native caption drag: keeps Win11 snap layouts, Aero snap and shake-to-minimize.</summary>
        public static void BeginDragMove(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        /// <summary>Wires the maximize/minimize clamping so a chrome-less window never covers the taskbar.</summary>
        public static void HookMaximizeFix(Window window)
        {
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var src = HwndSource.FromHwnd(hwnd);
                src?.AddHook(WndProc);
                StripNativeFrame(hwnd);
            };
        }

        /// <summary>
        /// Removes the native window frame for real.
        ///
        /// WindowStyle=None only removes the *theme* of the frame - the window is still created
        /// with WS_CAPTION | WS_THICKFRAME, so Windows reserves a non-client band around the
        /// client area and paints it in the system frame colour. On this machine that band was a
        /// solid white strip roughly 7px wide along the top and left of the window, which is the
        /// "thick border" the user reported.
        ///
        /// Answering WM_NCCALCSIZE alone is not enough: the band still exists because the styles
        /// are still there. Clearing the styles makes the client area cover the whole window, so
        /// there is no band left to paint. The resize behaviour is restored by the WM_NCHITTEST
        /// hook below, and the caption buttons are WPF controls that drag the window themselves.
        /// </summary>
        /// <summary>Reapply after WPF or DWM reasserts frame styles on activation/theme changes.</summary>
        public static void EnsureNativeFrameRemoved(Window window) =>
            StripNativeFrame(new WindowInteropHelper(window).Handle);

        private static void StripNativeFrame(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            int style = GetWindowLong(hwnd, GWL_STYLE);
            int stripped = style & ~(WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_DLGFRAME);
            // keep the bits the window still needs to behave: a menu is not needed, but the
            // min/max boxes are used by the accessibility tree, so they stay.
            stripped |= WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
            if (stripped != style)
            {
                SetWindowLong(hwnd, GWL_STYLE, stripped);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // ------------------------------------------------------------------
            // Kill the native non-client frame.
            //
            // A WindowStyle=None window is still created with WS_CAPTION, so Windows reserves
            // a thin non-client band around the whole client area: one border pixel plus, on
            // the top, the caption sliver. That band is drawn in the system frame colour, which
            // is the solid grey outline and the "slim extra bar above the title bar".
            // Answering WM_NCCALCSIZE with the proposed rectangle as the client rectangle makes
            // the client area cover the entire window, and WM_NCHITTEST below re-creates resize
            // edges inside it so the window still behaves like a real one.
            // ------------------------------------------------------------------
            if (msg == WM_NCCALCSIZE)
            {
                // With caption/frame styles removed there is no non-client border to reserve.
                // GETMINMAXINFO already uses the monitor work area when maximized. Do not
                // marshal lParam as WINDOWPOS here: it is NCCALCSIZE_PARAMS (or RECT when
                // wParam is zero), and writing WINDOWPOS over it corrupts the proposed rect.
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_NCHITTEST)
            {
                IntPtr res = HitTestResize(hwnd, lParam);
                if (res != IntPtr.Zero)
                {
                    handled = true;
                    return res;
                }
                return IntPtr.Zero;
            }

            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref mi))
                    {
                        mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                        mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                        mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                        mmi.ptMinTrackSize.X = 760;
                        mmi.ptMinTrackSize.Y = 560;
                    }
                }
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public int flags;
        }

        /// <summary>Gives the chrome-less window a resize grip on all eight edges.</summary>
        private static IntPtr HitTestResize(IntPtr hwnd, IntPtr lParam)
        {
            if (IsZoomed(hwnd) || !GetWindowRect(hwnd, out RECT w)) return IntPtr.Zero;

            int x = unchecked((short)(long)lParam);
            int y = unchecked((short)((long)lParam >> 16));

            int frame = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            if (frame < 4) frame = 4;

            bool left = x >= w.Left && x < w.Left + frame;
            bool right = x < w.Right && x >= w.Right - frame;
            bool top = y >= w.Top && y < w.Top + frame;
            bool bottom = y < w.Bottom && y >= w.Bottom - frame;

            if (top && left) return (IntPtr)HTTOPLEFT;
            if (top && right) return (IntPtr)HTTOPRIGHT;
            if (bottom && left) return (IntPtr)HTBOTTOMLEFT;
            if (bottom && right) return (IntPtr)HTBOTTOMRIGHT;
            if (left) return (IntPtr)HTLEFT;
            if (right) return (IntPtr)HTRIGHT;
            if (top) return (IntPtr)HTTOP;
            if (bottom) return (IntPtr)HTBOTTOM;
            return IntPtr.Zero;
        }
    }
}
