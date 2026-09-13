using System.Runtime.InteropServices;

namespace AllyAutoTDP.Windows;

[Flags]
public enum GlobalHotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
    NoRepeat = 0x4000
}

public readonly record struct HotkeyDefinition(
    GlobalHotkeyModifiers Modifiers,
    Keys Key)
{
    public static HotkeyDefinition Default => new(
        GlobalHotkeyModifiers.Control |
        GlobalHotkeyModifiers.Alt |
        GlobalHotkeyModifiers.NoRepeat,
        Keys.T);
}

public interface IGlobalHotkeyApi
{
    bool RegisterHotKey(
        nint windowHandle,
        int identifier,
        uint modifiers,
        uint key,
        out int errorCode);

    bool UnregisterHotKey(nint windowHandle, int identifier);
}

public sealed class GlobalHotkeyHost : IDisposable
{
    public const int WmHotkey = 0x0312;
    public const int HotkeyIdentifier = 0x4154;
    private readonly IGlobalHotkeyApi _api;
    private readonly HotkeyWindow _window;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkeyHost(IGlobalHotkeyApi? api = null)
    {
        _api = api ?? new User32GlobalHotkeyApi();
        _window = new HotkeyWindow(this);
        _window.CreateHandle(new CreateParams());
    }

    public event EventHandler? HotkeyPressed;

    public bool IsRegistered => _registered;

    public nint WindowHandle => _window.Handle;

    public int? RegistrationErrorCode { get; private set; }

    public string? RegistrationErrorMessage { get; private set; }

    public bool TryRegister(HotkeyDefinition definition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            Unregister();

            bool registered = _api.RegisterHotKey(
                _window.Handle,
                HotkeyIdentifier,
                (uint)definition.Modifiers,
                (uint)definition.Key,
                out int errorCode);
            _registered = registered;
            RegistrationErrorCode = registered ? null : errorCode;
            RegistrationErrorMessage = registered
                ? null
                : $"RegisterHotKey returned false (Win32 error code {errorCode}).";
            return registered;
        }
        catch (Exception exception) when (IsRecoverableInteropException(exception))
        {
            _registered = false;
            RegistrationErrorCode = null;
            RegistrationErrorMessage = exception.ToString();
            return false;
        }
    }

    public void Unregister()
    {
        if (!_registered)
            return;

        _api.UnregisterHotKey(_window.Handle, HotkeyIdentifier);
        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Unregister();
        _window.DestroyHandle();
        _disposed = true;
    }

    private void HandleMessage(ref Message message)
    {
        if (message.Msg == WmHotkey &&
            message.WParam.ToInt32() == HotkeyIdentifier)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsRecoverableInteropException(Exception exception) =>
        exception is EntryPointNotFoundException or
            DllNotFoundException or
            BadImageFormatException or
            MarshalDirectiveException;

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly GlobalHotkeyHost _owner;

        public HotkeyWindow(GlobalHotkeyHost owner) => _owner = owner;

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            _owner.HandleMessage(ref message);
        }
    }
}

public sealed class User32GlobalHotkeyApi : IGlobalHotkeyApi
{
    public bool RegisterHotKey(
        nint windowHandle,
        int identifier,
        uint modifiers,
        uint key,
        out int errorCode)
    {
        bool result = NativeRegisterHotKey(
            windowHandle,
            identifier,
            modifiers,
            key);
        errorCode = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public bool UnregisterHotKey(nint windowHandle, int identifier) =>
        NativeUnregisterHotKey(windowHandle, identifier);

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterHotKey",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeRegisterHotKey(
        nint windowHandle,
        int identifier,
        uint modifiers,
        uint key);

    [DllImport(
        "user32.dll",
        EntryPoint = "UnregisterHotKey",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeUnregisterHotKey(
        nint windowHandle,
        int identifier);
}
