using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class ForegroundApplicationDetectorTests
{
    [TestMethod]
    public void Detect_ResolvesWindowToProcessSnapshot()
    {
        ForegroundApplicationSnapshot expected = new(
            321,
            DateTimeOffset.UtcNow,
            @"C:\Games\Example\Example.exe",
            "Example.exe",
            "Example");
        FakeForegroundWindowApi windowApi = new(42, 77, 321);
        FakeProcessSnapshotProvider processProvider = new(expected);
        ForegroundApplicationDetector detector = new(
            windowApi,
            processProvider,
            ownProcessId: 999);

        ForegroundDetectionResult result = detector.Detect();

        Assert.IsTrue(result.IsAvailable);
        Assert.AreEqual(expected, result.Application);
        Assert.AreEqual(321, processProvider.LastProcessId);
    }

    [TestMethod]
    public void Detect_SkipsOwnProcess()
    {
        FakeForegroundWindowApi windowApi = new(42, 77, 999);
        FakeProcessSnapshotProvider processProvider = new(null);
        ForegroundApplicationDetector detector = new(
            windowApi,
            processProvider,
            ownProcessId: 999);

        ForegroundDetectionResult result = detector.Detect();

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsNull(processProvider.LastProcessId);
    }

    [TestMethod]
    public void Detect_ReturnsUnavailableWhenWindowHasNoProcess()
    {
        ForegroundApplicationDetector detector = new(
            new FakeForegroundWindowApi(42, 0, 0),
            new FakeProcessSnapshotProvider(null),
            ownProcessId: 999);

        ForegroundDetectionResult result = detector.Detect();

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Detect_PropagatesProcessResolutionFailure()
    {
        FakeProcessSnapshotProvider processProvider = new(null)
        {
            ErrorMessage = "access denied",
        };
        ForegroundApplicationDetector detector = new(
            new FakeForegroundWindowApi(42, 77, 321),
            processProvider,
            ownProcessId: 999);

        ForegroundDetectionResult result = detector.Detect();

        Assert.IsFalse(result.IsAvailable);
        Assert.AreEqual("access denied", result.ErrorMessage);
    }

    private sealed class FakeForegroundWindowApi : IForegroundWindowApi
    {
        private readonly nint _windowHandle;
        private readonly uint _threadId;
        private readonly uint _processId;

        public FakeForegroundWindowApi(
            nint windowHandle,
            uint threadId,
            uint processId)
        {
            _windowHandle = windowHandle;
            _threadId = threadId;
            _processId = processId;
        }

        public nint GetForegroundWindow() => _windowHandle;

        public uint GetWindowThreadProcessId(
            nint windowHandle,
            out uint processId)
        {
            processId = _processId;
            return _threadId;
        }
    }

    private sealed class FakeProcessSnapshotProvider : IProcessSnapshotProvider
    {
        private readonly ForegroundApplicationSnapshot? _snapshot;

        public FakeProcessSnapshotProvider(
            ForegroundApplicationSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public int? LastProcessId { get; private set; }

        public string? ErrorMessage { get; init; }

        public bool TryRead(
            int processId,
            out ForegroundApplicationSnapshot? snapshot,
            out string? errorMessage)
        {
            LastProcessId = processId;
            snapshot = _snapshot;
            errorMessage = ErrorMessage;
            return ErrorMessage is null && snapshot is not null;
        }
    }
}
