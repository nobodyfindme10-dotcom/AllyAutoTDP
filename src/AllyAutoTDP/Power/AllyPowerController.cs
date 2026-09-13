using System.Globalization;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Logging;

namespace AllyAutoTDP.Power;

public static class AllyPowerEndpoints
{
    public const uint A0 = 0x001200A0;
    public const uint A3 = 0x001200A3;
    public const uint C1 = 0x001200C1;
}

public enum PowerControllerState
{
    Ready,
    Faulted,
    Restoring,
    ShutdownPending,
    Closed
}

public readonly record struct PowerReadback(int? A0, int? A3, int? C1)
{
    public bool IsComplete => A0.HasValue && A3.HasValue && C1.HasValue;
}

public sealed record EndpointWriteResult(
    string Endpoint,
    int? RequestedWatts,
    int? ResultCode,
    bool Success,
    bool Skipped,
    string? Reason);

public sealed record TdpWriteResult(
    bool Accepted,
    string? RejectionReason,
    IReadOnlyList<EndpointWriteResult> EndpointResults)
{
    public AsusModeReapplyResult? RecoveryResult { get; init; }

    public bool AllSucceeded =>
        Accepted &&
        EndpointResults.Count == 3 &&
        EndpointResults.All(result => result.Success);
}

public sealed record RestoreResult(AsusModeReapplyResult? ModeResult)
{
    public bool IsComplete => ModeResult?.IsSuccess == true && FailureReason is null;

    public string? FailureReason { get; init; }
}

public sealed class AllyPowerController
{
    public const int MinimumTdpWatts = 5;
    public const int MaximumTdpWatts = 30;
    private const string ClosedOperationReason =
        "operation rejected: controller closed";
    private const string RestoreModeRequiredReason =
        "Power writes blocked: valid --restore-mode is required.";

    private static readonly (string Label, uint Id)[] Endpoints =
    [
        ("A0", AllyPowerEndpoints.A0),
        ("A3", AllyPowerEndpoints.A3),
        ("C1", AllyPowerEndpoints.C1)
    ];

    private readonly object _operationGate = new();
    private readonly IAsusAcpi _acpi;
    private readonly bool _writeAuthorized;
    private readonly AsusModeReapply? _modeReapply;
    private readonly AppLogger? _logger;
    private PowerControllerState _state = PowerControllerState.Ready;

    public AllyPowerController(
        IAsusAcpi acpi,
        bool writeAuthorized,
        RestoreMode? restoreMode = null,
        AppLogger? logger = null)
    {
        _acpi = acpi;
        _writeAuthorized = writeAuthorized;
        _modeReapply = restoreMode.HasValue
            ? new AsusModeReapply(acpi, restoreMode.Value, logger)
            : null;
        _logger = logger;
    }

    public bool WriteAuthorized => _writeAuthorized;

    public RestoreMode? RestoreMode => _modeReapply?.Mode;

    public PowerControllerState State
    {
        get
        {
            lock (_operationGate)
                return _state;
        }
    }

    public bool PowerWritesEnabled
    {
        get
        {
            lock (_operationGate)
                return GetWriteBlockReason() is null;
        }
    }

    public PowerReadback ReadExperimental()
    {
        lock (_operationGate)
        {
            if (_state == PowerControllerState.Closed)
                throw new InvalidOperationException(ClosedOperationReason);

            return ReadExperimentalCore();
        }
    }

    public void BeginShutdown()
    {
        lock (_operationGate)
        {
            if (_state == PowerControllerState.Closed)
                return;

            _state = PowerControllerState.ShutdownPending;
        }
    }

    public void CompleteShutdown()
    {
        lock (_operationGate)
        {
            if (_state == PowerControllerState.Closed)
                return;

            _state = PowerControllerState.Closed;
        }
    }

    public TdpWriteResult SetFixedTdp(int watts)
    {
        lock (_operationGate)
        {
            string? blockedReason = GetWriteBlockReason();
            if (blockedReason is not null)
            {
                Log("WRITE", $"BLOCKED requested={watts} W reason={blockedReason}");
                return new TdpWriteResult(false, blockedReason, Array.Empty<EndpointWriteResult>());
            }

            if (watts < MinimumTdpWatts || watts > MaximumTdpWatts)
            {
                string reason = $"Allowed range: {MinimumTdpWatts}-{MaximumTdpWatts} W.";
                Log("WRITE", $"REJECTED requested={watts} W reason={reason}");
                return new TdpWriteResult(false, reason, Array.Empty<EndpointWriteResult>());
            }

            var results = new List<EndpointWriteResult>(Endpoints.Length);
            foreach ((string label, uint id) in Endpoints)
            {
                EndpointWriteResult result = WriteEndpoint(label, id, watts);
                results.Add(result);
                if (result.Success)
                    continue;

                Log("WRITE", $"SET TDP FAILED endpoint={label}");
                AsusModeReapplyResult? recovery = ReapplyRestoreMode();
                if (recovery?.IsSuccess != true)
                {
                    _state = PowerControllerState.Faulted;
                    Log("CONTROLLER_STATE", "FAULTED");
                }

                return new TdpWriteResult(true, null, results)
                {
                    RecoveryResult = recovery
                };
            }

            return new TdpWriteResult(true, null, results);
        }
    }

    public RestoreResult Restore()
    {
        lock (_operationGate)
        {
            if (_state == PowerControllerState.Closed)
            {
                Log("RESTORE", ClosedOperationReason);
                return new RestoreResult(null)
                {
                    FailureReason = ClosedOperationReason
                };
            }

            PowerControllerState previousState = _state;
            _state = PowerControllerState.Restoring;

            AsusModeReapplyResult? modeResult = ReapplyRestoreMode();
            if (modeResult?.IsSuccess == true)
            {
                _state = previousState switch
                {
                    PowerControllerState.ShutdownPending => PowerControllerState.ShutdownPending,
                    PowerControllerState.Faulted => PowerControllerState.Faulted,
                    _ => PowerControllerState.Ready
                };
                return new RestoreResult(modeResult);
            }

            _state = previousState == PowerControllerState.ShutdownPending
                ? PowerControllerState.ShutdownPending
                : PowerControllerState.Faulted;
            string reason = modeResult?.FailureReason ?? RestoreModeRequiredReason;
            Log("RESTORE", $"ASUS MODE REAPPLY FAILED reason={reason}");
            return new RestoreResult(modeResult) { FailureReason = reason };
        }
    }

    public static bool TryParseTdpCommand(string input, out int watts)
    {
        string value = input.Trim();
        if (value.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            value = value[4..].Trim();

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out watts);
    }

    private PowerReadback ReadExperimentalCore()
    {
        int? a0 = ReadEndpoint("A0", AllyPowerEndpoints.A0);
        int? a3 = ReadEndpoint("A3", AllyPowerEndpoints.A3);
        int? c1 = ReadEndpoint("C1", AllyPowerEndpoints.C1);
        return new PowerReadback(a0, a3, c1);
    }

    private string? GetWriteBlockReason()
    {
        return _state switch
        {
            PowerControllerState.Faulted => "Power writes blocked: controller is FAULTED.",
            PowerControllerState.Closed => ClosedOperationReason,
            PowerControllerState.Restoring => "Power writes blocked: controller is RESTORING.",
            PowerControllerState.ShutdownPending => "Power writes blocked: controller shutdown is pending.",
            _ when !_writeAuthorized => "Power writes blocked: model is not an authorized RC71 device.",
            _ when !_acpi.IsConnected => "Power writes blocked: ATKACPI is not connected.",
            _ when _modeReapply is null => RestoreModeRequiredReason,
            _ => null
        };
    }

    private AsusModeReapplyResult? ReapplyRestoreMode()
    {
        if (_modeReapply is null)
        {
            Log("RESTORE_MODE", $"FAILED reason={RestoreModeRequiredReason}");
            return null;
        }

        return _modeReapply.Reapply();
    }

    private int? ReadEndpoint(string label, uint id)
    {
        AcpiReadResult result = _acpi.DeviceGet(id);
        int? value = result.IsSuccess && result.Value.HasValue && result.Value.Value >= 0
            ? result.Value.Value
            : null;

        if (!value.HasValue)
        {
            string error = result.ErrorMessage ?? "non-interpretable result";
            Log("ACPI_READBACK", $"{label} = UNKNOWN error={error}");
        }

        return value;
    }

    private EndpointWriteResult WriteEndpoint(string label, uint id, int watts) =>
        SetEndpoint("WRITE", label, id, watts, $"{label} requested={watts} W");

    private EndpointWriteResult SetEndpoint(
        string category,
        string label,
        uint id,
        int watts,
        string operation)
    {
        try
        {
            AcpiWriteResult result = _acpi.DeviceSet(id, watts);
            if (!result.IsSuccess)
            {
                Log(
                    category,
                    $"{operation} result=FAIL " +
                    $"code={result.ResultCode} " +
                    $"error={result.ErrorMessage ?? "unknown"}");
            }

            string? reason = result.IsSuccess
                ? null
                : result.ErrorMessage ?? "unknown";
            EndpointWriteResult endpointResult = new(
                label,
                watts,
                result.ResultCode,
                result.IsSuccess,
                false,
                reason);
            return endpointResult;
        }
        catch (Exception ex)
        {
            Log(
                category,
                $"{operation} result=FAIL exception={ex.GetType().Name}: {ex.Message}");
            return new EndpointWriteResult(label, watts, null, false, false, ex.Message);
        }
    }

    private void Log(string category, string message) => _logger?.Log(category, message);
}
