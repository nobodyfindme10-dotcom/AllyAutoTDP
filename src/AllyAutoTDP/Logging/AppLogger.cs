using System.Text;

namespace AllyAutoTDP.Logging;

public sealed class AppLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private bool _disposed;

    public string FilePath { get; }

    public AppLogger(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AllyAutoTDP",
            "AllyAutoTDP.log");
        _writer = CreateWriter(FilePath);
    }

    public AppLogger(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        FilePath = string.Empty;
        _writer = writer;
    }

    public void Log(string category, string message)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _writer.WriteLine($"{DateTimeOffset.Now:O} [{category}] {message}");
            }
        }
        catch
        {
        }
    }

    public void Error(string message, Exception? exception = null)
    {
        try
        {
            string suffix = exception is null
                ? string.Empty
                : $": {exception.GetType().Name}: {exception.Message}";
            Log("ERROR", message + suffix);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _writer.Dispose();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static TextWriter CreateWriter(string filePath)
    {
        FileStream? stream = null;
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            StreamWriter writer = new(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            stream = null;
            return writer;
        }
        catch
        {
            try
            {
                stream?.Dispose();
            }
            catch
            {
            }

            return TextWriter.Null;
        }
    }
}
