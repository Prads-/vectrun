using System.Text.Json;

namespace vectrun.Services;

public class RecentsService
{
    private const int MaxRecents = 10;

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "vectrun",
        "recents.json");

    private readonly string _filePath;
    private readonly object _lock = new();

    public RecentsService() : this(DefaultFilePath) { }

    public RecentsService(string filePath) => _filePath = filePath;

    public List<string> GetAll()
    {
        lock (_lock)
        {
            return GetAllUnlocked();
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
        if (!File.Exists(_filePath)) return [];
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch { return []; }
    }

    private void Save(List<string> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(list));
    }
}
