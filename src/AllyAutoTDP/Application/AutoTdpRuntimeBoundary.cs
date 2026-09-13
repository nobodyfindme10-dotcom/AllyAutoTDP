using AllyAutoTDP.Configuration;
using AllyAutoTDP.AutoTdp;

namespace AllyAutoTDP.Application;

public sealed record AutoTdpRuntimeSnapshot(
    bool IsRunning,
    AutoTdpStatus? Status,
    string? ErrorMessage)
{
    public static AutoTdpRuntimeSnapshot Idle(string? errorMessage = null) =>
        new(false, null, errorMessage);
}

public sealed record AutoTdpRuntimeResult(
    bool IsSuccess,
    string? ErrorMessage)
{
    public static AutoTdpRuntimeResult Success() => new(true, null);

    public static AutoTdpRuntimeResult Failure(string errorMessage) =>
        new(false, errorMessage);
}

public interface IAutoTdpRuntime
{
    AutoTdpRuntimeResult Start(GameProfile profile);

    AutoTdpRuntimeResult StopAndRestore();

    AutoTdpRuntimeSnapshot GetSnapshot();
}
