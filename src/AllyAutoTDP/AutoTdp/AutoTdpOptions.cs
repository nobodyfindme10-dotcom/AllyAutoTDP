using System.Globalization;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.AutoTdp;

public readonly record struct TdpRange(int MinTdp, int MaxTdp);

public static class TdpLimits
{
    public const int MinimumWatts = 6;
    public const int BatterySafetyMaxWatts = 25;
    public const int AcSafetyMaxWatts = 30;
    public const int UnknownSafetyMaxWatts = BatterySafetyMaxWatts;

    public static int SafetyMax(PowerSourceKind source) => source switch
    {
        PowerSourceKind.Ac => AcSafetyMaxWatts,
        PowerSourceKind.Battery => BatterySafetyMaxWatts,
        _ => UnknownSafetyMaxWatts
    };

    public static TdpRange DefaultRange(PowerSourceKind source) =>
        new(MinimumWatts, SafetyMax(source));

    public static bool TryValidate(
        PowerSourceKind source,
        int minTdp,
        int maxTdp,
        out string? error)
    {
        int safetyMax = SafetyMax(source);
        if (minTdp < MinimumWatts)
        {
            error = $"Minimum TDP must be at least {MinimumWatts} W.";
            return false;
        }

        if (maxTdp > safetyMax)
        {
            error =
                $"Maximum TDP {maxTdp} W exceeds the " +
                $"{FormatSource(source)} safety limit of {safetyMax} W.";
            return false;
        }

        if (minTdp > maxTdp)
        {
            error =
                $"TDP range must satisfy {MinimumWatts} <= min-tdp <= max-tdp " +
                $"<= {safetyMax} W.";
            return false;
        }

        error = null;
        return true;
    }

    public static string FormatSource(PowerSourceKind source) => source switch
    {
        PowerSourceKind.Ac => "AC",
        PowerSourceKind.Battery => "battery",
        _ => "unknown-source"
    };
}

public sealed record AutoTdpOptions(
    RestoreMode RestoreMode,
    int TargetFps,
    Func<PowerSourceKind, TdpRange> EffectiveRangeProvider,
    PowerSourceKind InitialSource)
{
    public const string RestoreModeArgument = "--restore-mode";
    public const string TargetFpsArgument = "--target-fps";
    public const string MinTdpArgument = "--min-tdp";
    public const string MaxTdpArgument = "--max-tdp";

    private static readonly int[] AcceptedTargets = [30, 40, 45, 60, 90, 120];

    public static bool IsAcceptedTargetFps(int targetFps) =>
        AcceptedTargets.Contains(targetFps);

    public TdpRange GetEffectiveRange(PowerSourceKind source) =>
        EffectiveRangeProvider(source);

    public static bool TryParse(
        string[] args,
        PowerSourceKind source,
        out AutoTdpOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        RestoreMode restoreMode = default;
        int targetFps = 0;
        int minTdp = TdpLimits.MinimumWatts;
        int? maxTdp = null;
        bool restoreModeFound = false;
        bool targetFound = false;
        bool minFound = false;
        bool maxFound = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!IsKnownArgument(argument))
            {
                error = $"Unknown argument '{argument}'.";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = $"Argument '{argument}' requires a value.";
                return false;
            }

            string value = args[++index];
            switch (argument.ToLowerInvariant())
            {
                case RestoreModeArgument:
                    if (restoreModeFound)
                    {
                        error = $"Argument '{RestoreModeArgument}' may be provided only once.";
                        return false;
                    }

                    if (!RestoreModeParser.TryParseValue(value, out restoreMode))
                    {
                        error =
                            $"Invalid restore mode '{value}'. " +
                            "Use silent, balanced or turbo.";
                        return false;
                    }

                    restoreModeFound = true;
                    break;

                case TargetFpsArgument:
                    if (targetFound)
                    {
                        error = $"Argument '{TargetFpsArgument}' may be provided only once.";
                        return false;
                    }

                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out targetFps) ||
                        !AcceptedTargets.Contains(targetFps))
                    {
                        error =
                            $"Invalid target FPS '{value}'. " +
                            "Use 30, 40, 45, 60, 90 or 120.";
                        return false;
                    }

                    targetFound = true;
                    break;

                case MinTdpArgument:
                    if (minFound)
                    {
                        error = $"Argument '{MinTdpArgument}' may be provided only once.";
                        return false;
                    }

                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minTdp))
                    {
                        error = $"Invalid minimum TDP '{value}'.";
                        return false;
                    }

                    minFound = true;
                    break;

                case MaxTdpArgument:
                    if (maxFound)
                    {
                        error = $"Argument '{MaxTdpArgument}' may be provided only once.";
                        return false;
                    }

                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
                    {
                        error = $"Invalid maximum TDP '{value}'.";
                        return false;
                    }

                    maxTdp = parsedMax;
                    maxFound = true;
                    break;
            }
        }

        if (!restoreModeFound)
        {
            error = $"Argument '{RestoreModeArgument}' is required.";
            return false;
        }

        if (!targetFound)
        {
            error = $"Argument '{TargetFpsArgument}' is required.";
            return false;
        }

        int resolvedMax = maxTdp ?? TdpLimits.SafetyMax(source);
        if (!TdpLimits.TryValidate(source, minTdp, resolvedMax, out error))
            return false;

        TdpRange batteryRange = TdpLimits.DefaultRange(PowerSourceKind.Battery);
        TdpRange acRange = TdpLimits.DefaultRange(PowerSourceKind.Ac);
        if (source == PowerSourceKind.Ac)
        {
            acRange = new(minTdp, resolvedMax);
        }
        else
        {
            batteryRange = new(minTdp, resolvedMax);
        }

        options = new AutoTdpOptions(
            restoreMode,
            targetFps,
            currentSource => currentSource == PowerSourceKind.Ac
                ? acRange
                : batteryRange,
            source);
        return true;
    }

    public static string Usage() =>
        "Usage: AllyAutoTDP.exe " +
        "--restore-mode <silent|balanced|turbo> " +
        "--target-fps <30|40|45|60|90|120> " +
        "[--min-tdp <watts>] [--max-tdp <watts>]";

    private static bool IsKnownArgument(string argument) =>
        argument.Equals(RestoreModeArgument, StringComparison.OrdinalIgnoreCase) ||
        argument.Equals(TargetFpsArgument, StringComparison.OrdinalIgnoreCase) ||
        argument.Equals(MinTdpArgument, StringComparison.OrdinalIgnoreCase) ||
        argument.Equals(MaxTdpArgument, StringComparison.OrdinalIgnoreCase);
}
