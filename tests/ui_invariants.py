#!/usr/bin/env python3
"""Regression checks for the real-Windows border/seam/disabled-button fixes."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1] / 'KnightWin.Randomizer'
xaml = (root/'MainWindow.xaml').read_text()
styles = (root/'Theme/Styles.xaml').read_text()
fx = (root/'Interop/WindowFx.cs').read_text()
theme = (root/'Theme/ThemeManager.cs').read_text()
checks = {
    'No padded acrylic ScrollViewer content boundary':
        'VerticalScrollBarVisibility="Auto" Padding="4,0,4,0"' not in xaml and
        'VerticalScrollBarVisibility="Auto" Padding="4,0,12,10"' not in xaml and
        'VerticalScrollBarVisibility="Auto" Padding="0"' in xaml and
        'StackPanel Margin="4,0,4,0"' in xaml and
        'StackPanel Margin="4,0,12,10"' in xaml,
    'One translucent page base and no full-window chrome overpaint':
        re.search(r'x:Name="PageSurface"[^>]+Background="Transparent"', xaml, re.S) is not None and
        'x:Name="RootBorder"' in xaml and
        re.search(r'x:Name="PageSettings"[^>]+Background="Transparent"', xaml, re.S) is not None and
        'Width="{Binding ActualWidth, ElementName=NavPane}"' in xaml and
        'Grid.RowSpan="2" HorizontalAlignment="Stretch"' not in xaml,
    'Requested dark and light alpha values and one percent accent':
        all(v in theme for v in ('"#E0171719"','"#F2F4F4F6"','"#AB121214"','"#9BFFFFFF"')) and
        'Color.FromArgb(3, tint.R, tint.G, tint.B)' in theme,
    'Translucent readable card surfaces':
        'B("CardBg",          "#55545457")' in theme and
        'B("CardBg",          "#3DFFFFFF")' in theme,
    'Transparent WPF target and full-client acrylic requested':
        'source.CompositionTarget.BackgroundColor = Colors.Transparent;' in (root/'MainWindow.xaml.cs').read_text() and
        'DwmExtendFrameIntoClientArea(hwnd, ref fullFrame)' in fx and
        'Left = -1, Top = -1, Right = -1, Bottom = -1' in fx,
    'Composition diagnostics visible in Settings About':
        'x:Name="AcrylicDiagnostic"' in xaml and
        'AcrylicDiagnostic.Text = WindowFx.DescribeFrame(this);' in (root/'MainWindow.xaml.cs').read_text() and
        'targetClear={clear}' in fx and 'frameExtend={LastFrameExtension}' in fx,
    'No paint-only ScrollViewer border lines':
        'No paint-only seals' in xaml and
        'No overpainting the scroll edges' in xaml and
        'HorizontalAlignment="Left" IsHitTestVisible="False"' not in xaml and
        'Height="2" Margin="0,3,0,0"' not in xaml,
    'DWM owns acrylic corners without overlapping manual region':
        'if (HasModernBackdrop && LastBackdrop == "system-acrylic")' in fx and
        'LastRegion = "dwm";' in fx and
        'DWMWCP_DONOTROUND' in fx,
    'DWM outer corners and single rounded chrome junction':
        re.search(r'x:Name="RootBorder"[^>]+CornerRadius="0"', xaml, re.S) is not None and
        'x:Name="ChromeFull" Grid.RowSpan="2"' in xaml and
        'InsideRadius="12"' in xaml and
        'ChromeBrush="{DynamicResource ChromeBg}"' in xaml and
        'AccentBrush="{DynamicResource ChromeTint}"' in xaml and
        'path.BezierTo(' in (root/'Controls/ChromeSurface.cs').read_text(),
    'PC sun and filled crescent App mode icons':
        all(f'GeoAppMode{glyph}' in styles and f'GeoAppMode{glyph}' in xaml for glyph in ('Pc','Sun','Moon')) and
        'Fill="{DynamicResource TextSecondary}"' in xaml,
    'Slim horizontal app-mode tiles and tighter page header inset':
        'Padding="12,9"' in styles and 'MinHeight="46"' in styles and
        xaml.count('<StackPanel Orientation="Horizontal" VerticalAlignment="Center">') >= 3 and
        re.search(r'<Grid Grid.Column="1">\s*<Grid.RowDefinitions>[\s\S]*?<RowDefinition Height="16"', xaml) is not None,
    'Title brand left, toggle in rail, headings closer to chrome':
        xaml.index('<TextBlock x:Name="NavHeader"') > xaml.index('<Button x:Name="NavToggleBtn"') > xaml.index('<Border x:Name="NavPane"') and
        xaml.index('<Border Width="20" Height="20" CornerRadius="5"') < xaml.index('<!-- Borderless, edge-aligned Windows-style caption targets.') and
        xaml.count('x:Name="NavToggleBtn"') == 1 and
        re.search(r'<Grid x:Name="TitleBar"[\s\S]*?<StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="6,0,0,0">\s*<Border Width="20"', xaml) is not None and
        'InsideRadius="12"' in xaml and 'TitleHeight="40"' in xaml,
    'Pane glyph stays three-line menu and caption targets are borderless':
        'FindResource("GeoMenu")' in (root/'MainWindow.xaml.cs').read_text() and
        'collapsed ? "GeoChevronRight" : "GeoMenu"' not in (root/'MainWindow.xaml.cs').read_text() and
        'TitleHeight="40" InsideRadius="12"' in xaml and
        re.search(r'<Grid.RowDefinitions>\s*<RowDefinition Height="40" />\s*<RowDefinition Height="\*" />', xaml) is not None and
        styles.count('CornerRadius="0" BorderThickness="0" Background=') == 2 and
        '<Setter Property="Height" Value="40" />' in styles and
        'VerticalAlignment="Center" Margin="0,0,6,0"' not in xaml,
    'Live About RAM chip switches MB and percent of physical RAM':
        'MouseLeftButtonDown="MemoryChip_MouseLeftButtonDown"' in xaml and
        'GlobalMemoryStatusEx(ref MemoryStatusEx status)' in (root/'MainWindow.xaml.cs').read_text() and
        'WorkingSet64' in (root/'MainWindow.xaml.cs').read_text() and
        'e.ClickCount != 2' in (root/'MainWindow.xaml.cs').read_text() and
        '_memoryAsPercent = !_memoryAsPercent;' in (root/'MainWindow.xaml.cs').read_text() and
        '_ramTimer.Tick += (_, _) => UpdateMemoryLine();' in (root/'MainWindow.xaml.cs').read_text() and
        '_ramTimer.Start();' in (root/'MainWindow.xaml.cs').read_text() and
        '_ramTimer.Stop();' in (root/'MainWindow.xaml.cs').read_text(),
    'Slow accent glow moves on preview card':
        'x:Name="PreviewGlow"' in xaml and '<RadialGradientBrush Center=' in xaml and
        'Color="{DynamicResource AccentSoftColor}"' in xaml and
        'Duration = new Duration(TimeSpan.FromSeconds(22))' in (root/'MainWindow.xaml.cs').read_text() and
        'glow.BeginAnimation(RadialGradientBrush.CenterProperty, path)' in (root/'MainWindow.xaml.cs').read_text() and
        'glow.BeginAnimation(RadialGradientBrush.GradientOriginProperty, path)' in (root/'MainWindow.xaml.cs').read_text(),
    'Wheel vector cache reuses frozen drawing across spin frames':
        'private DrawingGroup? _wheelArt;' in (root/'Controls/WheelControl.cs').read_text() and
        'if (_wheelArt != null && _artWidth == ActualWidth' in (root/'Controls/WheelControl.cs').read_text() and
        'art.Freeze();' in (root/'Controls/WheelControl.cs').read_text() and
        'dc.DrawDrawing(_wheelArt);' in (root/'Controls/WheelControl.cs').read_text(),
    'About logo opens official GitHub repository with keyboard support':
        'Click="AboutLogo_Click"' in xaml and
        'https://github.com/iamkrisstan2000/KnightWin-Randomizer' in (root/'MainWindow.xaml.cs').read_text() and
        'UseShellExecute = true' in (root/'MainWindow.xaml.cs').read_text() and
        'Keyboard.FocusedElement is not ButtonBase' in (root/'MainWindow.xaml.cs').read_text(),
    'Wheel click removed while rename spin remains':
        'MouseLeftButtonDown="Wheel_Click"' not in xaml and
        'private void Wheel_Click' not in (root/'MainWindow.xaml.cs').read_text() and
        'x:Name="Wheel" Width="30" Height="30"\n                                                       IsHitTestVisible="False"' in xaml and
        'Wheel.StartBusySpin(1.1);' in (root/'MainWindow.xaml.cs').read_text(),
    'Queue footer only has action buttons and status is in header':
        'x:Name="StatusText"' not in xaml and 'x:Name="StatusSub"' not in xaml and
        'QueueSub.Text = text;' in (root/'MainWindow.xaml.cs').read_text() and
        'if (importFeedback != null) Status(importFeedback, importContext);' in (root/'MainWindow.xaml.cs').read_text(),
    'Red Orange and adaptive Black/White accents':
        all(f'Key = "{key}"' in theme for key in ('Red','Orange','Monochrome')) and
        'Label = dark ? "White" : "Black"' in theme and
        'CurrentIsDark = dark;\n            var acc = ResolveAccent(accentKey);' in theme and
        'ThemeManager.ResolveAccent(item.Key)' in (root/'MainWindow.xaml.cs').read_text() and
        'mw.RefreshAccentChoices();' in (root/'App.xaml.cs').read_text(),
    'Official icon centers visible knight artwork, not transparent canvas':
        "visible = logo.getchannel('A').point(" in (root/'Resources/make_icon.py').read_text() and
        'logo_x = round(C - (left + right) / 2)' in (root/'Resources/make_icon.py').read_text() and
        'logo_y = round(C - (top + bottom) / 2)' in (root/'Resources/make_icon.py').read_text(),
    'Wheel and knight official icon deployed consistently':
        (root/'Assets/app-icon.png').is_file() and (root/'Resources/app.ico').is_file() and
        xaml.count('Source="Assets/app-icon.png"') == 3 and
        xaml.count('Source="Assets/logo.png"') == 1 and
        'ApplicationIcon>Resources\\app.ico' in (root/'KnightWinRandomizer.csproj').read_text(),
    'Window corners are clipped by an HWND region':
        'CreateRoundRectRgn(0, 0, w, h, rad * 2, rad * 2)' in fx and
        'SetWindowRgn(hwnd, rgn, true)' in fx and
        'region={LastRegion}' in fx,
    'PageSurface does not have an extra rail-width inset':
        not re.search(r'<Border[^>]*x:Name="PageSurface"[^>]*Margin=', xaml, re.S),
    'No faded disabled button background':
        'TargetName="Bd" Property="Opacity" Value="0.38"' not in styles and
        'TargetName="Bd" Property="Opacity" Value="0.4"' not in styles,
    'Disabled buttons have opaque fill':
        styles.count('TargetName="Bd" Property="Background" Value="{DynamicResource DisabledButtonBg}"') == 2,
    'Native frame style removal on source init':
        'StripNativeFrame(hwnd);' in fx and 'SWP_FRAMECHANGED' in fx,
    'Frame diagnostics are read-only':
        'DwmSet(' not in fx.split('public static string DescribeFrame(')[1].split('/// <summary>Sets a DWORD DWM attribute')[0],
    'No footer/nav decorative rule':
        'BorderThickness="0,1,0,0"' not in xaml and 'x:Name="NavBottomRule" Height="0"' in xaml,
}
for label, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + '  ' + label)
raise SystemExit(0 if all(checks.values()) else 1)
