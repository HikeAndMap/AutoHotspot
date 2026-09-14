namespace AutoHotspot;

/// <summary>Small append-only log at %LOCALAPPDATA%\AutoHotspot\log.txt, rotated at 1 MB.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static string FolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHotspot");

    public static string FilePath { get; } = Path.Combine(FolderPath, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(FolderPath);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(FolderPath, "log.old.txt"), overwrite: true);

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never take the keep-alive loop down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
