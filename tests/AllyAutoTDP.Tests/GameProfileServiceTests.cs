using AllyAutoTDP.Configuration;
using AllyAutoTDP.Power;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class GameProfileServiceTests
{
    [TestMethod]
    public void CreateFromExecutablePath_UsesDefaultsAndPersistsProfile()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        File.WriteAllText(executablePath, string.Empty);
        ConfigurationStore store = new(configPath);
        GameProfileService service = new(store);

        GameProfile profile = service.CreateFromExecutablePath(executablePath);

        Assert.AreEqual(
            ExecutablePathIdentity.Normalize(executablePath),
            profile.ExecutablePath);
        Assert.AreEqual("Example", profile.DisplayName);
        Assert.AreEqual(store.Current.ProfileDefaults.TargetFps, profile.TargetFps);
        Assert.AreEqual(store.Current.ProfileDefaults.RestoreMode, profile.RestoreMode);
        Assert.IsTrue(profile.Enabled);
        Assert.IsNotNull(new ConfigurationStore(configPath).Current.GameProfiles.Single());
    }

    [TestMethod]
    public void DuplicatePath_IsRejectedCaseInsensitively()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        File.WriteAllText(executablePath, string.Empty);
        GameProfileService service = new(new ConfigurationStore(
            Path.Combine(temp.Path, "config.json")));
        service.CreateFromExecutablePath(executablePath);

        Assert.ThrowsException<ConfigurationValidationException>(() =>
            service.CreateFromExecutablePath(executablePath.ToUpperInvariant()));
    }

    [TestMethod]
    public void Lookup_UsesNormalizedCaseInsensitiveExecutableIdentity()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        File.WriteAllText(executablePath, string.Empty);
        GameProfileService service = new(new ConfigurationStore(
            Path.Combine(temp.Path, "config.json")));
        GameProfile expected = service.CreateFromExecutablePath(executablePath);

        GameProfile? actual = service.FindByExecutablePath(
            executablePath.ToUpperInvariant());

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public void CreateFromForeground_UsesSnapshotDisplayNameAndPath()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        File.WriteAllText(executablePath, string.Empty);
        GameProfileService service = new(new ConfigurationStore(
            Path.Combine(temp.Path, "config.json")));
        ForegroundApplicationSnapshot snapshot = new(
            1234,
            DateTimeOffset.UtcNow,
            executablePath,
            "Example.exe",
            "Example Readable Name");

        GameProfile profile = service.CreateFromForeground(snapshot);

        Assert.AreEqual("Example Readable Name", profile.DisplayName);
        Assert.AreEqual(
            ExecutablePathIdentity.Normalize(executablePath),
            profile.ExecutablePath);
    }

    [TestMethod]
    public void UnknownExecutable_HasNoProfile()
    {
        using TemporaryDirectory temp = new();
        GameProfileService service = new(new ConfigurationStore(
            Path.Combine(temp.Path, "config.json")));
        string executablePath = Path.Combine(temp.Path, "Unknown.exe");

        Assert.IsNull(service.FindByExecutablePath(executablePath));
    }

    [TestMethod]
    public void Update_PersistsOnlyProfileSettings()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        File.WriteAllText(executablePath, string.Empty);
        GameProfileService service = new(new ConfigurationStore(
            Path.Combine(temp.Path, "config.json")));
        GameProfile profile = service.CreateFromExecutablePath(executablePath);

        profile.DisplayName = "Updated";
        profile.Enabled = false;
        profile.TargetFps = 90;
        profile.RestoreMode = RestoreMode.Turbo;
        service.Update(profile.ExecutablePath, profile);

        GameProfile saved = service.FindByExecutablePath(executablePath)!;
        Assert.AreEqual("Updated", saved.DisplayName);
        Assert.IsFalse(saved.Enabled);
        Assert.AreEqual(90, saved.TargetFps);
        Assert.AreEqual(RestoreMode.Turbo, saved.RestoreMode);
    }
}
