namespace AllyAutoTDP.Hardware.AMD;

public readonly record struct PowerReading(
    bool IsValid,
    int? Watts,
    int? ErrorCode,
    string? ErrorMessage)
{
    public static PowerReading Valid(int watts) =>
        new(true, watts, null, null);

    public static PowerReading Invalid(int? errorCode, string message) =>
        new(false, null, errorCode, message);
}

public sealed class AmdPowerReader : IDisposable
{
    private readonly IAmdAdl _adl;
    private bool _disposed;

    public AmdPowerReader(IAmdAdl adl)
    {
        _adl = adl ?? throw new ArgumentNullException(nameof(adl));
    }

    public PowerReading GetPower()
    {
        if (_disposed)
            return PowerReading.Invalid(-1, "Power reader is disposed.");

        AdlPowerResult result = _adl.GetIgpuAsicPower();
        if (!result.IsSuccess)
        {
            return PowerReading.Invalid(
                result.ErrorCode,
                result.ErrorMessage ?? "ADL ASIC power read failed.");
        }

        if (result.Value <= 0)
        {
            return PowerReading.Invalid(
                result.ErrorCode,
                $"ADL ASIC power value is invalid: {result.Value} W");
        }

        return PowerReading.Valid(result.Value);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
