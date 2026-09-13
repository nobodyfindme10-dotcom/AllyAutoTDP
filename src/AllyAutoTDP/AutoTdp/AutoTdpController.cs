using AllyAutoTDP.Hardware.AMD;

namespace AllyAutoTDP.AutoTdp;

public enum AutoTdpState
{
    Stable,
    Increased,
    Decreased,
    MaxLimit,
    SafetyClamped,
    FpsInvalid,
    FpsSourceLost,
    PowerInvalid,
    Faulted,
    Restored
}

public enum AutoTdpAction
{
    None,
    Increase,
    Decrease
}

public readonly record struct AutoTdpDecision(
    AutoTdpAction Action,
    bool ShouldStop,
    int CurrentTdp,
    int EffectiveMax,
    AutoTdpState State,
    string? Reason);

public sealed class AutoTdpController
{
    public static readonly TimeSpan DefaultInvalidTimeout =
        TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IncreaseCooldown =
        TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DecreaseCooldown =
        TimeSpan.FromMilliseconds(600);

    private readonly int _targetFps;
    private readonly TimeSpan _invalidTimeout;
    private DateTimeOffset? _fpsInvalidSince;
    private DateTimeOffset? _lastIncreaseAt;
    private DateTimeOffset? _lastDecreaseAt;
    private int _downSamples;
    private int _minTdp;
    private int _maxTdp;

    public AutoTdpController(
        int targetFps,
        TdpRange range,
        TimeSpan? invalidTimeout = null)
    {
        if (targetFps <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetFps));

        ValidateRange(range);
        _targetFps = targetFps;
        _minTdp = range.MinTdp;
        _maxTdp = range.MaxTdp;
        CurrentTdp = _maxTdp;
        _invalidTimeout = invalidTimeout ?? DefaultInvalidTimeout;
        if (_invalidTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(invalidTimeout));
    }

    public int TargetFps => _targetFps;
    public int MinTdp => _minTdp;
    public int EffectiveMax => _maxTdp;
    public int CurrentTdp { get; private set; }
    public AutoTdpState State { get; private set; } = AutoTdpState.Stable;
    public int DownSamples => _downSamples;

    public double IncreaseThreshold => Math.Min(
        _targetFps * 0.90,
        _targetFps - 4);

    public double DecreaseThreshold => Math.Min(
        _targetFps * 0.95,
        _targetFps - 2);

    public void UpdateLimits(TdpRange range)
    {
        ValidateRange(range);
        _minTdp = range.MinTdp;
        _maxTdp = range.MaxTdp;

        if (CurrentTdp > _maxTdp)
        {
            CurrentTdp = _maxTdp;
            _downSamples = 0;
            State = AutoTdpState.SafetyClamped;
        }
        else if (CurrentTdp < _minTdp)
        {
            CurrentTdp = _minTdp;
            _downSamples = 0;
            State = AutoTdpState.SafetyClamped;
        }
    }

    public AutoTdpDecision Tick(
        FpsReading fps,
        PowerReading power,
        DateTimeOffset now)
    {
        if (!fps.IsValid || !fps.Value.HasValue)
        {
            _downSamples = 0;
            _fpsInvalidSince ??= now;
            TimeSpan invalidDuration = now - _fpsInvalidSince.Value;
            if (invalidDuration >= _invalidTimeout)
            {
                State = AutoTdpState.FpsSourceLost;
                return new(
                    AutoTdpAction.None,
                    true,
                    CurrentTdp,
                    _maxTdp,
                    State,
                    $"FPS SOURCE LOST after {invalidDuration.TotalSeconds:0.0}s");
            }

            State = AutoTdpState.FpsInvalid;
            return new(
                AutoTdpAction.None,
                false,
                CurrentTdp,
                _maxTdp,
                State,
                fps.ErrorMessage ?? "FPS unavailable");
        }

        _fpsInvalidSince = null;
        double upThreshold = IncreaseThreshold;
        double downThreshold = DecreaseThreshold;
        float currentFps = fps.Value.Value;

        if (currentFps <= upThreshold)
        {
            _downSamples = 0;
            if (CurrentTdp >= _maxTdp)
            {
                State = AutoTdpState.MaxLimit;
                return NoAction("FPS is below the increase threshold at max TDP.");
            }

            if (_lastIncreaseAt.HasValue)
            {
                TimeSpan sinceIncrease = now - _lastIncreaseAt.Value;
                if (sinceIncrease < IncreaseCooldown)
                {
                    State = AutoTdpState.Stable;
                    return NoAction(
                        $"increase cooldown active " +
                        $"elapsed={sinceIncrease.TotalMilliseconds:0}ms/" +
                        $"{IncreaseCooldown.TotalMilliseconds:0}ms");
                }
            }

            CurrentTdp++;
            _lastIncreaseAt = now;
            State = CurrentTdp == _maxTdp
                ? AutoTdpState.MaxLimit
                : AutoTdpState.Increased;
            return new(
                AutoTdpAction.Increase,
                false,
                CurrentTdp,
                _maxTdp,
                State,
                $"FPS <= increase threshold {upThreshold:0.##}");
        }

        if (currentFps >= downThreshold)
        {
            _downSamples = Math.Min(_downSamples + 1, 8);
            if (_downSamples < 8)
            {
                State = AutoTdpState.Stable;
                return NoAction($"down samples={_downSamples}/8");
            }

            if (!power.IsValid || !power.Watts.HasValue)
            {
                State = AutoTdpState.PowerInvalid;
                return NoAction(
                    power.ErrorMessage ?? "ASIC power unavailable; decrease blocked.");
            }

            if (power.Watts.Value <= CurrentTdp && CurrentTdp > _minTdp)
            {
                if (_lastDecreaseAt.HasValue)
                {
                    TimeSpan sinceDecrease = now - _lastDecreaseAt.Value;
                    if (sinceDecrease < DecreaseCooldown)
                    {
                        State = AutoTdpState.Stable;
                        return NoAction(
                            $"decrease cooldown active " +
                            $"elapsed={sinceDecrease.TotalMilliseconds:0}ms/" +
                            $"{DecreaseCooldown.TotalMilliseconds:0}ms");
                    }
                }

                _downSamples--;
                CurrentTdp--;
                _lastDecreaseAt = now;
                State = AutoTdpState.Decreased;
                return new(
                    AutoTdpAction.Decrease,
                    false,
                    CurrentTdp,
                    _maxTdp,
                    State,
                    $"power={power.Watts.Value} W <= current TDP");
            }

            State = AutoTdpState.Stable;
            return NoAction("decrease conditions not met");
        }

        _downSamples = Math.Max(0, _downSamples - 1);
        State = AutoTdpState.Stable;
        return NoAction("FPS is inside the stable band.");
    }

    private AutoTdpDecision NoAction(string reason) =>
        new(
            AutoTdpAction.None,
            false,
            CurrentTdp,
            _maxTdp,
            State,
            reason);

    private static void ValidateRange(TdpRange range)
    {
        if (range.MinTdp < TdpLimits.MinimumWatts ||
            range.MinTdp > range.MaxTdp)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
    }
}
