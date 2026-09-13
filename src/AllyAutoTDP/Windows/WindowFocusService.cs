using System.Diagnostics;
using System.Runtime.InteropServices;
using AllyAutoTDP.Configuration;

namespace AllyAutoTDP.Windows;

public interface IWindowFocusService
{
    nint GetForegroundWindow();

    bool IsWindow(nint windowHandle);

    int GetProcessId(nint windowHandle);

    bool IsSameProcess(nint windowHandle, ForegroundApplicationSnapshot application);

    bool TrySetForegroundWindow(nint windowHandle);
}

public sealed class User32WindowFocusService : IWindowFocusService
{
    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    public bool IsWindow(nint windowHandle) =>
        windowHandle != nint.Zero && NativeIsWindow(windowHandle);

    public int GetProcessId(nint windowHandle)
    {
        if (!IsWindow(windowHandle))
            return 0;

        uint processId;
        return NativeGetWindowThreadProcessId(windowHandle, out processId) == 0
            ? 0
            : checked((int)processId);
    }

    public bool IsSameProcess(
        nint windowHandle,
        ForegroundApplicationSnapshot application)
    {
        ArgumentNullException.ThrowIfNull(application);
        int processId = GetProcessId(windowHandle);
        if (processId == 0 || processId != application.ProcessId)
            return false;

        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;

            DateTimeOffset startTime = new DateTimeOffset(process.StartTime)
                .ToUniversalTime();
            string? executablePath = process.MainModule?.FileName;
            return startTime == application.ProcessStartTime &&
                executablePath is not null &&
                string.Equals(
                    ExecutablePathIdentity.Normalize(executablePath),
                    application.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TrySetForegroundWindow(nint windowHandle) =>
        IsWindow(windowHandle) && NativeSetForegroundWindow(windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindow(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint NativeGetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeSetForegroundWindow(nint windowHandle);
}
