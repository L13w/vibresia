using System.Text;

namespace Vibresia.Core;

/// <summary>
/// The app's one diagnostic channel: a plain-text file the owner can send
/// back from a PC this code is never run on during development. One line per
/// event — <c>timestamp level [area] message</c> — so it reads by eye.
/// Rolls at a size cap and keeps a few old files, so it never grows without
/// bound on a machine nobody is watching.
/// </summary>
public sealed class Log : IDisposable
{
    public const long DefaultMaxBytes = 5 * 1024 * 1024;
    public const int DefaultKeep = 3;
    public const string FileName = "vibresia.log";

    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly int _keep;
    private StreamWriter _writer;

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, FileName);

    public Log(string directory, long maxBytes = DefaultMaxBytes, int keep = DefaultKeep)
    {
        Directory = directory;
        _maxBytes = maxBytes;
        _keep = keep;
        System.IO.Directory.CreateDirectory(directory);
        _writer = Open();
    }

    /// <summary>%LOCALAPPDATA%\Vibresia\Logs on Windows; the XDG equivalent elsewhere.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create), "Vibresia", "Logs");

    public void Info(string area, string message) => Write("INFO", area, message);
    public void Warn(string area, string message) => Write("WARN", area, message);
    public void Error(string area, string message) => Write("ERROR", area, message);

    public void Write(string level, string area, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} [{area}] {message}";
        lock (_gate)
        {
            if (_writer.BaseStream.Length + Encoding.UTF8.GetByteCount(line) + 1 > _maxBytes)
                Roll();
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private StreamWriter Open() =>
        new(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false));

    // vibresia.log → .1 → .2 …; the oldest beyond `keep` is dropped.
    // A failed move (another process holding a file open) must never leave
    // the logger without a writer: the crash handler logs through it too.
    private void Roll()
    {
        _writer.Dispose();
        try
        {
            for (var i = _keep - 1; i >= 1; i--)
            {
                var from = i == 1 ? FilePath : $"{FilePath}.{i - 1}";
                var to = $"{FilePath}.{i}";
                if (File.Exists(from)) File.Move(from, to, overwrite: true);
            }
            if (_keep <= 1) File.Delete(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keep appending to the current file rather than lose the log.
        }
        finally
        {
            _writer = Open();
        }
    }

    public void Dispose()
    {
        lock (_gate) _writer.Dispose();
    }
}
