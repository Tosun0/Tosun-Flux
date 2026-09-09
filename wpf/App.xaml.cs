using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TosunConverter.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID("Tosun.TosunFlux");
        ApplyColorProfile(IsSystemDarkMode());
        base.OnStartup(e);
    }

    private void ApplyColorProfile(bool dark)
    {
        var profile = dark
            ? new Dictionary<string, string>
            {
                ["TextBrush"] = "#FFF5F7FF",
                ["MutedBrush"] = "#B8C3CAE2",
                ["GlassBrush"] = "#40070A12",
                ["StrongGlassBrush"] = "#680B1020",
                ["FieldBrush"] = "#680B1020",
                ["PopupBrush"] = "#E80B1020",
                ["RootOverlayBrush"] = "#18000000",
                ["DropSurfaceBrush"] = "#38070A12",
                ["SecondaryBrush"] = "#50121929",
                ["SecondaryTextBrush"] = "#FFE5E9F7",
                ["ArrowBrush"] = "#FFD8DEF2",
                ["HoverBrush"] = "#707D8FE9",
                ["SelectedBrush"] = "#987D8FE9",
                ["ProgressTrackBrush"] = "#48000000",
                ["LogBrush"] = "#60070A12",
                ["ScrollThumbBrush"] = "#88798CE1",
                ["AccentBrush"] = "#FF7888E8",
            }
            : new Dictionary<string, string>
            {
                ["TextBrush"] = "#FF202943",
                ["MutedBrush"] = "#B85C6680",
                ["GlassBrush"] = "#40FFFFFF",
                ["StrongGlassBrush"] = "#68FFFFFF",
                ["FieldBrush"] = "#68FFFFFF",
                ["PopupBrush"] = "#ECF6F8FF",
                ["RootOverlayBrush"] = "#10FFFFFF",
                ["DropSurfaceBrush"] = "#38FFFFFF",
                ["SecondaryBrush"] = "#50FFFFFF",
                ["SecondaryTextBrush"] = "#FF46516A",
                ["ArrowBrush"] = "#FF59647D",
                ["HoverBrush"] = "#707D8FE9",
                ["SelectedBrush"] = "#987D8FE9",
                ["ProgressTrackBrush"] = "#30000000",
                ["LogBrush"] = "#60FFFFFF",
                ["ScrollThumbBrush"] = "#88798CE1",
                ["AccentBrush"] = "#FF7888E8",
            };

        foreach (var (key, value) in profile)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
            brush.Freeze();
            Resources[key] = brush;
        }
    }

    internal static bool IsSystemDarkMode()
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (personalize?.GetValue("AppsUseLightTheme") is int appTheme)
            return appTheme == 0;

        return personalize?.GetValue("SystemUsesLightTheme") is int systemTheme && systemTheme == 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
