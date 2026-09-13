using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;
using AllyAutoTDP.Windows;

namespace AllyAutoTDP.Application;

public sealed record AllyApplicationSnapshot(
    bool AutoTdpEnabled,
    bool StartWithWindows,
    GameSessionState SessionState,
    string? SessionErrorMessage,
    ForegroundApplicationSnapshot? DetectedApplication,
    ForegroundApplicationSnapshot? CandidateApplication,
    InvokingApplicationSnapshot? InvokingApplication,
    ActiveGameSession? ActiveSession,
    AutoTdpRuntimeSnapshot Runtime,
    PowerLimits PowerLimits,
    ProfileDefaults ProfileDefaults,
    IReadOnlyList<GameProfile> GameProfiles,
    string? AutoStartErrorMessage,
    string? LastOperationError);

public sealed class AllyApplicationController : IDisposable
{
    private readonly object _gate = new();
    private readonly ConfigurationStore _configurationStore;
    private readonly GameProfileService _profileService;
    private readonly IForegroundApplicationDetector _foregroundDetector;
    private readonly GameSessionManager _sessionManager;
    private readonly IProcessLifetimeChecker _processLifetimeChecker;
    private readonly IAutoTdpRuntime _runtime;
    private readonly AppLogger _logger;
    private readonly IAutoStartService _autoStartService;
    private ForegroundApplicationSnapshot? _detectedApplication;
    private ForegroundApplicationSnapshot? _candidateApplication;
    private InvokingApplicationSnapshot? _invokingApplication;
    private string? _autoStartErrorMessage;
    private string? _lastOperationError;
    private bool _disposed;

    public AllyApplicationController(
        ConfigurationStore configurationStore,
        GameProfileService profileService,
        IForegroundApplicationDetector foregroundDetector,
        GameSessionManager sessionManager,
        IAutoTdpRuntime runtime,
        AppLogger logger,
        IAutoStartService? autoStartService = null,
        IProcessLifetimeChecker? processLifetimeChecker = null)
    {
        _configurationStore = configurationStore ??
            throw new ArgumentNullException(nameof(configurationStore));
        _profileService = profileService ??
            throw new ArgumentNullException(nameof(profileService));
        _foregroundDetector = foregroundDetector ??
            throw new ArgumentNullException(nameof(foregroundDetector));
        _sessionManager = sessionManager ??
            throw new ArgumentNullException(nameof(sessionManager));
        _processLifetimeChecker = processLifetimeChecker ??
            new WindowsProcessLifetimeChecker();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _autoStartService = autoStartService ?? new NoOpAutoStartService();
    }

    public static AllyApplicationController CreateDefault()
    {
        var logger = new AppLogger();
        var configurationStore = new ConfigurationStore();
        var profileService = new GameProfileService(configurationStore);
        var detector = new ForegroundApplicationDetector(
            new User32ForegroundWindowApi(),
            new WindowsProcessSnapshotProvider());
        AllyDetectionResult detection = new AllyDetector().Detect();
        var runtime = new RealAutoTdpRuntime(
            detection,
            () => configurationStore.Current.PowerLimits,
            logger);
        var processLifetimeChecker = new WindowsProcessLifetimeChecker();
        var sessionManager = new GameSessionManager(
            profileService,
            runtime,
            processLifetimeChecker,
            configurationStore.Current.AutoTdpEnabled);

        var controller = new AllyApplicationController(
            configurationStore,
            profileService,
            detector,
            sessionManager,
            runtime,
            logger,
            new WindowsTaskSchedulerAutoStartService(
                new ComTaskScheduler(),
                logger),
            processLifetimeChecker);
        controller.SynchronizeConfiguredAutoStart();
        return controller;
    }

    public AllyApplicationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return CreateSnapshot();
        }
    }

    public void LogUiWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (_gate)
        {
            _logger.Log("UI", message);
        }
    }

    public void Poll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                ForegroundDetectionResult result = _foregroundDetector.Detect();
                _detectedApplication = result.Application;
                UpdateCandidateApplication(result.Application);
                _sessionManager.ObserveForeground(_detectedApplication);
                _sessionManager.CheckActiveProcess();

                AutoTdpRuntimeSnapshot runtime = _runtime.GetSnapshot();
                if (runtime.Status is AutoTdpStatus status &&
                    _sessionManager.ActiveSession is not null)
                {
                    _sessionManager.ReportRuntimeState(
                        MapRuntimeState(status.State));
                }

            }
            catch (Exception exception)
            {
                SetOperationError(exception);
            }
        }
    }

    public bool CaptureInvokingApplication()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            ForegroundDetectionResult result = _foregroundDetector.Detect();
            if (result.Application is null || result.WindowHandle == nint.Zero)
            {
                _invokingApplication = null;
                return false;
            }

            _invokingApplication = new(
                result.WindowHandle,
                result.Application);
            UpdateCandidateApplication(
                result.Application,
                allowReplacement: true);
            return true;
        }
    }

    public void ClearInvokingApplication()
    {
        lock (_gate)
        {
            _invokingApplication = null;
        }
    }

    public void SetAutoTdpEnabled(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                _configurationStore.Current.AutoTdpEnabled = enabled;
                _configurationStore.Save();
                _sessionManager.SetAutoTdpEnabled(enabled);
                _lastOperationError = null;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public AutoStartResult SetStartWithWindows(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            bool previousValue = _configurationStore.Current.StartWithWindows;
            try
            {
                _configurationStore.Current.StartWithWindows = enabled;
                _configurationStore.Save();
            }
            catch (Exception exception)
            {
                _configurationStore.Current.StartWithWindows = previousValue;
                SetOperationError(exception);
                return AutoStartResult.Failure(
                    $"Autostart configuration could not be saved: " +
                    exception.Message);
            }

            AutoStartResult result = SynchronizeAutoStart(enabled);
            if (result.IsSuccess)
            {
                _autoStartErrorMessage = null;
                _lastOperationError = null;
            }
            else
            {
                DisableStartWithWindowsAfterFailure();
            }

            return result;
        }
    }

    public void UpdatePowerLimits(PowerLimits values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                ConfigurationPolicy.ValidateBatteryRange(
                    values.BatteryMinTdp,
                    values.BatteryMaxTdp);
                ConfigurationPolicy.ValidateAcRange(
                    values.AcMinTdp,
                    values.AcMaxTdp);
                _configurationStore.Current.PowerLimits = values;
                _configurationStore.Save();
                _lastOperationError = null;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public void UpdateProfileDefaults(ProfileDefaults values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                if (!ConfigurationPolicy.IsAcceptedTargetFps(values.TargetFps))
                    throw new ConfigurationValidationException(
                        "La cible FPS par défaut est invalide.");
                if (!Enum.IsDefined(values.RestoreMode))
                    throw new ConfigurationValidationException(
                        "Le mode de restauration par défaut est invalide.");
                _configurationStore.Current.ProfileDefaults = values;
                _configurationStore.Save();
                _lastOperationError = null;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public GameProfile CreateProfileForCurrentApplication()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return CreateProfileForCurrentApplicationLocked(
                _configurationStore.Current.ProfileDefaults.TargetFps);
        }
    }

    public GameProfile CreateProfileForCurrentApplication(int targetFps)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return CreateProfileForCurrentApplicationLocked(targetFps);
        }
    }

    private GameProfile CreateProfileForCurrentApplicationLocked(int targetFps)
    {
        ForegroundApplicationSnapshot? application =
            _candidateApplication;
        if (application is null)
            throw new ConfigurationValidationException(
                "Aucune application courante n'est détectée.");

        try
        {
            ProfileDefaults defaults = _configurationStore.Current.ProfileDefaults;
            GameProfile profile = _profileService.CreateFromForeground(
                application,
                targetFps,
                defaults.RestoreMode,
                enabled: true);
            _lastOperationError = null;
            return profile;
        }
        catch (Exception exception)
        {
            SetOperationError(exception);
            throw;
        }
    }

    public GameProfile CreateProfileForInvokingApplication(
        int targetFps,
        RestoreMode restoreMode,
        bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_invokingApplication is null)
                throw new ConfigurationValidationException(
                    "Aucune application invoquante n'est disponible.");

            try
            {
                GameProfile profile = _profileService.CreateFromForeground(
                    _invokingApplication.Application,
                    targetFps,
                    restoreMode,
                    enabled);
                _lastOperationError = null;
                return profile;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public GameProfile AddManualProfile(string executablePath)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                GameProfile profile = _profileService.CreateFromExecutablePath(
                    executablePath);
                _lastOperationError = null;
                return profile;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public void UpdateProfile(string originalExecutablePath, GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            ThrowIfDisposed();
            ActiveGameSession? active = _sessionManager.ActiveSession;
            if (active is not null && string.Equals(
                    active.Profile.ExecutablePath,
                    originalExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                AutoTdpRuntimeResult result = _sessionManager.StopActiveSession();
                if (!result.IsSuccess)
                    throw new ConfigurationPersistenceException(
                        result.ErrorMessage ?? "La restauration a échoué.",
                        new InvalidOperationException(result.ErrorMessage));
            }

            try
            {
                _profileService.Update(originalExecutablePath, profile);
                _lastOperationError = null;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public void RemoveProfile(string executablePath)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            ActiveGameSession? active = _sessionManager.ActiveSession;
            if (active is not null && string.Equals(
                    active.Profile.ExecutablePath,
                    executablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                AutoTdpRuntimeResult result = _sessionManager.StopActiveSession();
                if (!result.IsSuccess)
                    throw new ConfigurationPersistenceException(
                        result.ErrorMessage ?? "La restauration a échoué.",
                        new InvalidOperationException(result.ErrorMessage));
            }

            try
            {
                _profileService.RemoveByExecutablePath(executablePath);
                _lastOperationError = null;
            }
            catch (Exception exception)
            {
                SetOperationError(exception);
                throw;
            }
        }
    }

    public AutoTdpRuntimeResult Shutdown()
    {
        lock (_gate)
        {
            if (_disposed)
                return AutoTdpRuntimeResult.Success();

            AutoTdpRuntimeResult result = _sessionManager.StopActiveSession();
            if (!result.IsSuccess)
            {
                SetOperationError(result.ErrorMessage ?? "La restauration a échoué.");
                return result;
            }

            _sessionManager.Dispose();
            if (_runtime is IDisposable disposableRuntime)
                disposableRuntime.Dispose();
            _logger.Dispose();
            _disposed = true;
            return result;
        }
    }

    public void Dispose() => Shutdown();

    private AllyApplicationSnapshot CreateSnapshot()
    {
        AllyAutoTdpConfiguration configuration = _configurationStore.Current;
        return new(
            configuration.AutoTdpEnabled,
            configuration.StartWithWindows,
            _sessionManager.State,
            _sessionManager.LastErrorMessage,
            _detectedApplication,
            _candidateApplication,
            _invokingApplication,
            _sessionManager.ActiveSession,
            _runtime.GetSnapshot(),
            configuration.PowerLimits,
            configuration.ProfileDefaults,
            configuration.GameProfiles.ToArray(),
            _autoStartErrorMessage,
            _lastOperationError);
    }

    private void SynchronizeConfiguredAutoStart()
    {
        AutoStartResult result = SynchronizeAutoStart(
            _configurationStore.Current.StartWithWindows);
        if (!result.IsSuccess)
            DisableStartWithWindowsAfterFailure();
    }

    private AutoStartResult SynchronizeAutoStart(bool enabled)
    {
        AutoStartResult result;
        try
        {
            result = _autoStartService.Synchronize(
                enabled,
                GetApplicationExecutablePath());
        }
        catch (Exception exception)
        {
            result = AutoStartResult.Failure(
                $"Task Scheduler autostart failed: {exception.Message}");
        }

        if (!result.IsSuccess)
        {
            string message = result.ErrorMessage ??
                "Task Scheduler autostart synchronization failed.";
            _autoStartErrorMessage = message;
            _lastOperationError = message;
            _logger.Error("AUTOSTART", new InvalidOperationException(message));
        }

        return result;
    }

    private void DisableStartWithWindowsAfterFailure()
    {
        try
        {
            _configurationStore.Current.StartWithWindows = false;
            _configurationStore.Save();
        }
        catch (Exception exception)
        {
            _logger.Error("AUTOSTART configuration rollback failed", exception);
        }

        AutoStartResult cleanup = SynchronizeAutoStart(false);
        if (!cleanup.IsSuccess)
        {
            _logger.Log(
                "AUTOSTART",
                cleanup.ErrorMessage ??
                "Could not remove the AllyAutoTDP autostart task after failure.");
        }
    }

    private static string GetApplicationExecutablePath()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(
                Path.GetFileName(processPath),
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return Path.Combine(AppContext.BaseDirectory, "AllyAutoTDP.exe");
    }

    private static GameSessionState MapRuntimeState(AutoTdpState state) => state switch
    {
        AutoTdpState.MaxLimit => GameSessionState.MaxLimit,
        AutoTdpState.FpsInvalid => GameSessionState.Paused,
        AutoTdpState.FpsSourceLost or
            AutoTdpState.PowerInvalid or
            AutoTdpState.Faulted => GameSessionState.Error,
        _ => GameSessionState.Active
    };

    private void UpdateCandidateApplication(
        ForegroundApplicationSnapshot? observedApplication,
        bool allowReplacement = false)
    {
        if (allowReplacement)
        {
            UpdateCandidateFromInvocation(observedApplication);
            return;
        }

        ForegroundApplicationSnapshot? invalidatedApplication = null;
        if (_candidateApplication is not null)
        {
            ProcessLifetimeStatus lifetime = CheckCandidateLifetime(
                _candidateApplication);
            if (lifetime == ProcessLifetimeStatus.Terminated)
            {
                invalidatedApplication = _candidateApplication;
                _candidateApplication = null;
            }
        }

        if (_candidateApplication is not null || observedApplication is null)
            return;

        if (invalidatedApplication is not null &&
            SameProcess(invalidatedApplication, observedApplication))
        {
            return;
        }

        if (_profileService.FindByExecutablePath(
                observedApplication.ExecutablePath) is not null)
        {
            return;
        }

        _candidateApplication = observedApplication;
    }

    private void UpdateCandidateFromInvocation(
        ForegroundApplicationSnapshot? invokedApplication)
    {
        if (invokedApplication is null)
            return;

        ForegroundApplicationSnapshot? previousApplication =
            _candidateApplication;
        if (_profileService.FindByExecutablePath(
                invokedApplication.ExecutablePath) is not null)
        {
            return;
        }

        if (previousApplication is null)
        {
            _candidateApplication = invokedApplication;
            return;
        }

        if (SameProcess(previousApplication, invokedApplication))
        {
            return;
        }

        _candidateApplication = invokedApplication;
    }

    private ProcessLifetimeStatus CheckCandidateLifetime(
        ForegroundApplicationSnapshot application)
    {
        try
        {
            return _processLifetimeChecker.Check(
                GameProcessIdentity.FromSnapshot(application));
        }
        catch (Exception exception)
        {
            _logger.Error("Candidate process lifetime check failed", exception);
            return ProcessLifetimeStatus.Unknown;
        }
    }

    private static bool SameProcess(
        ForegroundApplicationSnapshot first,
        ForegroundApplicationSnapshot second) =>
        first.ProcessId == second.ProcessId &&
        first.ProcessStartTime.ToUniversalTime() ==
            second.ProcessStartTime.ToUniversalTime();

    private void SetOperationError(Exception exception)
    {
        _lastOperationError = exception.Message;
        _logger.Error("Application operation failed", exception);
    }

    private void SetOperationError(string message)
    {
        _lastOperationError = message;
        _logger.Log("ERROR", message);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
