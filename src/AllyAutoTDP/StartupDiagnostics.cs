using System.Text;

namespace AllyAutoTDP;

internal static class StartupDiagnostics
{
    private const string FileName = "AllyAutoTDP-startup.log";
    private static readonly object Gate = new();

    public static void ReportException(string context, Exception exception)
    {
        lock (Gate)
        {
            Write(FormatExceptionReport(
                "STARTUP EXCEPTION",
                context,
                exception));
        }
    }

    public static void Fatal(Exception exception)
    {
        lock (Gate)
        {
            Write(FormatExceptionReport(
                "FATAL STARTUP ERROR",
                "global startup protection",
                exception));
        }
    }

    private static string FormatExceptionReport(
        string header,
        string context,
        Exception exception)
    {
        StringBuilder builder = new();
        builder.AppendLine($"{Timestamp()} {header}");
        builder.AppendLine($"Context: {context}");

        Exception? current = exception;
        int depth = 0;
        while (current is not null)
        {
            string prefix = depth == 0 ? "Exception" : $"InnerException[{depth}]";
            builder.AppendLine($"{prefix} Type: {current.GetType().FullName}");
            builder.AppendLine($"{prefix} Message: {current.Message}");
            builder.AppendLine(
                $"{prefix} StackTrace: {current.StackTrace ?? "<none>"}");
            current = current.InnerException;
            depth++;
        }

        return builder.ToString().TrimEnd();
    }

    private static string Timestamp() =>
        $"{DateTimeOffset.Now:O}";

    private static void Write(string text)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AllyAutoTDP",
                FileName);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(
                path,
                text + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
        }
    }
}
