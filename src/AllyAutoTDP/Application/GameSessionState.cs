using System.Diagnostics;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Windows;

namespace AllyAutoTDP.Application;

public enum GameSessionState
{
    Disabled,
    Ready,
    Active,
    Paused,
    MaxLimit,
    Restoring,
    Error,
}

public readonly record struct GameProcessIdentity(
    string ExecutablePath,
    int ProcessId,
    DateTimeOffset ProcessStartTime)
{
    public static GameProcessIdentity FromSnapshot(
        ForegroundApplicationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            ExecutablePathIdentity.Normalize(snapshot.ExecutablePath),
            snapshot.ProcessId,
            snapshot.ProcessStartTime.ToUniversalTime());
    }
}

public sealed record ActiveGameSession(
    GameProfile Profile,
    GameProcessIdentity ProcessIdentity);

public enum ProcessLifetimeStatus
{
    Alive,
    Terminated,
    Unknown,
}

public interface IProcessLifetimeChecker
{
    ProcessLifetimeStatus Check(GameProcessIdentity identity);
}

public sealed class WindowsProcessLifetimeChecker : IProcessLifetimeChecker
{
    public ProcessLifetimeStatus Check(GameProcessIdentity identity)
    {
        try
        {
            using Process process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited)
            {
                return ProcessLifetimeStatus.Terminated;
            }

            DateTimeOffset startTime = new DateTimeOffset(process.StartTime)
                .ToUniversalTime();
            if (startTime != identity.ProcessStartTime.ToUniversalTime())
            {
                return ProcessLifetimeStatus.Terminated;
            }

            string? executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return ProcessLifetimeStatus.Unknown;
            }

            string normalizedPath = ExecutablePathIdentity.Normalize(executablePath);
            return string.Equals(
                    normalizedPath,
                    identity.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase)
                ? ProcessLifetimeStatus.Alive
                : ProcessLifetimeStatus.Terminated;
        }
        catch (ArgumentException)
        {
            return ProcessLifetimeStatus.Terminated;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            UnauthorizedAccessException or
            ConfigurationValidationException)
        {
            return ProcessLifetimeStatus.Unknown;
        }
    }
}
