namespace AllyAutoTDP.Hardware.AMD;

public readonly record struct FpsReading(
    bool IsValid,
    float? Value,
    int? ErrorCode,
    string? ErrorMessage)
{
    public static FpsReading Valid(float value) =>
        new(true, value, null, null);

    public static FpsReading Invalid(int? errorCode, string message) =>
        new(false, null, errorCode, message);
}

public sealed class AmdFrameMetricsReader : IDisposable
{
    private const float MaxPlausibleFps = 1000f;
    private readonly IAmdAdl _adl;
    private bool _started;
    private bool _disposed;

    public AmdFrameMetricsReader(IAmdAdl adl)
    {
        _adl = adl ?? throw new ArgumentNullException(nameof(adl));
    }

    public bool IsStarted => _started && !_disposed;

    public AdlOperationResult StartFPS()
    {
        if (_disposed)
            return new(false, -1, "FPS reader is disposed.");

        if (_started)
            return new(true, AmdAdlNative.AdlSuccess, null);

        AdlOperationResult result = _adl.StartFrameMetrics();
        _started = result.IsSuccess;
        return result;
    }

    public FpsReading GetFPS()
    {
        if (_disposed)
            return FpsReading.Invalid(-1, "FPS reader is disposed.");

        if (!_started)
            return FpsReading.Invalid(-1, "FrameMetrics is not started.");

        AdlFpsResult result = _adl.GetFrameMetrics();
        if (!result.IsSuccess)
        {
            return FpsReading.Invalid(
                result.ErrorCode,
                result.ErrorMessage ?? "ADL FPS read failed.");
        }

        if (!float.IsFinite(result.Value) ||
            result.Value <= 0 ||
            result.Value > MaxPlausibleFps)
        {
            return FpsReading.Invalid(
                result.ErrorCode,
                $"ADL FPS value is invalid: {result.Value}");
        }

        return FpsReading.Valid(result.Value);
    }

    public AdlOperationResult StopFPS()
    {
        if (_disposed)
            return new(false, -1, "FPS reader is disposed.");

        if (!_started)
            return new(true, AmdAdlNative.AdlSuccess, null);

        AdlOperationResult result = _adl.StopFrameMetrics();
        _started = false;
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            StopFPS();
        }
        finally
        {
            _disposed = true;
        }
    }
}
