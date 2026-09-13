using AllyAutoTDP.Application;
using AllyAutoTDP.Configuration;

namespace AllyAutoTDP.UI;

internal sealed class ProfileWorkflow
{
    private readonly AllyApplicationController _controller;

    public ProfileWorkflow(AllyApplicationController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public GameProfile? BrowseForExecutable(IWin32Window? owner)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Ajouter un profil de jeu",
            Filter = "Fichiers exécutables (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        string executablePath = dialog.FileName;
        GameProfile? profile = FindProfile(
            _controller.GetSnapshot(),
            executablePath);
        if (profile is not null)
            return profile;

        try
        {
            return _controller.AddManualProfile(executablePath);
        }
        catch (Exception exception)
        {
            ShowError(owner, exception);
            return null;
        }
    }

    private static GameProfile? FindProfile(
        AllyApplicationSnapshot snapshot,
        string executablePath)
    {
        if (!ExecutablePathIdentity.TryNormalize(
                executablePath,
                out string normalizedPath,
                out _))
        {
            return null;
        }

        return snapshot.GameProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.ExecutablePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void ShowError(
        IWin32Window? owner,
        Exception exception) =>
        MessageBox.Show(
            owner,
            exception.Message,
            "Erreur AutoTDP",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
}
