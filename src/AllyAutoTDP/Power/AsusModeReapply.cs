using AllyAutoTDP.Hardware;
using AllyAutoTDP.Logging;

namespace AllyAutoTDP.Power;

public enum RestoreMode
{
    Balanced = 0,
    Turbo = 1,
    Silent = 2
}

public static class RestoreModeParser
{
    public const string ArgumentName = "--restore-mode";

    public static bool TryParse(
        string[] args,
        out RestoreMode mode,
        out string? error)
    {
        mode = default;
        error = null;
        bool found = false;

        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], ArgumentName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unknown argument '{args[index]}'.";
                return false;
            }

            if (found)
            {
                error = $"Argument '{ArgumentName}' may be provided only once.";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = $"Argument '{ArgumentName}' requires silent, balanced or turbo.";
                return false;
            }

            string value = args[++index];
            if (!TryParseValue(value, out mode))
            {
                error = $"Invalid restore mode '{value}'. Use silent, balanced or turbo.";
                return false;
            }

            found = true;
        }

        if (!found)
        {
            error = $"Argument '{ArgumentName}' is required.";
            return false;
        }

        return true;
    }

    public static bool TryParseValue(string value, out RestoreMode mode)
    {
        if (string.Equals(value, "silent", StringComparison.OrdinalIgnoreCase))
        {
            mode = RestoreMode.Silent;
            return true;
        }

        if (string.Equals(value, "balanced", StringComparison.OrdinalIgnoreCase))
        {
            mode = RestoreMode.Balanced;
            return true;
        }

        if (string.Equals(value, "turbo", StringComparison.OrdinalIgnoreCase))
        {
            mode = RestoreMode.Turbo;
            return true;
        }

        mode = default;
        return false;
    }

    public static string Format(RestoreMode mode) =>
        $"{mode} ({(int)mode})";
}

public sealed record AsusModeReapplyResult(
    RestoreMode Mode,
    bool IsSuccess,
    int ResultCode,
    string? FailureReason)
{
    public int ModeValue => (int)Mode;
}

public sealed class AsusModeReapply
{
    public const uint PerformanceModeEndpoint = 0x00120075;

    private readonly IAsusAcpi _acpi;
    private readonly RestoreMode _mode;
    private readonly AppLogger? _logger;

    public AsusModeReapply(
        IAsusAcpi acpi,
        RestoreMode mode,
        AppLogger? logger = null)
    {
        _acpi = acpi;
        _mode = mode;
        _logger = logger;
    }

    public RestoreMode Mode => _mode;

    public AsusModeReapplyResult Reapply()
    {
        if (!_acpi.IsConnected)
        {
            const string reason = "ATKACPI is not connected.";
            Log($"mode={RestoreModeParser.Format(_mode)} result=FAILED reason={reason}");
            return new AsusModeReapplyResult(_mode, false, -1, reason);
        }

        try
        {
            int modeValue = (int)_mode;
            AcpiWriteResult result = _acpi.DeviceSet(PerformanceModeEndpoint, modeValue);
            if (result.IsSuccess)
            {
                return new AsusModeReapplyResult(_mode, true, result.ResultCode, null);
            }

            string reason = result.ErrorMessage ?? "ASUS PerformanceMode write failed.";
            Log(
                $"mode={RestoreModeParser.Format(_mode)} result=FAILED " +
                $"code={result.ResultCode} reason={reason}");
            return new AsusModeReapplyResult(_mode, false, result.ResultCode, reason);
        }
        catch (Exception ex)
        {
            Log(
                $"mode={RestoreModeParser.Format(_mode)} result=FAILED " +
                $"exception={ex.GetType().Name}: {ex.Message}");
            return new AsusModeReapplyResult(_mode, false, -1, ex.Message);
        }
    }

    private void Log(string message) => _logger?.Log("RESTORE_MODE", message);
}
