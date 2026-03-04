using System.Text.Json;

namespace rtfm26.Services;

public sealed class SiteSettingsStore
{
    public sealed class NavigationItem
    {
        public string Label { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    private sealed class SiteSettings
    {
        public int HomeRecentPostCount { get; set; } = 5;
        public List<NavigationItem> NavigationItems { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _gate = new();

    public SiteSettingsStore(IWebHostEnvironment env)
    {
        var dataDirectory = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "site-settings.json");
    }

    public int GetHomeRecentPostCount()
    {
        lock (_gate)
        {
            var settings = LoadInternal();
            return NormalizeCount(settings.HomeRecentPostCount);
        }
    }

    public void SetHomeRecentPostCount(int count)
    {
        lock (_gate)
        {
            var settings = LoadInternal();
            settings.HomeRecentPostCount = NormalizeCount(count);
            SaveInternal(settings);
        }
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems()
    {
        lock (_gate)
        {
            var settings = LoadInternal();
            return settings.NavigationItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new NavigationItem
                {
                    Label = x.Label.Trim(),
                    Url = x.Url.Trim()
                })
                .ToList();
        }
    }

    public void SetNavigationItems(IEnumerable<NavigationItem> items)
    {
        lock (_gate)
        {
            var settings = LoadInternal();
            settings.NavigationItems = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new NavigationItem
                {
                    Label = x.Label.Trim(),
                    Url = x.Url.Trim()
                })
                .ToList();
            SaveInternal(settings);
        }
    }

    private SiteSettings LoadInternal()
    {
        if (!File.Exists(_path))
        {
            return new SiteSettings();
        }

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SiteSettings();
        }

        return JsonSerializer.Deserialize<SiteSettings>(json, JsonOptions) ?? new SiteSettings();
    }

    private void SaveInternal(SiteSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private static int NormalizeCount(int count)
    {
        if (count < 1) return 1;
        if (count > 25) return 25;
        return count;
    }
}
