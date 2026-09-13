using AllyAutoTDP.Application;
using AllyAutoTDP.Logging;
using AllyAutoTDP.UI;

namespace AllyAutoTDP;

public static class Program
{
    private const string Version = "V1";

    [STAThread]
    public static int Main(string[] args)
    {
        if (!SingleInstanceGuard.TryAcquire(out SingleInstanceGuard? guard) ||
            guard is null)
        {
            return 0;
        }

        using (guard)
        {
            try
            {
                ApplicationConfiguration.Initialize();
                StartupOptions startupOptions = StartupOptions.FromArgs(args);
                using var context = new AllyApplicationContext(
                    startInBackground: startupOptions.StartInBackground);
                System.Windows.Forms.Application.Run(context);
                return 0;
            }
            catch (Exception exception)
            {
                StartupDiagnostics.Fatal(exception);
                using var logger = new AppLogger();
                logger.Error($"FATAL STARTUP ERROR version={Version}", exception);
                return 100;
            }
        }
    }
}
