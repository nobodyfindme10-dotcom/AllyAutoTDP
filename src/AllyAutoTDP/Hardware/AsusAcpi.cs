using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AllyAutoTDP.Hardware;

public readonly record struct AcpiReadResult(
    bool IsSuccess,
    int? Value,
    int? Win32Error,
    string? ErrorMessage);

public readonly record struct AcpiWriteResult(
    bool IsSuccess,
    int ResultCode,
    int? Win32Error,
    string? ErrorMessage);

public interface IAsusAcpi : IDisposable
{
    bool IsConnected { get; }
    string? OpenError { get; }
    AcpiReadResult DeviceGet(uint deviceId);
    AcpiWriteResult DeviceSet(uint deviceId, int value);
}

public sealed class AsusAcpi : IAsusAcpi
{
    private const string FileName = @"\\.\ATKACPI";
    private const uint ControlCode = 0x0022240C;
    private const uint Dsts = 0x53545344;
    private const uint Devs = 0x53564544;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private IntPtr _handle = InvalidHandleValue;

    public bool IsConnected => _handle != InvalidHandleValue && _handle != IntPtr.Zero;
    public string? OpenError { get; private set; }

    public AsusAcpi()
    {
        try
        {
            _handle = CreateFile(
                FileName,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            if (!IsConnected)
            {
                int win32Error = Marshal.GetLastWin32Error();
                OpenError = $"CreateFile({FileName}) failed: {win32Error} {new Win32Exception(win32Error).Message}";
            }
        }
        catch (Exception ex)
        {
            OpenError = $"CreateFile({FileName}) threw {ex.GetType().Name}: {ex.Message}";
            _handle = InvalidHandleValue;
        }
    }

    public AcpiReadResult DeviceGet(uint deviceId)
    {
        AcpiCallResult call = CallMethod(Dsts, new byte[8]
        {
            (byte)deviceId,
            (byte)(deviceId >> 8),
            (byte)(deviceId >> 16),
            (byte)(deviceId >> 24),
            0, 0, 0, 0
        });

        if (!call.IsSuccess)
            return new(false, null, call.Win32Error, call.ErrorMessage);

        int rawValue = BitConverter.ToInt32(call.Output, 0);
        long decodedValue = (long)rawValue - 65536;
        if (decodedValue < 0 || decodedValue > int.MaxValue)
        {
            return new(false, null, null, $"DSTS returned non-interpretable value {rawValue}");
        }

        return new(true, (int)decodedValue, null, null);
    }

    public AcpiWriteResult DeviceSet(uint deviceId, int value)
    {
        byte[] args = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        BitConverter.GetBytes((uint)value).CopyTo(args, 4);

        AcpiCallResult call = CallMethod(Devs, args);
        if (!call.IsSuccess)
            return new(false, -1, call.Win32Error, call.ErrorMessage);

        int resultCode = BitConverter.ToInt32(call.Output, 0);
        return new(resultCode == 1, resultCode, null, null);
    }

    public void Dispose()
    {
        if (IsConnected)
            CloseHandle(_handle);

        _handle = InvalidHandleValue;
    }

    private AcpiCallResult CallMethod(uint methodId, byte[] args)
    {
        if (!IsConnected)
            return new(false, Array.Empty<byte>(), null, OpenError ?? "ATKACPI is not connected");

        byte[] input = new byte[8 + args.Length];
        byte[] output = new byte[16];
        BitConverter.GetBytes(methodId).CopyTo(input, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(input, 4);
        Array.Copy(args, 0, input, 8, args.Length);

        uint bytesReturned = 0;
        bool success = DeviceIoControl(
            _handle,
            ControlCode,
            input,
            (uint)input.Length,
            output,
            (uint)output.Length,
            ref bytesReturned,
            IntPtr.Zero);

        if (!success)
        {
            int win32Error = Marshal.GetLastWin32Error();
            return new(false, Array.Empty<byte>(), win32Error,
                $"DeviceIoControl failed: {win32Error} {new Win32Exception(win32Error).Message}");
        }

        if (bytesReturned < sizeof(int))
        {
            return new(false, Array.Empty<byte>(), null,
                $"DeviceIoControl returned only {bytesReturned} bytes");
        }

        return new(true, output, null, null);
    }

    private readonly record struct AcpiCallResult(
        bool IsSuccess,
        byte[] Output,
        int? Win32Error,
        string? ErrorMessage);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        ref uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
