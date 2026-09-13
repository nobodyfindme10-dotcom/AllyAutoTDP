namespace AllyAutoTDP.Application;

public sealed record AutoStartTaskDefinition(
    string TaskPath,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory)
{
    public const string StableTaskFolderPath = @"\AllyAutoTDP";

    public const string StableTaskPath = StableTaskFolderPath + @"\AutoStart";

    public bool TriggerAtLogon => true;

    public bool UseInteractiveToken => true;

    public bool UseHighestAvailableRunLevel => true;

    public bool IgnoreNewInstances => true;

    public static AutoStartTaskDefinition ForExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException(
                "An executable path is required.",
                nameof(executablePath));

        string fullPath = Path.GetFullPath(executablePath);
        string workingDirectory = Path.GetDirectoryName(fullPath) ??
            AppContext.BaseDirectory;
        return new(
            StableTaskPath,
            fullPath,
            "--background",
            workingDirectory);
    }
}

public readonly record struct AutoStartResult(
    bool IsSuccess,
    string? ErrorMessage)
{
    public static AutoStartResult Success() => new(true, null);

    public static AutoStartResult Failure(string message) =>
        new(false, string.IsNullOrWhiteSpace(message)
            ? "Autostart operation failed."
            : message);
}

public sealed class TaskSchedulerOperationException : Exception
{
    public TaskSchedulerOperationException(
        string stage,
        Exception innerException)
        : base($"Task Scheduler COM stage '{stage}' failed.", innerException)
    {
        Stage = stage;
        HResultCode = GetHResult(innerException);
    }

    public string Stage { get; }

    public int HResultCode { get; }

    private static int GetHResult(Exception exception)
    {
        int fallback = exception.HResult;
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is System.Runtime.InteropServices.COMException)
            {
                return current.HResult;
            }

            uint code = unchecked((uint)current.HResult);
            if (code == 0x80070002U || code == 0x80070003U)
                return current.HResult;
        }

        return fallback;
    }
}

public interface IAutoStartService
{
    AutoStartResult Synchronize(bool enabled, string executablePath);
}

public interface IAutoStartTaskScheduler
{
    void RegisterOrUpdate(AutoStartTaskDefinition definition);

    void Delete(string taskPath);
}

public sealed class NoOpAutoStartService : IAutoStartService
{
    public AutoStartResult Synchronize(bool enabled, string executablePath) =>
        AutoStartResult.Success();
}
