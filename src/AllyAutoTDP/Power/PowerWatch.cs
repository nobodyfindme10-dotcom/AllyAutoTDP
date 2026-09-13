namespace AllyAutoTDP.Power;

public sealed class PowerWatch
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly AllyPowerController _controller;

    public PowerWatch(AllyPowerController controller)
    {
        _controller = controller;
    }

    public PowerReadback PollOnce(Action<PowerReadback>? onSample = null)
    {
        PowerReadback current = _controller.ReadExperimental();

        onSample?.Invoke(current);
        return current;
    }

    public async Task RunAsync(
        CancellationToken cancellationToken,
        Action<PowerReadback>? onSample = null,
        TimeSpan? interval = null)
    {
        TimeSpan delay = interval ?? DefaultInterval;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PollOnce(onSample);
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
