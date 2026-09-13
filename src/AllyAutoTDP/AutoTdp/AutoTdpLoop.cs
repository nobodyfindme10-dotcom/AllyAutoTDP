using AllyAutoTDP.Hardware;
using AllyAutoTDP.Hardware.AMD;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;
using AllyAutoTDP;

namespace AllyAutoTDP.AutoTdp;

public readonly record struct AutoTdpStatus(
    FpsReading Fps,
    PowerReading Power,
    PowerSourceReading PowerSource,
    int TargetFps,
    int CurrentTdp,
    int EffectiveMax,
    AutoTdpState State,
    string? Message);

public readonly record struct AutoTdpTickResult(
    AutoTdpStatus Status,
    bool WriteAttempted,
    bool RefreshWrite,
    bool ShouldStop,
    bool RestoreRequired,
    string? StopReason);

public sealed class AutoTdpLoop : IDisposable
{
    public static readonly TimeSpan TickInterval =
        TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan RefreshInterval =
        TimeSpan.FromMilliseconds(2000);

    private readonly AutoTdpController _controller;
    private readonly AutoTdpOptions _options;
    private readonly AmdFrameMetricsReader _fpsReader;
    private readonly AmdPowerReader _powerReader;
    private readonly IPowerSourceReader _powerSource;
    private readonly Func<int, TdpWriteResult> _writeTdp;
    private readonly Func<RestoreResult> _restore;
    private readonly AppLogger? _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private Action<AutoTdpStatus>? _onStatus;
    private DateTimeOffset? _lastWriteAt;
    private bool _hasWritten;
    private bool _pendingRestore;
    private bool _disposed;

    public AutoTdpLoop(
        AutoTdpController controller,
        AutoTdpOptions options,
        AmdFrameMetricsReader fpsReader,
        AmdPowerReader powerReader,
        IPowerSourceReader powerSource,
        Func<int, TdpWriteResult> writeTdp,
        Func<RestoreResult> restore,
        AppLogger? logger = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fpsReader = fpsReader ?? throw new ArgumentNullException(nameof(fpsReader));
        _powerReader = powerReader ?? throw new ArgumentNullException(nameof(powerReader));
        _powerSource = powerSource ?? throw new ArgumentNullException(nameof(powerSource));
        _writeTdp = writeTdp ?? throw new ArgumentNullException(nameof(writeTdp));
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _logger = logger;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _task is { IsCompleted: false };
        }
    }

    public RestoreResult? LastRestoreResult { get; private set; }

    public void Start(Action<AutoTdpStatus>? onStatus = null)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoTdpLoop));
            if (_task is { IsCompleted: false })
                return;

            _onStatus = onStatus;
            _cancellation = new CancellationTokenSource();
            _task = Task.Run(
                () => RunAsync(_cancellation.Token),
                CancellationToken.None);
        }

    }

    public void Stop()
    {
        Task? task;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _cancellation;
            task = _task;
        }

        if (cancellation is null && task is null)
            return;

        cancellation?.Cancel();
        try
        {
            task?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
        }
        finally
        {
            lock (_gate)
            {
                _task = null;
                _cancellation = null;
            }

            cancellation?.Dispose();
        }

    }

    public AutoTdpTickResult ProcessSample(
        FpsReading fps,
        PowerReading power,
        PowerSourceReading powerSource,
        DateTimeOffset now)
    {
        PowerSourceKind source = powerSource.Source;
        TdpRange range = _options.GetEffectiveRange(source);
        int beforeLimitsTdp = _controller.CurrentTdp;
        _controller.UpdateLimits(range);
        bool safetyClamp = _controller.CurrentTdp != beforeLimitsTdp;

        AutoTdpDecision decision = _controller.Tick(fps, power, now);
        bool calculatedChange = _controller.CurrentTdp != beforeLimitsTdp;
        bool refreshDue = _hasWritten &&
            _lastWriteAt.HasValue &&
            now - _lastWriteAt.Value >= RefreshInterval;
        bool writeRequired = safetyClamp || calculatedChange || !_hasWritten || refreshDue;
        bool refreshWrite = writeRequired &&
            !safetyClamp &&
            !calculatedChange &&
            _hasWritten;
        bool writeAttempted = false;
        bool shouldStop = decision.ShouldStop;
        bool restoreRequired = false;
        string? stopReason = decision.ShouldStop ? decision.Reason : null;

        if (writeRequired && !decision.ShouldStop)
        {
            writeAttempted = true;
            TdpWriteResult result = _writeTdp(_controller.CurrentTdp);
            if (result.AllSucceeded)
            {
                _hasWritten = true;
                _lastWriteAt = now;
            }
            else
            {
                shouldStop = true;
                stopReason = "TDP write failed.";
                if (result.RecoveryResult is null)
                    restoreRequired = true;
                Log("AUTOTDP", $"STOP reason={stopReason}");
            }
        }

        if (decision.ShouldStop)
        {
            restoreRequired = true;
            Log("AUTOTDP", $"STOP reason={decision.Reason}");
        }

        if (restoreRequired)
            _pendingRestore = true;

        AutoTdpState state = shouldStop && !decision.ShouldStop
            ? AutoTdpState.Faulted
            : decision.State;
        string? message = powerSource.IsKnown
            ? decision.Reason
            : powerSource.ErrorMessage ?? "Unknown power source; 25 W safety cap applied.";
        AutoTdpStatus status = new(
            fps,
            power,
            powerSource,
            _controller.TargetFps,
            _controller.CurrentTdp,
            _controller.EffectiveMax,
            state,
            message);

        return new(
            status,
            writeAttempted,
            refreshWrite,
            shouldStop,
            restoreRequired,
            stopReason);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        bool started = false;
        try
        {
            AdlOperationResult startResult = _fpsReader.StartFPS();
            if (!startResult.IsSuccess)
            {
                _pendingRestore = true;
                Log(
                    "FPS",
                    $"START FAILED code={startResult.ErrorCode} " +
                    $"reason={startResult.ErrorMessage ?? "unknown"}");
                return;
            }

            started = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                FpsReading fps = _fpsReader.GetFPS();
                PowerReading power = fps.IsValid
                    ? _powerReader.GetPower()
                    : PowerReading.Invalid(null, "FPS invalid; power read skipped.");
                PowerSourceReading source = _powerSource.Read();
                AutoTdpTickResult result = ProcessSample(fps, power, source, now);
                try
                {
                    _onStatus?.Invoke(result.Status);
                }
                catch (Exception ex)
                {
                    Log("AUTOTDP", $"status callback failed: {ex.Message}");
                }

                if (result.ShouldStop)
                    break;

                await Task.Delay(TickInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _pendingRestore = true;
            StartupDiagnostics.ReportException("AutoTDP loop", ex);
            Log("AUTOTDP", $"LOOP FAILED exception={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (started)
            {
                AdlOperationResult stopResult = _fpsReader.StopFPS();
                if (!stopResult.IsSuccess)
                {
                    Log(
                        "FPS",
                        $"STOP FAILED code={stopResult.ErrorCode} " +
                        $"reason={stopResult.ErrorMessage ?? "unknown"}");
                }
            }

            if (_pendingRestore && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    LastRestoreResult = _restore();
                    if (!LastRestoreResult.IsComplete)
                        Log("RESTORE", "AUTOTDP STOP RESTORE FAILED");
                }
                catch (Exception ex)
                {
                    Log("RESTORE", $"AUTOTDP STOP RESTORE THREW: {ex.Message}");
                }
                finally
                {
                    _pendingRestore = false;
                }
            }
        }
    }

    private void Log(string category, string message) => _logger?.Log(category, message);
}
