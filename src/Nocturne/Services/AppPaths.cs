namespace Nocturne.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nocturne");

    public static string Wallpapers { get; } = Path.Combine(Root, "Wallpapers");
    public static string Thumbnails { get; } = Path.Combine(Root, "Thumbnails");
    public static string LibraryFile { get; } = Path.Combine(Root, "library.json");
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string LogFile { get; } = Path.Combine(Root, "nocturne.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Wallpapers);
        Directory.CreateDirectory(Thumbnails);
    }
}
