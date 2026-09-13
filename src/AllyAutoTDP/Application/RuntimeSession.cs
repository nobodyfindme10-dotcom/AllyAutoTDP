using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Application;

public enum RuntimeSessionState
{
    Running,
    ShutdownPending,
    Closed
}

public sealed class RuntimeSession : IDisposable
{
    private readonly object _sessionGate = new();
    private readonly AllyPowerController _controller;
    private readonly PowerWatch? _watch;
    private readonly AutoTdpLoop? _autoLoop;
    private readonly AppLogger? _logger;
    private CancellationTokenSource? _watchCancellation;
    private Task? _watchTask;
    private bool _quitRequested;
    private RuntimeSessionState _state = RuntimeSessionState.Running;
    private RestoreResult? _lastRestoreResult;

    public RuntimeSession(
        AllyPowerController controller,
        PowerWatch watch,
        AppLogger? logger = null)
    {
        _controller = controller;
        _watch = watch;
        _logger = logger;
    }

    public RuntimeSession(
        AllyPowerController controller,
        AutoTdpLoop autoLoop,
        AppLogger? logger = null,
        Action<AutoTdpStatus>? onStatus = null)
    {
        _controller = controller;
        _autoLoop = autoLoop;
        _logger = logger;
        _autoLoop.Start(onStatus);
    }

    public bool QuitRequested
    {
        get
        {
            lock (_sessionGate)
                return _quitRequested;
        }
    }

    public bool WatchRunning
    {
        get
        {
            lock (_sessionGate)
            {
                return _autoLoop?.IsRunning == true ||
                    _watchTask is { IsCompleted: false };
            }
        }
    }

    public RuntimeSessionState State
    {
        get
        {
            lock (_sessionGate)
                return _state;
        }
    }

    public void ToggleWatch(Action<PowerReadback> onSample)
    {
        lock (_sessionGate)
        {
            if (_autoLoop is not null || _state != RuntimeSessionState.Running)
                return;

            if (WatchRunning)
            {
                StopLoop();
                return;
            }

            _watchCancellation = new CancellationTokenSource();
            _watchTask = _watch!.RunAsync(_watchCancellation.Token, onSample);
        }
    }

    public void RequestQuit() => Quit();

    public RestoreResult Restore()
    {
        lock (_sessionGate)
        {
            try
            {
                StopLoop();
            }
            catch (Exception ex)
            {
                SafeLogError("AutoTDP/watch stop failed before manual restore", ex);
            }

            return RestoreCore();
        }
    }

    public RestoreResult Quit()
    {
        lock (_sessionGate)
        {
            if (_state == RuntimeSessionState.Closed &&
                _lastRestoreResult is not null)
            {
                return _lastRestoreResult;
            }

            _quitRequested = false;
            _state = RuntimeSessionState.ShutdownPending;

            try
            {
                _controller.BeginShutdown();
            }
            catch (Exception ex)
            {
                SafeLogError("BeginShutdown failed", ex);
            }

            try
            {
                StopLoop();
            }
            catch (Exception ex)
            {
                SafeLogError("AutoTDP/watch stop failed", ex);
            }

            RestoreResult result = RestoreCore();
            if (result.IsComplete)
            {
                try
                {
                    _controller.CompleteShutdown();
                    if (_controller.State != PowerControllerState.Closed)
                    {
                        throw new InvalidOperationException(
                            "controller did not enter CLOSED state");
                    }

                    _state = RuntimeSessionState.Closed;
                    _quitRequested = true;
                }
                catch (Exception ex)
                {
                    SafeLogError("CompleteShutdown failed", ex);
                    _state = RuntimeSessionState.ShutdownPending;
                    _quitRequested = false;
                    result = result with
                    {
                        FailureReason = $"shutdown close failed: {ex.Message}"
                    };
                }
            }
            else
            {
                _state = RuntimeSessionState.ShutdownPending;
                _quitRequested = false;
                SafeLog("RESTORE", "INCOMPLETE; session remains SHUTDOWN_PENDING");
            }

            _lastRestoreResult = result;
            return result;
        }
    }

    public void Dispose()
    {
        lock (_sessionGate)
        {
            if (_state == RuntimeSessionState.Closed ||
                (_lastRestoreResult is not null && !_lastRestoreResult.IsComplete))
            {
                return;
            }
        }

        try
        {
            Quit();
        }
        catch (Exception ex)
        {
            SafeLogError("Session dispose failed", ex);
        }
    }

    private RestoreResult RestoreCore()
    {
        try
        {
            RestoreResult result = _controller.Restore();
            _lastRestoreResult = result;
            return result;
        }
        catch (Exception ex)
        {
            SafeLogError("Restore failed", ex);
            RestoreResult result = new((AsusModeReapplyResult?)null)
            {
                FailureReason = ex.Message
            };
            _lastRestoreResult = result;
            return result;
        }
    }

    private void StopLoop()
    {
        if (_autoLoop is not null)
        {
            _autoLoop.Stop();
            return;
        }

        CancellationTokenSource? cancellation = _watchCancellation;
        Task? task = _watchTask;
        if (cancellation is null && task is null)
            return;

        try
        {
            cancellation?.Cancel();
            task?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (
            cancellation?.IsCancellationRequested == true)
        {
        }
        finally
        {
            _watchCancellation = null;
            _watchTask = null;
            cancellation?.Dispose();
        }
    }

    private void SafeLog(string category, string message)
    {
        try
        {
            _logger?.Log(category, message);
        }
        catch
        {
        }
    }

    private void SafeLogError(string message, Exception exception)
    {
        try
        {
            _logger?.Error(message, exception);
        }
        catch
        {
        }
    }
}
