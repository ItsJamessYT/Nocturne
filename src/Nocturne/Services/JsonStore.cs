using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nocturne.Services;

internal static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load {Path.GetFileName(path)}", ex);
            TryBackup(path);
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save {Path.GetFileName(path)}", ex);
        }
    }

    private static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, path + ".corrupt", true);
        }
        catch { }
    }
}
