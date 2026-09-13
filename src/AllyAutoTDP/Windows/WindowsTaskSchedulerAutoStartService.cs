using System.Runtime.InteropServices;
using System.Security.Principal;
using AllyAutoTDP.Application;
using AllyAutoTDP.Logging;

namespace AllyAutoTDP.Windows;

public sealed class WindowsTaskSchedulerAutoStartService : IAutoStartService
{
    private readonly IAutoStartTaskScheduler _scheduler;
    private readonly AppLogger? _logger;

    public WindowsTaskSchedulerAutoStartService()
        : this(new ComTaskScheduler(), null)
    {
    }

    public WindowsTaskSchedulerAutoStartService(
        IAutoStartTaskScheduler scheduler,
        AppLogger? logger = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _logger = logger;
    }

    public AutoStartResult Synchronize(bool enabled, string executablePath)
    {
        AutoStartTaskDefinition? definition = null;
        try
        {
            definition = AutoStartTaskDefinition.ForExecutable(executablePath);
            if (enabled)
                ValidateExecutable(definition);

            if (enabled)
                _scheduler.RegisterOrUpdate(definition);
            else
                _scheduler.Delete(definition.TaskPath);

            return AutoStartResult.Success();
        }
        catch (Exception exception)
        {
            string message = FormatFailure(definition, exception);
            _logger?.Log("AUTOSTART", message);
            return AutoStartResult.Failure(message);
        }
    }

    private static void ValidateExecutable(AutoStartTaskDefinition definition)
    {
        if (!File.Exists(definition.ExecutablePath))
        {
            throw new FileNotFoundException(
                "The configured AllyAutoTDP executable was not found.",
                definition.ExecutablePath);
        }

        if (!Directory.Exists(definition.WorkingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The executable working directory was not found: " +
                definition.WorkingDirectory);
        }
    }

    private static string FormatFailure(
        AutoStartTaskDefinition? definition,
        Exception exception)
    {
        string taskPath = definition?.TaskPath ??
            AutoStartTaskDefinition.StableTaskPath;
        string executablePath = definition?.ExecutablePath ?? "<unavailable>";
        string arguments = definition?.Arguments ?? "<unavailable>";
        string workingDirectory = definition?.WorkingDirectory ?? "<unavailable>";
        string stage = exception is TaskSchedulerOperationException operation
            ? operation.Stage
            : "request validation or scheduler backend";
        int hresult = exception is TaskSchedulerOperationException taskException
            ? taskException.HResultCode
            : GetHResult(exception);
        string detail = GetInnermostMessage(exception);

        return
            $"Task Scheduler autostart failed at {stage}. " +
            $"HRESULT=0x{unchecked((uint)hresult):X8}; " +
            $"TaskFolder={AutoStartTaskDefinition.StableTaskFolderPath}; " +
            $"TaskPath={taskPath}; " +
            $"EXE Path='{executablePath}'; " +
            $"Arguments='{arguments}'; " +
            $"WorkingDirectory='{workingDirectory}'; " +
            detail;
    }

    private static int GetHResult(Exception exception)
    {
        int fallback = exception.HResult;
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is COMException)
            {
                return current.HResult;
            }

            uint code = unchecked((uint)current.HResult);
            if (code == 0x80070002U || code == 0x80070003U)
                return current.HResult;
        }

        return fallback;
    }

    private static string GetInnermostMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }
}

public sealed class ComTaskScheduler : IAutoStartTaskScheduler
{
    private const string TaskServiceProgId = "Schedule.Service";
    private const string TaskFolderName = "AllyAutoTDP";
    private const string TaskName = "AutoStart";

    private const int ExecAction = 0;
    private const int LogonTrigger = 9;
    private const int CreateOrUpdate = 6;
    private const int InteractiveToken = 3;
    private const int HighestAvailable = 1;
    private const int IgnoreNew = 0;

    public void RegisterOrUpdate(AutoStartTaskDefinition definition)
    {
        ValidateDefinition(definition);

        object? service = null;
        object? rootFolder = null;
        object? taskFolder = null;
        object? taskDefinition = null;
        string stage = "COM activation";
        try
        {
            Type? serviceType = Type.GetTypeFromProgID(
                TaskServiceProgId,
                throwOnError: false);
            if (serviceType is null)
            {
                throw new InvalidOperationException(
                    "Windows Task Scheduler COM service is unavailable.");
            }

            service = Activator.CreateInstance(serviceType) ??
                throw new InvalidOperationException(
                    "Windows Task Scheduler COM service could not be created.");
            dynamic taskService = service;

            stage = "Connect";
            taskService.Connect();

            stage = "Get RootFolder";
            rootFolder = taskService.GetFolder("\\");
            dynamic root = rootFolder;

            stage = "Get/Create AllyAutoTDP folder";
            taskFolder = GetOrCreateTaskFolder(root);
            dynamic folder = taskFolder;

            stage = "NewTask";
            taskDefinition = taskService.NewTask(0);
            dynamic task = taskDefinition;
            string user = WindowsIdentity.GetCurrent().Name;

            stage = "Configure registration info and principal";
            dynamic registrationInfo = task.RegistrationInfo;
            registrationInfo.Description =
                "Starts AllyAutoTDP in the background when the user logs on.";
            dynamic principal = task.Principal;
            principal.UserId = user;
            principal.LogonType = InteractiveToken;
            principal.RunLevel = HighestAvailable;

            stage = "Configure LogonTrigger";
            dynamic triggers = task.Triggers;
            dynamic trigger = triggers.Create(LogonTrigger);
            trigger.Enabled = true;
            trigger.UserId = user;

            stage = "Configure ExecAction";
            dynamic actions = task.Actions;
            dynamic action = actions.Create(ExecAction);
            action.Path = definition.ExecutablePath;
            action.Arguments = definition.Arguments;
            action.WorkingDirectory = definition.WorkingDirectory;

            stage = "Configure task settings";
            dynamic settings = task.Settings;
            settings.MultipleInstances = IgnoreNew;
            settings.StartWhenAvailable = true;

            stage = "RegisterTaskDefinition";
            folder.RegisterTaskDefinition(
                TaskName,
                task,
                CreateOrUpdate,
                null,
                null,
                InteractiveToken,
                null);
        }
        catch (TaskSchedulerOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TaskSchedulerOperationException(stage, exception);
        }
        finally
        {
            ReleaseComObject(taskDefinition);
            ReleaseComObject(taskFolder);
            ReleaseComObject(rootFolder);
            ReleaseComObject(service);
        }
    }

    public void Delete(string taskPath)
    {
        if (!string.Equals(
                taskPath,
                AutoStartTaskDefinition.StableTaskPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only the AllyAutoTDP autostart task may be deleted.",
                nameof(taskPath));
        }

        object? service = null;
        object? rootFolder = null;
        object? taskFolder = null;
        string stage = "COM activation";
        try
        {
            Type? serviceType = Type.GetTypeFromProgID(
                TaskServiceProgId,
                throwOnError: false);
            if (serviceType is null)
            {
                throw new InvalidOperationException(
                    "Windows Task Scheduler COM service is unavailable.");
            }

            service = Activator.CreateInstance(serviceType) ??
                throw new InvalidOperationException(
                    "Windows Task Scheduler COM service could not be created.");
            dynamic taskService = service;

            stage = "Connect";
            taskService.Connect();
            stage = "Get RootFolder";
            rootFolder = taskService.GetFolder("\\");
            dynamic root = rootFolder;

            stage = "Get AllyAutoTDP folder";
            try
            {
                taskFolder = root.GetFolder(TaskFolderName);
            }
            catch (Exception exception) when (IsNotFound(exception))
            {
                return;
            }

            dynamic folder = taskFolder;
            stage = "Delete AutoStart";
            try
            {
                folder.DeleteTask(TaskName, 0);
            }
            catch (Exception exception) when (IsNotFound(exception))
            {
            }
        }
        catch (TaskSchedulerOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TaskSchedulerOperationException(stage, exception);
        }
        finally
        {
            ReleaseComObject(taskFolder);
            ReleaseComObject(rootFolder);
            ReleaseComObject(service);
        }
    }

    private static dynamic GetOrCreateTaskFolder(dynamic rootFolder)
    {
        try
        {
            return rootFolder.GetFolder(TaskFolderName);
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            return rootFolder.CreateFolder(TaskFolderName, null);
        }
    }

    private static void ValidateDefinition(AutoStartTaskDefinition definition)
    {
        if (!string.Equals(
                definition.TaskPath,
                AutoStartTaskDefinition.StableTaskPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only the AllyAutoTDP autostart task may be registered.",
                nameof(definition));
        }

        if (!definition.TriggerAtLogon ||
            !definition.UseInteractiveToken ||
            !definition.UseHighestAvailableRunLevel ||
            !definition.IgnoreNewInstances)
        {
            throw new ArgumentException(
                "The AllyAutoTDP autostart task policy is incomplete.",
                nameof(definition));
        }
    }

    private static bool IsNotFound(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            uint code = unchecked((uint)current.HResult);
            if (code == 0x80070002U || code == 0x80070003U)
                return true;
        }

        return false;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
