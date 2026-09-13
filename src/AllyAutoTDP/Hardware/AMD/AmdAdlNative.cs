using System.Runtime.InteropServices;
using AllyAutoTDP;

namespace AllyAutoTDP.Hardware.AMD;

public readonly record struct AdlOperationResult(
    bool IsSuccess,
    int ErrorCode,
    string? ErrorMessage);

public readonly record struct AdlFpsResult(
    bool IsSuccess,
    float Value,
    int ErrorCode,
    string? ErrorMessage);

public readonly record struct AdlPowerResult(
    bool IsSuccess,
    int Value,
    int ErrorCode,
    string? ErrorMessage);

public interface IAmdAdl : IDisposable
{
    bool IsInitialized { get; }
    AdlOperationResult StartFrameMetrics();
    AdlFpsResult GetFrameMetrics();
    AdlOperationResult StopFrameMetrics();
    AdlPowerResult GetIgpuAsicPower();
}

public sealed class AmdAdlNative : IAmdAdl
{
    public const int AdlSuccess = 0;
    public const int AdlMaxAdapters = 40;
    public const int AdlMaxPath = 256;
    public const int AdlPmLogMaxSensors = 256;
    public const int AdlPmLogAsicPower = 23;
    public const int AmdVendorId = 1002;

    private readonly nint _context;
    private readonly int _igpuAdapterIndex;
    private bool _frameMetricsStarted;
    private bool _disposed;
    private bool _contextDestroyed;
    private static readonly NativeMethods.AdlMainMemoryAlloc MemoryAllocCallback =
        MemoryAlloc;

    private AmdAdlNative(nint context, int igpuAdapterIndex)
    {
        _context = context;
        _igpuAdapterIndex = igpuAdapterIndex;
    }

    public bool IsInitialized => !_disposed && _context != nint.Zero;

    public static bool TryCreate(out AmdAdlNative? session, out string? error)
    {
        session = null;
        error = null;
        nint context = nint.Zero;

        try
        {
            Marshal.PrelinkAll(typeof(NativeMethods));

            int createResult = NativeMethods.ADL2_Main_Control_Create(
                MemoryAllocCallback,
                1,
                out context);
            if (createResult != AdlSuccess || context == nint.Zero)
            {
                error = $"ADL2_Main_Control_Create failed: {createResult}";
                DestroyContext(context);
                return false;
            }

            if (!TryFindIntegratedAdapter(context, out AdlAdapterInfo adapter, out error))
            {
                DestroyContext(context);
                return false;
            }

            session = new AmdAdlNative(context, adapter.AdapterIndex);
            return true;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException("ADL initialization", ex);
            DestroyContext(context);
            error = $"ADL initialization failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public AdlOperationResult StartFrameMetrics()
    {
        if (!IsInitialized)
            return Failure("ADL session is not initialized.");

        try
        {
            int result = NativeMethods.ADL2_Adapter_FrameMetrics_Start(
                _context,
                _igpuAdapterIndex,
                0);
            if (result == AdlSuccess)
                _frameMetricsStarted = true;

            return result == AdlSuccess
                ? Success()
                : Failure($"ADL FrameMetrics start failed: {result}", result);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException("ADL FrameMetrics start", ex);
            return Failure($"ADL FrameMetrics start threw: {ex.Message}");
        }
    }

    public AdlFpsResult GetFrameMetrics()
    {
        if (!IsInitialized)
            return new(false, 0, -1, "ADL session is not initialized.");

        try
        {
            int result = NativeMethods.ADL2_Adapter_FrameMetrics_Get(
                _context,
                _igpuAdapterIndex,
                0,
                out float fps);
            return result == AdlSuccess
                ? new(true, fps, result, null)
                : new(false, 0, result, $"ADL FrameMetrics get failed: {result}");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException("ADL FrameMetrics get", ex);
            return new(false, 0, -1, $"ADL FrameMetrics get threw: {ex.Message}");
        }
    }

    public AdlOperationResult StopFrameMetrics()
    {
        if (!IsInitialized)
            return Failure("ADL session is not initialized.");

        if (!_frameMetricsStarted)
            return Success();

        try
        {
            int result = NativeMethods.ADL2_Adapter_FrameMetrics_Stop(
                _context,
                _igpuAdapterIndex,
                0);
            _frameMetricsStarted = false;
            return result == AdlSuccess
                ? Success()
                : Failure($"ADL FrameMetrics stop failed: {result}", result);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException("ADL FrameMetrics stop", ex);
            _frameMetricsStarted = false;
            return Failure($"ADL FrameMetrics stop threw: {ex.Message}");
        }
    }

    public AdlPowerResult GetIgpuAsicPower()
    {
        if (!IsInitialized)
            return new(false, 0, -1, "ADL session is not initialized.");

        try
        {
            int result = NativeMethods.ADL2_New_QueryPMLogData_Get(
                _context,
                _igpuAdapterIndex,
                out AdlPmLogDataOutput log);
            if (result != AdlSuccess)
            {
                return new(
                    false,
                    0,
                    result,
                    $"ADL PMLog query failed: {result}");
            }

            if (log.Sensors is null || log.Sensors.Length <= AdlPmLogAsicPower)
            {
                return new(false, 0, result, "ADL PMLog sensor array is unavailable.");
            }

            AdlSingleSensorData sensor = log.Sensors[AdlPmLogAsicPower];
            if (sensor.Supported == 0)
            {
                return new(false, 0, result, "ADL ASIC power sensor is unsupported.");
            }

            return new(true, sensor.Value, result, null);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException("ADL PMLog query", ex);
            return new(false, 0, -1, $"ADL PMLog query threw: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_frameMetricsStarted)
                StopFrameMetricsForDispose();
        }
        finally
        {
            DestroyContext(_context);
            _contextDestroyed = true;
        }
    }

    private void StopFrameMetricsForDispose()
    {
        try
        {
            NativeMethods.ADL2_Adapter_FrameMetrics_Stop(
                _context,
                _igpuAdapterIndex,
                0);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException(
                "ADL FrameMetrics stop during dispose",
                ex);
        }

        _frameMetricsStarted = false;
    }

    private static bool TryFindIntegratedAdapter(
        nint context,
        out AdlAdapterInfo adapter,
        out string? error)
    {
        adapter = default;
        error = null;

        int numberResult = NativeMethods.ADL2_Adapter_NumberOfAdapters_Get(
            context,
            out int numberOfAdapters);
        if (numberResult != AdlSuccess)
        {
            error = $"ADL adapter count failed: {numberResult}";
            return false;
        }

        if (numberOfAdapters <= 0)
        {
            error = "ADL reported no adapters.";
            return false;
        }

        AdlAdapterInfoArray info = new()
        {
            Items = new AdlAdapterInfo[AdlMaxAdapters]
        };
        int size = Marshal.SizeOf<AdlAdapterInfoArray>();
        nint buffer = Marshal.AllocCoTaskMem(size);

        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            int infoResult = NativeMethods.ADL2_Adapter_AdapterInfo_Get(
                context,
                buffer,
                size);
            if (infoResult != AdlSuccess)
            {
                error = $"ADL adapter information failed: {infoResult}";
                return false;
            }

            info = Marshal.PtrToStructure<AdlAdapterInfoArray>(buffer);
            int count = Math.Min(numberOfAdapters, info.Items?.Length ?? 0);
            for (int index = 0; index < count; index++)
            {
                AdlAdapterInfo candidate = info.Items[index];
                if (candidate.Exist == 0 || candidate.Present == 0 ||
                    candidate.VendorId != AmdVendorId)
                    continue;

                int familyResult = NativeMethods.ADL2_Adapter_ASICFamilyType_Get(
                    context,
                    candidate.AdapterIndex,
                    out AdlAsicFamilyType family,
                    out int validMask);
                if (familyResult != AdlSuccess)
                    continue;

                family &= (AdlAsicFamilyType)validMask;
                if ((family & AdlAsicFamilyType.Integrated) != 0)
                {
                    adapter = candidate;
                    return true;
                }
            }

            error = "ADL reported no integrated AMD adapter.";
            return false;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static void DestroyContext(nint context)
    {
        if (context == nint.Zero)
            return;

        try
        {
            NativeMethods.ADL2_Main_Control_Destroy(context);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportException("ADL context destroy", ex);
        }
    }

    private static nint MemoryAlloc(int size) => Marshal.AllocCoTaskMem(size);

    private static AdlOperationResult Success() => new(true, AdlSuccess, null);

    private static AdlOperationResult Failure(string message, int code = -1) =>
        new(false, code, message);

    [Flags]
    private enum AdlAsicFamilyType
    {
        Discrete = 1 << 0,
        Integrated = 1 << 1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdlAdapterInfo
    {
        private int Size;
        public int AdapterIndex;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        private string? Udid;

        private int BusNumber;
        private int DriverNumber;
        private int FunctionNumber;
        public int VendorId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        private string? AdapterName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        private string? DisplayName;

        public int Present;
        public int Exist;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        private string? DriverPath;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        private string? DriverPathExt;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)]
        private string? PnpString;

        private int OsDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlAdapterInfoArray
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = AdlMaxAdapters)]
        public AdlAdapterInfo[]? Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlSingleSensorData
    {
        public int Supported;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlPmLogDataOutput
    {
        private int Size;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = AdlPmLogMaxSensors)]
        public AdlSingleSensorData[]? Sensors;
    }

    private static class NativeMethods
    {
        private const string AtiAdlDll = "atiadlxx.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate nint AdlMainMemoryAlloc(int size);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ADL2_Main_Control_Create(
            AdlMainMemoryAlloc callback,
            int enumConnectedAdapters,
            out nint adlContextHandle);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Main_Control_Destroy(nint adlContextHandle);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_NumberOfAdapters_Get(
            nint adlContextHandle,
            out int numberOfAdapters);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_AdapterInfo_Get(
            nint adlContextHandle,
            nint info,
            int inputSize);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_ASICFamilyType_Get(
            nint adlContextHandle,
            int adapterIndex,
            out AdlAsicFamilyType asicFamilyType,
            out int validMask);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_FrameMetrics_Start(
            nint context,
            int adapterIndex,
            int vidPnSourceId);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_FrameMetrics_Stop(
            nint context,
            int adapterIndex,
            int vidPnSourceId);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_Adapter_FrameMetrics_Get(
            nint context,
            int adapterIndex,
            int vidPnSourceId,
            out float framesPerSecond);

        [DllImport(AtiAdlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ADL2_New_QueryPMLogData_Get(
            nint context,
            int adapterIndex,
            out AdlPmLogDataOutput logData);
    }
}
