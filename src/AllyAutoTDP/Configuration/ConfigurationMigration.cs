using System.Text.Json;
using System.Text.Json.Serialization;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Configuration;

internal static class ConfigurationMigration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool TryMigrate(
        string json,
        out AllyAutoTdpConfiguration? configuration,
        out string? warning)
    {
        ArgumentNullException.ThrowIfNull(json);

        configuration = null;
        warning = null;

        using JsonDocument document = JsonDocument.Parse(json);
        if (!HasProperty(document.RootElement, "Defaults") ||
            HasProperty(document.RootElement, "PowerLimits") ||
            HasProperty(document.RootElement, "ProfileDefaults"))
        {
            return false;
        }

        LegacyConfiguration legacy = JsonSerializer.Deserialize<LegacyConfiguration>(
                json,
                SerializerOptions)
            ?? throw new ConfigurationValidationException(
                "Legacy configuration is empty.");

        if (legacy.SchemaVersion != ConfigurationPolicy.CurrentSchemaVersion)
        {
            throw new ConfigurationValidationException(
                $"Unsupported configuration schema version " +
                $"{legacy.SchemaVersion}.");
        }

        if (legacy.Defaults is null)
        {
            throw new ConfigurationValidationException(
                "Legacy configuration Defaults are required.");
        }

        configuration = new AllyAutoTdpConfiguration
        {
            SchemaVersion = ConfigurationPolicy.CurrentSchemaVersion,
            AutoTdpEnabled = legacy.AutoTdpEnabled,
            StartWithWindows = false,
            PowerLimits = new PowerLimits
            {
                BatteryMinTdp = legacy.Defaults.BatteryMinTdp,
                BatteryMaxTdp = legacy.Defaults.BatteryMaxTdp,
                AcMinTdp = legacy.Defaults.AcMinTdp,
                AcMaxTdp = legacy.Defaults.AcMaxTdp,
            },
            ProfileDefaults = new ProfileDefaults
            {
                TargetFps = legacy.Defaults.TargetFps,
                RestoreMode = legacy.Defaults.RestoreMode,
            },
            GameProfiles = (legacy.GameProfiles ?? []).Select(profile => new GameProfile
            {
                ExecutablePath = profile.ExecutablePath,
                DisplayName = profile.DisplayName,
                Enabled = profile.Enabled,
                TargetFps = profile.TargetFps,
                RestoreMode = profile.RestoreMode,
            }).ToList(),
        };

        warning =
            "Legacy configuration migrated: per-profile TDP limits were " +
            "discarded and replaced by the general PowerLimits policy.";
        return true;
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class LegacyConfiguration
    {
        public int SchemaVersion { get; set; }

        public bool AutoTdpEnabled { get; set; } = true;

        public LegacyDefaults? Defaults { get; set; }

        public List<LegacyGameProfile>? GameProfiles { get; set; }
    }

    private sealed class LegacyDefaults
    {
        public int TargetFps { get; set; } = 60;

        public int BatteryMinTdp { get; set; } = TdpLimits.MinimumWatts;

        public int BatteryMaxTdp { get; set; } = TdpLimits.BatterySafetyMaxWatts;

        public int AcMinTdp { get; set; } = TdpLimits.MinimumWatts;

        public int AcMaxTdp { get; set; } = TdpLimits.AcSafetyMaxWatts;

        public RestoreMode RestoreMode { get; set; } = RestoreMode.Balanced;
    }

    private sealed class LegacyGameProfile
    {
        public string ExecutablePath { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public int TargetFps { get; set; }

        public RestoreMode RestoreMode { get; set; }
    }
}
