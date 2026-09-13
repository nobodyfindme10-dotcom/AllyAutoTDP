using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Hardware.AMD;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Application;

public interface IAutoTdpHardwareFactory
{
    bool TryCreateAdl(out IAmdAdl? adl, out string? errorMessage);

    IAsusAcpi CreateAcpi();

    IPowerSourceReader CreatePowerSource();
}

public sealed class DefaultAutoTdpHardwareFactory : IAutoTdpHardwareFactory
{
    public bool TryCreateAdl(out IAmdAdl? adl, out string? errorMessage)
    {
        if (AmdAdlNative.TryCreate(
                out AmdAdlNative? nativeAdl,
                out errorMessage) && nativeAdl is not null)
        {
            adl = nativeAdl;
            return true;
        }

        adl = null;
        return false;
    }

    public IAsusAcpi CreateAcpi() => new AsusAcpi();

    public IPowerSourceReader CreatePowerSource() => new WindowsPowerSource();
}

public sealed class RealAutoTdpRuntime : IAutoTdpRuntime, IDisposable
{
    private readonly object _gate = new();
    private readonly AllyDetectionResult _detection;
    private readonly Func<PowerLimits> _powerLimitsProvider;
    private readonly AppLogger? _logger;
    private readonly IAutoTdpHardwareFactory _hardwareFactory;

    private RuntimeSession? _session;
    private AutoTdpLoop? _autoLoop;
    private AmdFrameMetricsReader? _fpsReader;
    private AmdPowerReader? _powerReader;
    private IAsusAcpi? _acpi;
    private IAmdAdl? _adl;
    private AutoTdpStatus? _latestStatus;
    private string? _lastErrorMessage;
    private bool _starting;
    private bool _disposed;

    public RealAutoTdpRuntime(
        AllyDetectionResult detection,
        Func<PowerLimits> powerLimitsProvider,
        AppLogger? logger = null,
        IAutoTdpHardwareFactory? hardwareFactory = null)
    {
        _detection = detection ?? throw new ArgumentNullException(nameof(detection));
        _powerLimitsProvider = powerLimitsProvider ??
            throw new ArgumentNullException(nameof(powerLimitsProvider));
        _logger = logger;
        _hardwareFactory = hardwareFactory ?? new DefaultAutoTdpHardwareFactory();
    }

    public AutoTdpRuntimeResult Start(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_gate)
        {
            if (_disposed)
                return AutoTdpRuntimeResult.Failure(
                    "Le runtime AutoTDP est fermé.");

            if (_session is not null || _starting)
                return AutoTdpRuntimeResult.Failure(
                    "Une session AutoTDP est déjà active.");

            _starting = true;
        }

        if (!_detection.IsAllyFamily || !_detection.WriteAuthorized)
        {
            return AutoTdpRuntimeResult.Failure(
                "Le matériel Ally autorisé n'est pas détecté.");
        }

        if (!AutoTdpOptions.IsAcceptedTargetFps(profile.TargetFps))
        {
            return AutoTdpRuntimeResult.Failure(
                $"La cible FPS {profile.TargetFps} n'est pas prise en charge.");
        }

        if (!Enum.IsDefined(profile.RestoreMode))
        {
            return AutoTdpRuntimeResult.Failure(
                "Le mode de restauration du profil est invalide.");
        }

        IAmdAdl? adl = null;
        IAsusAcpi? acpi = null;
        AmdFrameMetricsReader? fpsReader = null;
        AmdPowerReader? powerReader = null;
        AutoTdpLoop? autoLoop = null;
        RuntimeSession? session = null;

        try
        {
            PowerLimits powerLimits = _powerLimitsProvider();
            ConfigurationPolicy.ValidateBatteryRange(
                powerLimits.BatteryMinTdp,
                powerLimits.BatteryMaxTdp);
            ConfigurationPolicy.ValidateAcRange(
                powerLimits.AcMinTdp,
                powerLimits.AcMaxTdp);

            if (!_hardwareFactory.TryCreateAdl(out adl, out string? adlError) ||
                adl is null)
            {
                return AutoTdpRuntimeResult.Failure(
                    adlError ?? "ADL n'est pas disponible.");
            }

            acpi = _hardwareFactory.CreateAcpi();
            if (!acpi.IsConnected)
            {
                return AutoTdpRuntimeResult.Failure(
                    acpi.OpenError ?? "ACPI ASUS n'est pas connecté.");
            }

            IPowerSourceReader powerSource = _hardwareFactory.CreatePowerSource();
            PowerSourceReading initialSource = powerSource.Read();
            if (!initialSource.IsKnown)
            {
                return AutoTdpRuntimeResult.Failure(
                    initialSource.ErrorMessage ??
                    "La source d'alimentation est inconnue.");
            }

            AutoTdpOptions options = new(
                profile.RestoreMode,
                profile.TargetFps,
                source => _powerLimitsProvider().GetEffectiveRange(source),
                initialSource.Source);
            TdpRange initialRange = options.GetEffectiveRange(initialSource.Source);
            var controller = new AllyPowerController(
                acpi,
                _detection.WriteAuthorized,
                profile.RestoreMode,
                _logger);
            fpsReader = new AmdFrameMetricsReader(adl);
            powerReader = new AmdPowerReader(adl);
            autoLoop = new AutoTdpLoop(
                new AutoTdpController(profile.TargetFps, initialRange),
                options,
                fpsReader,
                powerReader,
                powerSource,
                controller.SetFixedTdp,
                controller.Restore,
                _logger);

            AdlOperationResult fpsStart = fpsReader.StartFPS();
            if (!fpsStart.IsSuccess)
            {
                return AutoTdpRuntimeResult.Failure(
                    fpsStart.ErrorMessage ?? "Le suivi FPS n'a pas démarré.");
            }

            session = new RuntimeSession(
                controller,
                autoLoop,
                _logger,
                status => OnStatus(status));

            bool rejectedBecauseDisposed;
            lock (_gate)
            {
                rejectedBecauseDisposed = _disposed;
                if (!rejectedBecauseDisposed)
                {
                    _adl = adl;
                    _acpi = acpi;
                    _fpsReader = fpsReader;
                    _powerReader = powerReader;
                    _autoLoop = autoLoop;
                    _session = session;
                    _lastErrorMessage = null;
                }
            }

            if (rejectedBecauseDisposed)
                return AutoTdpRuntimeResult.Failure(
                    "Le runtime AutoTDP a été fermé pendant le démarrage.");

            return AutoTdpRuntimeResult.Success();
        }
        catch (Exception exception)
        {
            LogError("Le démarrage du runtime AutoTDP a échoué.", exception);
            return AutoTdpRuntimeResult.Failure(exception.Message);
        }
        finally
        {
            lock (_gate)
                _starting = false;

            if (session is not null && !ReferenceEquals(GetSession(), session))
            {
                try
                {
                    session.Quit();
                }
                catch (Exception exception)
                {
                    LogError("La restauration après échec du démarrage a échoué.", exception);
                }
            }

            if (!ReferenceEquals(GetSession(), session))
            {
                autoLoop?.Dispose();
                fpsReader?.Dispose();
                powerReader?.Dispose();
                acpi?.Dispose();
                adl?.Dispose();
            }
        }
    }

    public AutoTdpRuntimeResult StopAndRestore()
    {
        RuntimeSession? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null)
            return AutoTdpRuntimeResult.Success();

        RestoreResult restoreResult;
        try
        {
            restoreResult = session.Quit();
        }
        catch (Exception exception)
        {
            LogError("L'arrêt du runtime AutoTDP a échoué.", exception);
            return AutoTdpRuntimeResult.Failure(exception.Message);
        }

        if (!restoreResult.IsComplete)
        {
            string reason = restoreResult.FailureReason ??
                "La restauration du mode ASUS a échoué.";
            lock (_gate)
            {
                _lastErrorMessage = reason;
            }

            return AutoTdpRuntimeResult.Failure(reason);
        }

        DisposeResources(session);
        return AutoTdpRuntimeResult.Success();
    }

    public AutoTdpRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new(
                _session is not null,
                _latestStatus,
                _lastErrorMessage);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        AutoTdpRuntimeResult result = StopAndRestore();
        if (!result.IsSuccess)
        {
            LogError(
                "Le runtime AutoTDP reste actif après une restauration incomplète.",
                new InvalidOperationException(result.ErrorMessage));
            return;
        }

        lock (_gate)
        {
            _disposed = true;
        }
    }

    private RuntimeSession? GetSession()
    {
        lock (_gate)
            return _session;
    }

    private void DisposeResources(RuntimeSession session)
    {
        AutoTdpLoop? autoLoop;
        AmdFrameMetricsReader? fpsReader;
        AmdPowerReader? powerReader;
        IAsusAcpi? acpi;
        IAmdAdl? adl;

        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
                return;

            _session = null;
            autoLoop = _autoLoop;
            fpsReader = _fpsReader;
            powerReader = _powerReader;
            acpi = _acpi;
            adl = _adl;
            _autoLoop = null;
            _fpsReader = null;
            _powerReader = null;
            _acpi = null;
            _adl = null;
            _latestStatus = null;
            _lastErrorMessage = null;
        }

        session.Dispose();
        autoLoop?.Dispose();
        fpsReader?.Dispose();
        powerReader?.Dispose();
        acpi?.Dispose();
        adl?.Dispose();
    }

    private void OnStatus(AutoTdpStatus status)
    {
        lock (_gate)
        {
            _latestStatus = status;
            if (status.State is AutoTdpState.FpsSourceLost or
                AutoTdpState.PowerInvalid)
            {
                _lastErrorMessage = status.Message;
            }
        }
    }

    private void LogError(string message, Exception exception)
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
