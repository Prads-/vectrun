using vectrun.Models.Api;

namespace vectrun.Services;

public class FilesystemService
{
    public BrowseResult Browse(string? path)
    {
        var current = string.IsNullOrEmpty(path)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : path;

        var parent = Path.GetDirectoryName(current);
        var entries = Directory.GetDirectories(current)
            .Select(d => new BrowseEntry(Path.GetFileName(d), d))
            .OrderBy(e => e.Name)
            .ToList();

        return new BrowseResult(current, parent, entries);
    }
}
