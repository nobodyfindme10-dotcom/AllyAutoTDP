using System.Diagnostics;
using System.Runtime.InteropServices;
using AllyAutoTDP.Configuration;

namespace AllyAutoTDP.Windows;

public sealed record ForegroundApplicationSnapshot(
    int ProcessId,
    DateTimeOffset ProcessStartTime,
    string ExecutablePath,
    string FileName,
    string DisplayName);

public readonly record struct ForegroundDetectionResult(
    ForegroundApplicationSnapshot? Application,
    string? ErrorMessage,
    nint WindowHandle = 0)
{
    public bool IsAvailable => Application is not null;
}

public interface IForegroundWindowApi
{
    nint GetForegroundWindow();

    uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}

public interface IProcessSnapshotProvider
{
    bool TryRead(
        int processId,
        out ForegroundApplicationSnapshot? snapshot,
        out string? errorMessage);
}

public interface IForegroundApplicationDetector
{
    ForegroundDetectionResult Detect();
}

public sealed class ForegroundApplicationDetector : IForegroundApplicationDetector
{
    private readonly IForegroundWindowApi _windowApi;
    private readonly IProcessSnapshotProvider _processProvider;
    private readonly int _ownProcessId;

    public ForegroundApplicationDetector(
        IForegroundWindowApi windowApi,
        IProcessSnapshotProvider processProvider,
        int? ownProcessId = null)
    {
        _windowApi = windowApi ?? throw new ArgumentNullException(nameof(windowApi));
        _processProvider = processProvider ??
            throw new ArgumentNullException(nameof(processProvider));
        _ownProcessId = ownProcessId ?? Environment.ProcessId;
    }

    public ForegroundDetectionResult Detect()
    {
        nint windowHandle = _windowApi.GetForegroundWindow();
        if (windowHandle == nint.Zero)
            return new(null, "No foreground window is available.");

        uint threadId = _windowApi.GetWindowThreadProcessId(
            windowHandle,
            out uint processId);
        if (threadId == 0 || processId == 0)
            return new(
                null,
                "The foreground window has no resolvable process.",
                windowHandle);

        if (processId == _ownProcessId)
            return new(
                null,
                "The AllyAutoTDP process is foreground.",
                windowHandle);

        if (!_processProvider.TryRead(
                checked((int)processId),
                out ForegroundApplicationSnapshot? snapshot,
                out string? errorMessage))
            return new(
                null,
                errorMessage ?? "Foreground process could not be read.",
                windowHandle);

        return new(snapshot, null, windowHandle);
    }
}

public sealed class User32ForegroundWindowApi : IForegroundWindowApi
{
    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    public uint GetWindowThreadProcessId(nint windowHandle, out uint processId) =>
        NativeGetWindowThreadProcessId(windowHandle, out processId);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindowFromUser32();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint NativeGetWindowThreadProcessIdFromUser32(
        nint windowHandle,
        out uint processId);

    private static nint NativeGetForegroundWindow() =>
        NativeGetForegroundWindowFromUser32();

    private static uint NativeGetWindowThreadProcessId(
        nint windowHandle,
        out uint processId) =>
        NativeGetWindowThreadProcessIdFromUser32(windowHandle, out processId);
}

public sealed class WindowsProcessSnapshotProvider : IProcessSnapshotProvider
{
    public bool TryRead(
        int processId,
        out ForegroundApplicationSnapshot? snapshot,
        out string? errorMessage)
    {
        snapshot = null;
        errorMessage = null;

        try
        {
            using Process process = Process.GetProcessById(processId);

            if (process.HasExited)
            {
                errorMessage = $"Process {processId} has exited.";
                return false;
            }

            string? executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                errorMessage = $"Process {processId} has no readable executable path.";
                return false;
            }

            string normalizedPath = ExecutablePathIdentity.Normalize(executablePath);
            DateTimeOffset startTime = new DateTimeOffset(process.StartTime)
                .ToUniversalTime();
            string fileName = Path.GetFileName(normalizedPath);
            string displayName = GetDisplayName(normalizedPath, fileName);
            snapshot = new(
                processId,
                startTime,
                normalizedPath,
                fileName,
                displayName);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            UnauthorizedAccessException or
            ConfigurationValidationException)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private string GetDisplayName(
        string executablePath,
        string fileName)
    {
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(executablePath)
                .FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }
}
