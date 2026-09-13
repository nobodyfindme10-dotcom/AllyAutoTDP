using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AllyAutoTDP.Hardware;

public enum PowerSourceKind
{
    Battery,
    Ac,
    Unknown
}

public readonly record struct PowerSourceReading(
    PowerSourceKind Source,
    string? ErrorMessage)
{
    public bool IsKnown => Source != PowerSourceKind.Unknown;
}

public interface IPowerSourceReader
{
    PowerSourceReading Read();
}

public sealed class WindowsPowerSource : IPowerSourceReader
{
    public PowerSourceReading Read()
    {
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
        {
            int error = Marshal.GetLastWin32Error();
            return new(
                PowerSourceKind.Unknown,
                $"GetSystemPowerStatus failed: {error} " +
                new Win32Exception(error).Message);
        }

        return TryInterpretAcLineStatus(
            status.ACLineStatus,
            out PowerSourceKind source,
            out string? errorMessage)
            ? new(source, null)
            : new(PowerSourceKind.Unknown, errorMessage);
    }

    public static bool TryInterpretAcLineStatus(
        byte acLineStatus,
        out PowerSourceKind source,
        out string? errorMessage)
    {
        switch (acLineStatus)
        {
            case 0:
                source = PowerSourceKind.Battery;
                errorMessage = null;
                return true;
            case 1:
                source = PowerSourceKind.Ac;
                errorMessage = null;
                return true;
            default:
                source = PowerSourceKind.Unknown;
                errorMessage =
                    $"Windows power source is unknown (ACLineStatus={acLineStatus}).";
                return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(
        out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
