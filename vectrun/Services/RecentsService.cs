using System.Text.Json;

namespace vectrun.Services;

public class RecentsService
{
    private const int MaxRecents = 10;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "vectrun",
        "recents.json");

    private readonly object _lock = new();

    public List<string> GetAll()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath)) return [];
            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch { return []; }
        }
    }

    public List<string> Add(string path)
    {
        lock (_lock)
        {
            var list = GetAllUnlocked();
            list.Remove(path);
            list.Insert(0, path);
            if (list.Count > MaxRecents) list.RemoveRange(MaxRecents, list.Count - MaxRecents);
            Save(list);
            return list;
        }
    }

    public List<string> Remove(string path)
    {
        lock (_lock)
        {
            var list = GetAllUnlocked();
            list.Remove(path);
            Save(list);
            return list;
        }
    }

    private List<string> GetAllUnlocked()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch { return []; }
    }

    private static void Save(List<string> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
    }
}
