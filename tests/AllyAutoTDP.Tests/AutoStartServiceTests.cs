using AllyAutoTDP.Application;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AutoStartServiceTests
{
    [TestMethod]
    public void StartWithWindows_DefaultsToFalse()
    {
        using TemporaryDirectory temp = new();

        ConfigurationStore store = new(Path.Combine(temp.Path, "config.json"));

        Assert.IsFalse(store.Current.StartWithWindows);
    }

    [TestMethod]
    public void ExistingJsonWithoutStartWithWindows_LoadsFalse()
    {
        using TemporaryDirectory temp = new();
        string configPath = Path.Combine(temp.Path, "config.json");
        File.WriteAllText(
            configPath,
            """
            {
              "SchemaVersion": 1,
              "AutoTdpEnabled": true,
              "PowerLimits": {
                "BatteryMinTdp": 6,
                "BatteryMaxTdp": 25,
                "AcMinTdp": 6,
                "AcMaxTdp": 30
              },
              "ProfileDefaults": {
                "TargetFps": 60,
                "RestoreMode": "Balanced"
              },
              "GameProfiles": []
            }
            """);

        ConfigurationStore store = new(configPath);

        Assert.IsFalse(store.Current.StartWithWindows);
    }

    [TestMethod]
    public void EnabledAutostart_CreatesAndUpdatesStableTask()
    {
        using TemporaryDirectory temp = new();
        var scheduler = new FakeTaskScheduler();
        var service = new WindowsTaskSchedulerAutoStartService(scheduler);
        string executablePath = Path.Combine(
            temp.Path,
            "Ally AutoTDP",
            "AllyAutoTDP.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);

        AutoStartResult created = service.Synchronize(true, executablePath);

        Assert.IsTrue(created.IsSuccess);
        AutoStartTaskDefinition definition = scheduler.Tasks[
            AutoStartTaskDefinition.StableTaskPath];
        Assert.AreEqual(
            Path.GetFullPath(executablePath),
            definition.ExecutablePath);
        Assert.AreEqual("--background", definition.Arguments);
        Assert.AreEqual(
            Path.GetDirectoryName(Path.GetFullPath(executablePath)),
            definition.WorkingDirectory);
        Assert.IsTrue(definition.TriggerAtLogon);
        Assert.IsTrue(definition.UseInteractiveToken);
        Assert.IsTrue(definition.UseHighestAvailableRunLevel);
        Assert.IsTrue(definition.IgnoreNewInstances);

        string movedPath = Path.Combine(
            temp.Path,
            "Ally AutoTDP moved",
            "AllyAutoTDP.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        File.WriteAllText(movedPath, string.Empty);
        AutoStartResult updated = service.Synchronize(true, movedPath);

        Assert.IsTrue(updated.IsSuccess);
        Assert.AreEqual(
            Path.GetFullPath(movedPath),
            scheduler.Tasks[AutoStartTaskDefinition.StableTaskPath]
                .ExecutablePath);
        Assert.AreEqual(2, scheduler.RegisteredDefinitions.Count);
        Assert.AreEqual(1, scheduler.FolderCreateCount);
        Assert.AreEqual(1, scheduler.FolderReuseCount);
    }

    [TestMethod]
    public void ExistingTaskFolder_IsReusedWithoutCreation()
    {
        using TemporaryDirectory temp = new();
        var scheduler = new FakeTaskScheduler { FolderExists = true };
        var service = new WindowsTaskSchedulerAutoStartService(scheduler);
        string executablePath = CreateExecutable(temp.Path);

        AutoStartResult result = service.Synchronize(true, executablePath);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, scheduler.FolderCreateCount);
        Assert.AreEqual(1, scheduler.FolderReuseCount);
        Assert.IsTrue(scheduler.Tasks.ContainsKey(
            AutoStartTaskDefinition.StableTaskPath));
    }

    [TestMethod]
    public void MissingExecutable_IsRejectedBeforeTaskRegistration()
    {
        var scheduler = new FakeTaskScheduler();
        var service = new WindowsTaskSchedulerAutoStartService(scheduler);
        string executablePath = Path.Combine(
            Path.GetTempPath(),
            "Ally AutoTDP missing",
            "AllyAutoTDP.exe");

        AutoStartResult result = service.Synchronize(true, executablePath);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage!, "EXE Path");
        Assert.AreEqual(0, scheduler.RegisteredDefinitions.Count);
    }

    [TestMethod]
    public void DisabledAutostart_DeletesOnlyStableTaskAndIsIdempotent()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks[AutoStartTaskDefinition.StableTaskPath] =
            AutoStartTaskDefinition.ForExecutable("C:\\AllyAutoTDP\\old.exe");
        scheduler.Tasks[@"\Foreign\Task"] =
            AutoStartTaskDefinition.ForExecutable("C:\\Foreign\\foreign.exe") with
            {
                TaskPath = @"\Foreign\Task"
            };
        var service = new WindowsTaskSchedulerAutoStartService(scheduler);

        Assert.IsTrue(service.Synchronize(false, "C:\\AllyAutoTDP\\AllyAutoTDP.exe").IsSuccess);
        Assert.IsTrue(service.Synchronize(false, "C:\\AllyAutoTDP\\AllyAutoTDP.exe").IsSuccess);

        Assert.IsFalse(scheduler.Tasks.ContainsKey(
            AutoStartTaskDefinition.StableTaskPath));
        Assert.IsTrue(scheduler.Tasks.ContainsKey(@"\Foreign\Task"));
        CollectionAssert.AreEqual(
            new[]
            {
                AutoStartTaskDefinition.StableTaskPath,
                AutoStartTaskDefinition.StableTaskPath
            },
            scheduler.DeletedPaths);
    }

    [TestMethod]
    public void TaskSchedulerFailure_IsReturnedWithoutThrowing()
    {
        using TemporaryDirectory temp = new();
        var scheduler = new FakeTaskScheduler
        {
            Failure = new UnauthorizedAccessException("Access denied.")
        };
        var service = new WindowsTaskSchedulerAutoStartService(scheduler);

        AutoStartResult result = service.Synchronize(
            true,
            CreateExecutable(temp.Path));

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage!, "Access denied");
    }

    [TestMethod]
    public void TaskSchedulerFailure_LogsStageHResultAndActionPaths()
    {
        using TemporaryDirectory temp = new();
        using var writer = new StringWriter();
        using var logger = new AllyAutoTDP.Logging.AppLogger(writer);
        var scheduler = new FakeTaskScheduler
        {
            Failure = new UnauthorizedAccessException("Access denied.")
        };
        var service = new WindowsTaskSchedulerAutoStartService(scheduler, logger);
        string executablePath = CreateExecutable(temp.Path);

        service.Synchronize(true, executablePath);

        string log = writer.ToString();
        StringAssert.Contains(log, "HRESULT=0x80070005");
        StringAssert.Contains(log, "TaskFolder=");
        StringAssert.Contains(log, "EXE Path=");
        StringAssert.Contains(log, "Arguments='--background'");
        StringAssert.Contains(log, "WorkingDirectory=");
    }

    private sealed class FakeTaskScheduler : IAutoStartTaskScheduler
    {
        public Dictionary<string, AutoStartTaskDefinition> Tasks { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<AutoStartTaskDefinition> RegisteredDefinitions { get; } = [];

        public List<string> DeletedPaths { get; } = [];

        public bool FolderExists { get; set; }

        public int FolderCreateCount { get; private set; }

        public int FolderReuseCount { get; private set; }

        public Exception? Failure { get; init; }

        public void RegisterOrUpdate(AutoStartTaskDefinition definition)
        {
            if (Failure is not null)
                throw Failure;

            if (!FolderExists)
            {
                FolderExists = true;
                FolderCreateCount++;
            }
            else
                FolderReuseCount++;
            RegisteredDefinitions.Add(definition);
            Tasks[definition.TaskPath] = definition;
        }

        public void Delete(string taskPath)
        {
            if (Failure is not null)
                throw Failure;

            DeletedPaths.Add(taskPath);
            Tasks.Remove(taskPath);
        }
    }

    private static string CreateExecutable(string directory)
    {
        string executablePath = Path.Combine(
            directory,
            "Ally AutoTDP",
            "AllyAutoTDP.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        return executablePath;
    }
}
