using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Configuration;

public static class ConfigurationPolicy
{
    public const int CurrentSchemaVersion = 1;

    public static bool IsAcceptedTargetFps(int targetFps) =>
        AutoTdpOptions.IsAcceptedTargetFps(targetFps);

    public static void ValidateBatteryRange(int minTdp, int maxTdp) =>
        ValidateRange(
            minTdp,
            maxTdp,
            TdpLimits.BatterySafetyMaxWatts,
            "battery");

    public static void ValidateAcRange(int minTdp, int maxTdp) =>
        ValidateRange(
            minTdp,
            maxTdp,
            TdpLimits.AcSafetyMaxWatts,
            "AC");

    private static void ValidateRange(
        int minTdp,
        int maxTdp,
        int maximum,
        string source)
    {
        if (minTdp < TdpLimits.MinimumWatts || maxTdp > maximum || minTdp > maxTdp)
        {
            throw new ConfigurationValidationException(
                $"Invalid {source} TDP range. Expected " +
                $"{TdpLimits.MinimumWatts} <= min <= max <= {maximum} W.");
        }
    }
}

public sealed class AllyAutoTdpConfiguration
{
    public int SchemaVersion { get; set; } = ConfigurationPolicy.CurrentSchemaVersion;

    public bool AutoTdpEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public PowerLimits PowerLimits { get; set; } = PowerLimits.Create();

    public ProfileDefaults ProfileDefaults { get; set; } = ProfileDefaults.Create();

    public List<GameProfile> GameProfiles { get; set; } = [];
}

public sealed class PowerLimits
{
    public int BatteryMinTdp { get; set; } = TdpLimits.MinimumWatts;

    public int BatteryMaxTdp { get; set; } = TdpLimits.BatterySafetyMaxWatts;

    public int AcMinTdp { get; set; } = TdpLimits.MinimumWatts;

    public int AcMaxTdp { get; set; } = TdpLimits.AcSafetyMaxWatts;

    public static PowerLimits Create() => new();

    public TdpRange GetEffectiveRange(PowerSourceKind source) => source switch
    {
        PowerSourceKind.Ac => new(AcMinTdp, AcMaxTdp),
        _ => new(BatteryMinTdp, BatteryMaxTdp),
    };
}

public sealed class ProfileDefaults
{
    public int TargetFps { get; set; } = 60;

    public RestoreMode RestoreMode { get; set; } = RestoreMode.Balanced;

    public static ProfileDefaults Create() => new();
}

public sealed class GameProfile
{
    public string ExecutablePath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int TargetFps { get; set; }

    public RestoreMode RestoreMode { get; set; }
}

public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(string message)
        : base(message)
    {
    }

    public ConfigurationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class GameConfigurationValidator
{
    public static void NormalizeAndValidate(AllyAutoTdpConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.SchemaVersion != ConfigurationPolicy.CurrentSchemaVersion)
        {
            throw new ConfigurationValidationException(
                $"Unsupported configuration schema version " +
                $"{configuration.SchemaVersion}.");
        }

        if (configuration.PowerLimits is null)
            throw new ConfigurationValidationException("Configuration PowerLimits are required.");

        ValidatePowerLimits(configuration.PowerLimits);

        if (configuration.ProfileDefaults is null)
        {
            throw new ConfigurationValidationException(
                "Configuration ProfileDefaults are required.");
        }

        ValidateProfileDefaults(configuration.ProfileDefaults);

        if (configuration.GameProfiles is null)
        {
            throw new ConfigurationValidationException(
                "Configuration GameProfiles are required.");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameProfile profile in configuration.GameProfiles)
        {
            if (profile is null)
                throw new ConfigurationValidationException("GameProfiles cannot contain null entries.");

            profile.ExecutablePath = ExecutablePathIdentity.Normalize(profile.ExecutablePath);
            if (!paths.Add(profile.ExecutablePath))
            {
                throw new ConfigurationValidationException(
                    $"Duplicate game profile path '{profile.ExecutablePath}'.");
            }

            if (string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                throw new ConfigurationValidationException(
                    $"Game profile '{profile.ExecutablePath}' requires DisplayName.");
            }

            ValidateProfile(profile);
        }
    }

    private static void ValidatePowerLimits(PowerLimits powerLimits)
    {
        ConfigurationPolicy.ValidateBatteryRange(
            powerLimits.BatteryMinTdp,
            powerLimits.BatteryMaxTdp);
        ConfigurationPolicy.ValidateAcRange(
            powerLimits.AcMinTdp,
            powerLimits.AcMaxTdp);
    }

    private static void ValidateProfileDefaults(ProfileDefaults defaults)
    {
        if (!ConfigurationPolicy.IsAcceptedTargetFps(defaults.TargetFps))
        {
            throw new ConfigurationValidationException(
                $"Unsupported default target FPS '{defaults.TargetFps}'.");
        }

        ValidateRestoreMode(defaults.RestoreMode, "default profile");
    }

    private static void ValidateProfile(GameProfile profile)
    {
        if (!ConfigurationPolicy.IsAcceptedTargetFps(profile.TargetFps))
        {
            throw new ConfigurationValidationException(
                $"Unsupported target FPS '{profile.TargetFps}' for " +
                $"'{profile.ExecutablePath}'.");
        }

        ValidateRestoreMode(profile.RestoreMode, profile.ExecutablePath);
    }

    private static void ValidateRestoreMode(RestoreMode mode, string context)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ConfigurationValidationException(
                $"Unsupported restore mode '{mode}' for '{context}'.");
        }
    }
}
