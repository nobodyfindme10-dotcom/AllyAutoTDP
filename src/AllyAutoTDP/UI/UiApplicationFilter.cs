using AllyAutoTDP.Windows;

namespace AllyAutoTDP.UI;

internal static class UiApplicationFilter
{
    private static readonly HashSet<string> GenericApplicationNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe",
            "discord.exe",
            "armourycrate.exe",
            "armourycrate.se.exe",
            "armourycratese.exe",
            "applicationframehost.exe",
            "dwm.exe",
            "powershell.exe",
            "pwsh.exe",
            "cmd.exe",
            "conhost.exe",
            "windowsterminal.exe",
            "searchhost.exe",
            "startmenuexperiencehost.exe",
            "shellexperiencehost.exe",
            "textinputhost.exe",
            "runtimebroker.exe",
            "taskmgr.exe",
            "services.exe",
            "svchost.exe"
        };

    public static bool IsRelevant(ForegroundApplicationSnapshot application)
    {
        ArgumentNullException.ThrowIfNull(application);

        string fileName = application.FileName.Trim();
        if (GenericApplicationNames.Contains(fileName))
            return false;

        string windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        return string.IsNullOrWhiteSpace(windowsDirectory) ||
            !application.ExecutablePath.StartsWith(
                windowsDirectory,
                StringComparison.OrdinalIgnoreCase);
    }
}
