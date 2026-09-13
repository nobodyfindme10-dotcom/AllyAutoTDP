using AllyAutoTDP.Configuration;
using AllyAutoTDP.Windows;

namespace AllyAutoTDP.Application;

public sealed class GameSessionManager : IDisposable
{
    private const int RequiredTransitionObservations = 2;

    private readonly IGameProfileLookup _profileLookup;
    private readonly IAutoTdpRuntime _runtime;
    private readonly IProcessLifetimeChecker _processLifetimeChecker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PendingForegroundCandidate? _pendingCandidate;
    private ActiveGameSession? _activeSession;
    private bool _autoTdpEnabled;
    private GameSessionState _state;
    private bool _disposed;

    public GameSessionManager(
        IGameProfileLookup profileLookup,
        IAutoTdpRuntime runtime,
        IProcessLifetimeChecker processLifetimeChecker,
        bool autoTdpEnabled = true)
    {
        _profileLookup = profileLookup ??
            throw new ArgumentNullException(nameof(profileLookup));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _processLifetimeChecker = processLifetimeChecker ??
            throw new ArgumentNullException(nameof(processLifetimeChecker));
        _autoTdpEnabled = autoTdpEnabled;
        _state = autoTdpEnabled
            ? GameSessionState.Ready
            : GameSessionState.Disabled;
    }

    public bool AutoTdpEnabled => _autoTdpEnabled;

    public GameSessionState State => _state;

    public ActiveGameSession? ActiveSession => _activeSession;

    public string? LastErrorMessage { get; private set; }

    public void ObserveForeground(ForegroundApplicationSnapshot? observation)
    {
        ExecuteSerialized(() => ObserveForegroundCore(observation));
    }

    public void CheckActiveProcess()
    {
        ExecuteSerialized(CheckActiveProcessCore);
    }

    public void SetAutoTdpEnabled(bool enabled)
    {
        ExecuteSerialized(() => SetAutoTdpEnabledCore(enabled));
    }

    public AutoTdpRuntimeResult StopActiveSession()
    {
        AutoTdpRuntimeResult result = AutoTdpRuntimeResult.Success();
        ExecuteSerialized(() =>
        {
            if (_activeSession is not null)
                result = StopAndRestoreCore();
        });
        return result;
    }

    public void ReportRuntimeState(GameSessionState runtimeState)
    {
        ExecuteSerialized(() =>
        {
            if (_activeSession is null)
            {
                return;
            }

            if (runtimeState is GameSessionState.Active or
                GameSessionState.Paused or
                GameSessionState.MaxLimit or
                GameSessionState.Error)
            {
                _state = runtimeState;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ExecuteSerialized(() =>
        {
            if (_activeSession is not null)
            {
                StopAndRestoreCore();
            }
        });
        _disposed = true;
        _gate.Dispose();
    }

    private void ObserveForegroundCore(ForegroundApplicationSnapshot? observation)
    {
        if (!_autoTdpEnabled)
        {
            ClearPendingCandidate();
            if (_activeSession is null)
            {
                _state = GameSessionState.Disabled;
            }

            return;
        }

        if (observation is null)
        {
            ClearPendingCandidate();
            SetReadyIfNoActiveSession();
            return;
        }

        GameProfile? profile = _profileLookup.FindByExecutablePath(
            observation.ExecutablePath);
        if (profile is null || !profile.Enabled)
        {
            ClearPendingCandidate();
            SetReadyIfNoActiveSession();
            return;
        }

        GameProcessIdentity identity = GameProcessIdentity.FromSnapshot(observation);
        if (_activeSession is null)
        {
            if (IsFailedCandidate(identity))
            {
                return;
            }

            ClearPendingCandidate();
            StartCore(profile, identity);
            return;
        }

        if (SameInstance(_activeSession.ProcessIdentity, identity))
        {
            ClearPendingCandidate();
            return;
        }

        ObserveTransitionCandidate(profile, identity);
    }

    private void CheckActiveProcessCore()
    {
        if (_activeSession is null)
        {
            SetReadyIfNoActiveSession();
            return;
        }

        ProcessLifetimeStatus lifetime = _processLifetimeChecker.Check(
            _activeSession.ProcessIdentity);
        switch (lifetime)
        {
            case ProcessLifetimeStatus.Alive:
                return;
            case ProcessLifetimeStatus.Unknown:
                SetError("The active game process lifetime could not be verified.");
                return;
            case ProcessLifetimeStatus.Terminated:
                ClearPendingCandidate();
                StopAndRestoreCore();
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void SetAutoTdpEnabledCore(bool enabled)
    {
        _autoTdpEnabled = enabled;
        ClearPendingCandidate();

        if (enabled)
        {
            if (_activeSession is null)
            {
                _state = GameSessionState.Ready;
                LastErrorMessage = null;
            }

            return;
        }

        if (_activeSession is null)
        {
            _state = GameSessionState.Disabled;
            LastErrorMessage = null;
            return;
        }

        AutoTdpRuntimeResult result = StopAndRestoreCore();
        if (result.IsSuccess)
        {
            _state = GameSessionState.Disabled;
            LastErrorMessage = null;
        }
    }

    private void ObserveTransitionCandidate(
        GameProfile profile,
        GameProcessIdentity identity)
    {
        if (_pendingCandidate is null ||
            !SameInstance(_pendingCandidate.Identity, identity))
        {
            _pendingCandidate = new PendingForegroundCandidate(
                profile,
                identity,
                1);
            return;
        }

        _pendingCandidate = _pendingCandidate with
        {
            Observations = _pendingCandidate.Observations + 1,
        };
        if (_pendingCandidate.Observations < RequiredTransitionObservations)
        {
            return;
        }

        PendingForegroundCandidate candidate = _pendingCandidate;
        ClearPendingCandidate();
        AutoTdpRuntimeResult restoreResult = StopAndRestoreCore();
        if (!restoreResult.IsSuccess)
        {
            return;
        }

        StartCore(candidate.Profile, candidate.Identity);
    }

    private void StartCore(GameProfile profile, GameProcessIdentity identity)
    {
        AutoTdpRuntimeResult result;
        try
        {
            result = _runtime.Start(profile);
        }
        catch (Exception exception)
        {
            result = AutoTdpRuntimeResult.Failure(exception.Message);
        }

        if (!result.IsSuccess)
        {
            SetError(result.ErrorMessage ?? "AutoTDP runtime start failed.");
            _pendingCandidate = new PendingForegroundCandidate(
                profile,
                identity,
                RequiredTransitionObservations);
            return;
        }

        _activeSession = new ActiveGameSession(profile, identity);
        _state = GameSessionState.Active;
        LastErrorMessage = null;
    }

    private AutoTdpRuntimeResult StopAndRestoreCore()
    {
        _state = GameSessionState.Restoring;
        AutoTdpRuntimeResult result;
        try
        {
            result = _runtime.StopAndRestore();
        }
        catch (Exception exception)
        {
            result = AutoTdpRuntimeResult.Failure(exception.Message);
        }

        if (!result.IsSuccess)
        {
            SetError(result.ErrorMessage ?? "AutoTDP runtime restore failed.");
            return result;
        }

        _activeSession = null;
        _state = _autoTdpEnabled
            ? GameSessionState.Ready
            : GameSessionState.Disabled;
        LastErrorMessage = null;
        return result;
    }

    private bool IsFailedCandidate(GameProcessIdentity identity) =>
        _state == GameSessionState.Error &&
        _pendingCandidate is not null &&
        SameInstance(_pendingCandidate.Identity, identity);

    private void SetReadyIfNoActiveSession()
    {
        if (_activeSession is null)
        {
            _state = _autoTdpEnabled
                ? GameSessionState.Ready
                : GameSessionState.Disabled;
        }
    }

    private void SetError(string errorMessage)
    {
        _state = GameSessionState.Error;
        LastErrorMessage = errorMessage;
    }

    private void ClearPendingCandidate()
    {
        _pendingCandidate = null;
    }

    private void ExecuteSerialized(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool SameInstance(
        GameProcessIdentity first,
        GameProcessIdentity second) =>
        first.ProcessId == second.ProcessId &&
        first.ProcessStartTime == second.ProcessStartTime &&
        string.Equals(
            first.ExecutablePath,
            second.ExecutablePath,
            StringComparison.OrdinalIgnoreCase);

    private sealed record PendingForegroundCandidate(
        GameProfile Profile,
        GameProcessIdentity Identity,
        int Observations);
}
