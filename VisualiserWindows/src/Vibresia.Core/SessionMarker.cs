namespace Vibresia.Core;

/// <summary>
/// A file that exists only while the app runs. Finding it at startup means
/// the last run didn't exit cleanly: a crash, or the user killing an app
/// frozen by a custom shader that hung the GPU (Windows resets the driver,
/// which can leave the app hung or dead). A custom shader compiles fine
/// however long it loops, so without this the hanging vibescape, saved as
/// the last one shown, would reopen on every launch (security-model.md).
/// </summary>
public sealed class SessionMarker
{
    public const string FileName = "running";

    private readonly string _path;

    public SessionMarker(string path) => _path = path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create), "Vibresia", FileName);

    /// <returns>Whether the previous run left its marker behind (it didn't exit cleanly).</returns>
    public bool Begin(Log log)
    {
        var unclean = File.Exists(_path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn("startup", $"couldn't write {_path}: {e.Message}");
        }
        return unclean;
    }

    /// <summary>A clean exit: the next launch starts normally.</summary>
    public void End(Log log)
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn("startup", $"couldn't remove {_path}: {e.Message}");
        }
    }
}
