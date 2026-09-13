using AllyAutoTDP.Windows;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Configuration;

public static class ExecutablePathIdentity
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ConfigurationValidationException(
                "ExecutablePath is required.");
        }

        string trimmedPath = path.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            throw new ConfigurationValidationException(
                $"ExecutablePath must be fully qualified: '{path}'.");
        }

        if (!string.Equals(
                Path.GetExtension(trimmedPath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationValidationException(
                $"ExecutablePath must point to an .exe file: '{path}'.");
        }

        string normalizedPath = Path.GetFullPath(trimmedPath);
        return normalizedPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    public static bool TryNormalize(
        string? path,
        out string normalizedPath,
        out string? errorMessage)
    {
        try
        {
            normalizedPath = Normalize(path ?? string.Empty);
            errorMessage = null;
            return true;
        }
        catch (ConfigurationValidationException exception)
        {
            normalizedPath = string.Empty;
            errorMessage = exception.Message;
            return false;
        }
    }
}

public interface IGameProfileLookup
{
    GameProfile? FindByExecutablePath(string executablePath);
}

public sealed class GameProfileService : IGameProfileLookup
{
    private readonly ConfigurationStore _store;

    public GameProfileService(ConfigurationStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public GameProfile? FindByExecutablePath(string executablePath)
    {
        if (!ExecutablePathIdentity.TryNormalize(
                executablePath,
                out string normalizedPath,
                out _))
        {
            return null;
        }

        return _store.Current.GameProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.ExecutablePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
    }

    public GameProfile CreateFromForeground(ForegroundApplicationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return CreateFromExecutablePath(
            snapshot.ExecutablePath,
            snapshot.DisplayName);
    }

    public GameProfile CreateFromForeground(
        ForegroundApplicationSnapshot snapshot,
        int targetFps,
        RestoreMode restoreMode,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return CreateFromExecutablePath(
            snapshot.ExecutablePath,
            snapshot.DisplayName,
            targetFps,
            restoreMode,
            enabled);
    }

    public GameProfile CreateFromExecutablePath(
        string executablePath,
        string? displayName = null)
    {
        ProfileDefaults defaults = _store.Current.ProfileDefaults;
        return CreateFromExecutablePath(
            executablePath,
            displayName,
            defaults.TargetFps,
            defaults.RestoreMode,
            enabled: true);
    }

    private GameProfile CreateFromExecutablePath(
        string executablePath,
        string? displayName,
        int targetFps,
        RestoreMode restoreMode,
        bool enabled)
    {
        string normalizedPath = ExecutablePathIdentity.Normalize(executablePath);
        if (FindByExecutablePath(normalizedPath) is not null)
        {
            throw new ConfigurationValidationException(
                $"A game profile already exists for '{normalizedPath}'.");
        }

        if (!File.Exists(normalizedPath))
        {
            throw new ConfigurationValidationException(
                $"Executable file does not exist: '{normalizedPath}'.");
        }

        if (!ConfigurationPolicy.IsAcceptedTargetFps(targetFps))
        {
            throw new ConfigurationValidationException(
                $"Unsupported target FPS '{targetFps}'.");
        }

        if (!Enum.IsDefined(restoreMode))
        {
            throw new ConfigurationValidationException(
                $"Unsupported restore mode '{restoreMode}'.");
        }

        GameProfile profile = new()
        {
            ExecutablePath = normalizedPath,
            DisplayName = ResolveDisplayName(normalizedPath, displayName),
            Enabled = enabled,
            TargetFps = targetFps,
            RestoreMode = restoreMode,
        };

        _store.Current.GameProfiles.Add(profile);
        try
        {
            _store.Save();
        }
        catch
        {
            _store.Current.GameProfiles.Remove(profile);
            throw;
        }

        return profile;
    }

    public void Update(
        string originalExecutablePath,
        GameProfile updatedProfile)
    {
        ArgumentNullException.ThrowIfNull(updatedProfile);

        string originalPath = ExecutablePathIdentity.Normalize(originalExecutablePath);
        GameProfile? existing = FindByExecutablePath(originalPath);
        if (existing is null)
        {
            throw new ConfigurationValidationException(
                $"Game profile does not exist for '{originalPath}'.");
        }

        string normalizedPath = ExecutablePathIdentity.Normalize(
            updatedProfile.ExecutablePath);
        if (!File.Exists(normalizedPath))
        {
            throw new ConfigurationValidationException(
                $"Executable file does not exist: '{normalizedPath}'.");
        }

        GameProfile? duplicate = _store.Current.GameProfiles.FirstOrDefault(profile =>
            !ReferenceEquals(profile, existing) &&
            string.Equals(
                profile.ExecutablePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            throw new ConfigurationValidationException(
                $"A game profile already exists for '{normalizedPath}'.");
        }

        int index = _store.Current.GameProfiles.IndexOf(existing);
        GameProfile previous = new()
        {
            ExecutablePath = existing.ExecutablePath,
            DisplayName = existing.DisplayName,
            Enabled = existing.Enabled,
            TargetFps = existing.TargetFps,
            RestoreMode = existing.RestoreMode
        };
        updatedProfile.ExecutablePath = normalizedPath;
        _store.Current.GameProfiles[index] = updatedProfile;
        try
        {
            _store.Save();
        }
        catch
        {
            _store.Current.GameProfiles[index] = previous;
            throw;
        }
    }

    public bool RemoveByExecutablePath(string executablePath)
    {
        string normalizedPath = ExecutablePathIdentity.Normalize(executablePath);
        GameProfile? profile = FindByExecutablePath(normalizedPath);
        if (profile is null)
        {
            return false;
        }

        _store.Current.GameProfiles.Remove(profile);
        try
        {
            _store.Save();
        }
        catch
        {
            _store.Current.GameProfiles.Add(profile);
            throw;
        }

        return true;
    }

    private static string ResolveDisplayName(
        string executablePath,
        string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return Path.GetFileNameWithoutExtension(executablePath);
    }
}
