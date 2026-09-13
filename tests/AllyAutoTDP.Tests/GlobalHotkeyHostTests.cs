using System.Reflection;
using System.Runtime.InteropServices;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class GlobalHotkeyHostTests
{
    [TestMethod]
    public void NativeHotkeyDeclarations_MapToUser32EntryPoints()
    {
        AssertEntryPoint("NativeRegisterHotKey", "RegisterHotKey");
        AssertEntryPoint("NativeUnregisterHotKey", "UnregisterHotKey");
    }

    [TestMethod]
    public void Register_UsesCtrlAltTAndNoRepeat_AndUnregistersOnDispose()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                var api = new FakeGlobalHotkeyApi();
                using (var host = new GlobalHotkeyHost(api))
                {
                    Assert.IsTrue(host.TryRegister(HotkeyDefinition.Default));
                    Assert.AreEqual(
                        (uint)(GlobalHotkeyModifiers.Control |
                            GlobalHotkeyModifiers.Alt |
                            GlobalHotkeyModifiers.NoRepeat),
                        api.Modifiers);
                    Assert.AreEqual((uint)Keys.T, api.Key);
                }

                Assert.AreEqual(1, api.UnregisterCount);
                Assert.AreEqual(
                    GlobalHotkeyHost.HotkeyIdentifier,
                    api.UnregisterIdentifier);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void RegisterFailure_IsNonFatalAndExposesError()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                var api = new FakeGlobalHotkeyApi
                {
                    RegisterResult = false,
                    ErrorCode = 1409
                };
                using var host = new GlobalHotkeyHost(api);

                Assert.IsFalse(host.TryRegister(HotkeyDefinition.Default));
                Assert.IsFalse(host.IsRegistered);
                Assert.AreEqual(1409, host.RegistrationErrorCode);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void RegisterInteropException_IsNonFatalAndMarksHotkeyUnavailable()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                var api = new FakeGlobalHotkeyApi
                {
                    RegisterException = new EntryPointNotFoundException(
                        "RegisterHotKey missing")
                };
                using var host = new GlobalHotkeyHost(api);

                Assert.IsFalse(host.TryRegister(HotkeyDefinition.Default));
                Assert.IsFalse(host.IsRegistered);
                Assert.IsNull(host.RegistrationErrorCode);
                Assert.IsNotNull(host.RegistrationErrorMessage);
                StringAssert.Contains(
                    host.RegistrationErrorMessage!,
                    nameof(EntryPointNotFoundException));
                StringAssert.Contains(
                    host.RegistrationErrorMessage!,
                    "RegisterHotKey missing");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void Dispose_WithoutSuccessfulRegistration_DoesNotCallUnregister()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                var api = new FakeGlobalHotkeyApi();
                using var host = new GlobalHotkeyHost(api);

                host.Dispose();

                Assert.AreEqual(0, api.UnregisterCount);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    [TestMethod]
    public void WmHotkey_RaisesEvent()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using var host = new GlobalHotkeyHost(new FakeGlobalHotkeyApi());
                using var received = new ManualResetEventSlim();
                host.HotkeyPressed += (_, _) => received.Set();

                Assert.IsTrue(NativePostMessage(
                    host.WindowHandle,
                    GlobalHotkeyHost.WmHotkey,
                    (nint)GlobalHotkeyHost.HotkeyIdentifier,
                    nint.Zero));

                DateTime deadline = DateTime.UtcNow.AddSeconds(1);
                while (!received.IsSet && DateTime.UtcNow < deadline)
                {
            System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(1);
                }

                Assert.IsTrue(received.IsSet);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }

    private sealed class FakeGlobalHotkeyApi : IGlobalHotkeyApi
    {
        public bool RegisterResult { get; init; } = true;

        public int ErrorCode { get; init; }

        public Exception? RegisterException { get; init; }

        public uint Modifiers { get; private set; }

        public uint Key { get; private set; }

        public int UnregisterCount { get; private set; }

        public int UnregisterIdentifier { get; private set; }

        public bool RegisterHotKey(
            nint windowHandle,
            int identifier,
            uint modifiers,
            uint key,
            out int errorCode)
        {
            if (RegisterException is not null)
                throw RegisterException;

            Modifiers = modifiers;
            Key = key;
            errorCode = ErrorCode;
            return RegisterResult;
        }

        public bool UnregisterHotKey(nint windowHandle, int identifier)
        {
            UnregisterCount++;
            UnregisterIdentifier = identifier;
            return true;
        }
    }

    private static void AssertEntryPoint(string methodName, string entryPoint)
    {
        MethodInfo method = typeof(User32GlobalHotkeyApi).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new AssertFailedException(
                $"Could not find native method {methodName}.");
        DllImportAttribute? import = method.GetCustomAttribute<DllImportAttribute>();

        Assert.IsNotNull(import);
        Assert.AreEqual("user32.dll", import.Value);
        Assert.AreEqual(entryPoint, import.EntryPoint);
    }

    [DllImport("user32.dll", EntryPoint = "PostMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativePostMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam);
}
