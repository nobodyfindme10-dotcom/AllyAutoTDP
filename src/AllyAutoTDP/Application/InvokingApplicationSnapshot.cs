using AllyAutoTDP.Windows;

namespace AllyAutoTDP.Application;

public sealed record InvokingApplicationSnapshot(
    nint WindowHandle,
    ForegroundApplicationSnapshot Application);
