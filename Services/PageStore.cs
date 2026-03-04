using System.Text.Json;
using rtfm26.Models;

namespace rtfm26.Services;

public sealed class PageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _gate = new();

    public PageStore(IWebHostEnvironment env)
    {
        var dataDirectory = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "pages.json");
    }

    public IReadOnlyList<SitePage> List()
    {
        lock (_gate)
        {
            return LoadInternal()
                .OrderBy(x => x.Title)
                .ToList();
        }
    }

    public SitePage? GetBySlug(string slug)
    {
        lock (_gate)
        {
            return LoadInternal().FirstOrDefault(x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        }
    }

    public SitePage? GetById(Guid id)
    {
        lock (_gate)
        {
            return LoadInternal().FirstOrDefault(x => x.Id == id);
        }
    }

    public SitePage Save(SitePage page)
    {
        lock (_gate)
        {
            var pages = LoadInternal();
            var existing = pages.FindIndex(x => x.Id == page.Id);
            page.UpdatedUtc = DateTimeOffset.UtcNow;

            if (existing >= 0)
            {
                pages[existing] = page;
            }
            else
            {
                pages.Add(page);
            }

            SaveInternal(pages);
            return page;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            var pages = LoadInternal();
            var removed = pages.RemoveAll(x => x.Id == id) > 0;
            if (removed)
            {
                SaveInternal(pages);
            }

            return removed;
        }
    }

    private List<SitePage> LoadInternal()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SitePage>>(json, JsonOptions) ?? [];
    }

    private void SaveInternal(List<SitePage> pages)
    {
        var json = JsonSerializer.Serialize(pages, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
