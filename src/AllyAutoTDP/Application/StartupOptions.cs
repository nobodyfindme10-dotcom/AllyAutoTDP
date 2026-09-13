namespace AllyAutoTDP.Application;

public sealed record StartupOptions(bool StartInBackground)
{
    public static StartupOptions FromArgs(IEnumerable<string>? args)
    {
        bool background = args?.Any(argument => string.Equals(
            argument,
            "--background",
            StringComparison.OrdinalIgnoreCase)) == true;
        return new(background);
    }
}
