using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllyAutoTDP.Configuration;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigurationStore(string? configPath = null)
    {
        ConfigPath = configPath ?? GetDefaultConfigPath();
        BackupPath = ConfigPath + ".bak";
        TemporaryPath = ConfigPath + ".tmp";
        Current = LoadOrCreate();
    }

    public string ConfigPath { get; }

    public string BackupPath { get; }

    public string TemporaryPath { get; }

    public AllyAutoTdpConfiguration Current { get; private set; }

    public string? LastLoadWarning { get; private set; }

    public void Save()
    {
        Save(Current);
    }

    public void Save(AllyAutoTdpConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        GameConfigurationValidator.NormalizeAndValidate(configuration);

        string json = JsonSerializer.Serialize(configuration, SerializerOptions);
        try
        {
            string? directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            WriteTemporaryFile(json);
            ReplacePrimaryFile();
            Current = configuration;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            throw new ConfigurationPersistenceException(
                $"Could not persist configuration at '{ConfigPath}'.",
                exception);
        }
    }

    private AllyAutoTdpConfiguration LoadOrCreate()
    {
        bool primaryExists = File.Exists(ConfigPath);
        bool backupExists = File.Exists(BackupPath);
        Exception? primaryError = null;

        if (primaryExists && TryLoad(
                ConfigPath,
                out AllyAutoTdpConfiguration? primary,
                out primaryError,
                out bool primaryMigrated,
                out string? primaryMigrationWarning))
        {
            if (primaryMigrated)
            {
                LastLoadWarning = primaryMigrationWarning;
                Save(primary!);
            }

            return primary!;
        }

        if (backupExists && TryLoad(
                BackupPath,
                out AllyAutoTdpConfiguration? backup,
                out Exception? backupError,
                out bool backupMigrated,
                out string? backupMigrationWarning))
        {
            LastLoadWarning = primaryExists
                ? $"Primary configuration was invalid; loaded backup '{BackupPath}'."
                : $"Primary configuration was missing; loaded backup '{BackupPath}'.";
            if (backupMigrated && !string.IsNullOrWhiteSpace(backupMigrationWarning))
            {
                LastLoadWarning += $" {backupMigrationWarning}";
            }

            return backup!;
        }

        if (primaryExists || backupExists)
        {
            string detail = primaryError?.Message ?? "No valid primary configuration.";
            throw new ConfigurationLoadException(
                $"No valid configuration could be loaded from '{ConfigPath}' " +
                $"or '{BackupPath}'. {detail}",
                primaryError);
        }

        AllyAutoTdpConfiguration defaults = new();
        GameConfigurationValidator.NormalizeAndValidate(defaults);
        Save(defaults);
        return defaults;
    }

    private bool TryLoad(
        string path,
        out AllyAutoTdpConfiguration? configuration,
        out Exception? error,
        out bool migrated,
        out string? migrationWarning)
    {
        migrated = false;
        migrationWarning = null;
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (ConfigurationMigration.TryMigrate(
                    json,
                    out configuration,
                    out migrationWarning))
            {
                migrated = true;
            }
            else
            {
                configuration = JsonSerializer.Deserialize<AllyAutoTdpConfiguration>(
                    json,
                    SerializerOptions);
            }

            if (configuration is null)
            {
                throw new ConfigurationValidationException(
                    $"Configuration '{path}' is empty.");
            }

            GameConfigurationValidator.NormalizeAndValidate(configuration);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or
            ConfigurationValidationException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            configuration = null;
            error = exception;
            return false;
        }
    }

    private void WriteTemporaryFile(string json)
    {
        using FileStream stream = new(
            TemporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using StreamWriter writer = new(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true);
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private void ReplacePrimaryFile()
    {
        if (!File.Exists(ConfigPath))
        {
            File.Move(TemporaryPath, ConfigPath);
            return;
        }

        try
        {
            File.Replace(
                TemporaryPath,
                ConfigPath,
                BackupPath,
                ignoreMetadataErrors: true);
        }
        catch (IOException)
        {
            ReplacePrimaryFileWithFallback();
        }
        catch (PlatformNotSupportedException)
        {
            ReplacePrimaryFileWithFallback();
        }
    }

    private void ReplacePrimaryFileWithFallback()
    {
        File.Copy(ConfigPath, BackupPath, overwrite: true);
        File.Move(TemporaryPath, ConfigPath, overwrite: true);
    }

    private static string GetDefaultConfigPath()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AllyAutoTDP", "config.json");
    }
}

public sealed class ConfigurationLoadException : Exception
{
    public ConfigurationLoadException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ConfigurationPersistenceException : Exception
{
    public ConfigurationPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
