using System.Text.Json;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Power;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class ConfigurationStoreTests
{
    [TestMethod]
    public void MissingConfiguration_CreatesValidatedDefaults()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");

        ConfigurationStore store = new(configPath);

        Assert.IsTrue(File.Exists(configPath));
        Assert.AreEqual(1, store.Current.SchemaVersion);
        Assert.IsTrue(store.Current.AutoTdpEnabled);
        Assert.AreEqual(60, store.Current.ProfileDefaults.TargetFps);
        Assert.AreEqual(6, store.Current.PowerLimits.BatteryMinTdp);
        Assert.AreEqual(25, store.Current.PowerLimits.BatteryMaxTdp);
        Assert.AreEqual(6, store.Current.PowerLimits.AcMinTdp);
        Assert.AreEqual(30, store.Current.PowerLimits.AcMaxTdp);
        Assert.AreEqual(RestoreMode.Balanced, store.Current.ProfileDefaults.RestoreMode);
    }

    [TestMethod]
    public void SaveAndReload_PreservesTypedProfileAndEnum()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");
        ConfigurationStore store = new(configPath);
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        store.Current.GameProfiles.Add(new GameProfile
        {
            ExecutablePath = executablePath,
            DisplayName = "Example",
            Enabled = true,
            TargetFps = 90,
            RestoreMode = RestoreMode.Turbo,
        });

        store.Save();
        ConfigurationStore reloaded = new(configPath);

        GameProfile profile = reloaded.Current.GameProfiles.Single();
        Assert.AreEqual(
            ExecutablePathIdentity.Normalize(executablePath),
            profile.ExecutablePath);
        Assert.AreEqual(90, profile.TargetFps);
        Assert.AreEqual(RestoreMode.Turbo, profile.RestoreMode);
        StringAssert.Contains(File.ReadAllText(configPath), "\"RestoreMode\": \"Turbo\"");
        Assert.IsTrue(File.Exists(store.BackupPath));
    }

    [DataTestMethod]
    [DataRow(5, 25)]
    [DataRow(6, 26)]
    public void InvalidBatteryRange_IsRejected(int minimum, int maximum)
    {
        AllyAutoTdpConfiguration configuration = new();
        configuration.PowerLimits.BatteryMinTdp = minimum;
        configuration.PowerLimits.BatteryMaxTdp = maximum;

        Assert.ThrowsException<ConfigurationValidationException>(() =>
            GameConfigurationValidator.NormalizeAndValidate(configuration));
    }

    [TestMethod]
    public void InvalidAcMaximum_IsRejected()
    {
        AllyAutoTdpConfiguration configuration = new();
        configuration.PowerLimits.AcMaxTdp = 31;

        Assert.ThrowsException<ConfigurationValidationException>(() =>
            GameConfigurationValidator.NormalizeAndValidate(configuration));
    }

    [TestMethod]
    public void InvalidTargetFps_IsRejected()
    {
        AllyAutoTdpConfiguration configuration = new();
        configuration.ProfileDefaults.TargetFps = 55;

        Assert.ThrowsException<ConfigurationValidationException>(() =>
            GameConfigurationValidator.NormalizeAndValidate(configuration));
    }

    [TestMethod]
    public void MalformedPrimary_IsNotSilentlyOverwritten()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");
        const string malformedJson = "{ malformed configuration";
        File.WriteAllText(configPath, malformedJson);

        Assert.ThrowsException<ConfigurationLoadException>(() =>
            new ConfigurationStore(configPath));
        Assert.AreEqual(malformedJson, File.ReadAllText(configPath));
    }

    [TestMethod]
    public void ValidBackup_IsUsedWhenPrimaryIsMalformed()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");
        string backupPath = configPath + ".bak";
        string sourcePath = Path.Combine(temp.Path, "source.json");
        ConfigurationStore source = new(sourcePath);
        File.Copy(source.ConfigPath, backupPath);
        const string malformedJson = "not json";
        File.WriteAllText(configPath, malformedJson);

        ConfigurationStore recovered = new(configPath);

        Assert.IsFalse(string.IsNullOrWhiteSpace(recovered.LastLoadWarning));
        Assert.AreEqual(60, recovered.Current.ProfileDefaults.TargetFps);
        Assert.AreEqual(malformedJson, File.ReadAllText(configPath));
    }

    [TestMethod]
    public void LegacyConfiguration_MigratesPowerPolicyAndDropsProfileTdp()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");
        string executablePath = Path.Combine(temp.Path, "Example.exe");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "AutoTdpEnabled": true,
              "Defaults": {
                "TargetFps": 45,
                "BatteryMinTdp": 7,
                "BatteryMaxTdp": 20,
                "AcMinTdp": 8,
                "AcMaxTdp": 29,
                "RestoreMode": "Turbo"
              },
              "GameProfiles": [{
                "ExecutablePath": "{{executablePath.Replace("\\", "\\\\")}}",
                "DisplayName": "Example",
                "Enabled": true,
                "TargetFps": 60,
                "BatteryMinTdp": 10,
                "BatteryMaxTdp": 18,
                "AcMinTdp": 10,
                "AcMaxTdp": 25,
                "RestoreMode": "Balanced"
              }]
            }
            """
        );

        ConfigurationStore store = new(configPath);

        Assert.AreEqual(7, store.Current.PowerLimits.BatteryMinTdp);
        Assert.AreEqual(20, store.Current.PowerLimits.BatteryMaxTdp);
        Assert.AreEqual(8, store.Current.PowerLimits.AcMinTdp);
        Assert.AreEqual(29, store.Current.PowerLimits.AcMaxTdp);
        Assert.AreEqual(45, store.Current.ProfileDefaults.TargetFps);
        Assert.AreEqual(RestoreMode.Turbo, store.Current.ProfileDefaults.RestoreMode);
        Assert.AreEqual(1, store.Current.GameProfiles.Count);
        using JsonDocument normalized = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement profileJson = normalized.RootElement
            .GetProperty("GameProfiles")[0];
        Assert.IsFalse(profileJson.TryGetProperty("BatteryMinTdp", out _));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = Directory.CreateTempSubdirectory("AllyAutoTdpTests_").FullName;
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
