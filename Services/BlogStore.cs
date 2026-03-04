using System.Text.Json;
using rtfm26.Models;

namespace rtfm26.Services;

public sealed class BlogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _gate = new();

    public BlogStore(IWebHostEnvironment env)
    {
        var dataDirectory = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "posts.json");
    }

    public IReadOnlyList<BlogPost> List()
    {
        lock (_gate)
        {
            return LoadInternal()
                .OrderByDescending(x => x.UpdatedUtc)
                .ToList();
        }
    }

    public BlogPost? GetBySlug(string slug)
    {
        lock (_gate)
        {
            return LoadInternal().FirstOrDefault(x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        }
    }

    public BlogPost? GetById(Guid id)
    {
        lock (_gate)
        {
            return LoadInternal().FirstOrDefault(x => x.Id == id);
        }
    }

    public BlogPost Save(BlogPost post)
    {
        lock (_gate)
        {
            var posts = LoadInternal();
            var existing = posts.FindIndex(x => x.Id == post.Id);
            post.Category = NormalizeCategory(post.Category);
            post.UpdatedUtc = DateTimeOffset.UtcNow;

            if (existing >= 0)
            {
                posts[existing] = post;
            }
            else
            {
                posts.Add(post);
            }

            SaveInternal(posts);
            return post;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            var posts = LoadInternal();
            var removed = posts.RemoveAll(x => x.Id == id) > 0;
            if (removed)
            {
                SaveInternal(posts);
            }

            return removed;
        }
    }

    private List<BlogPost> LoadInternal()
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

        var posts = JsonSerializer.Deserialize<List<BlogPost>>(json, JsonOptions) ?? [];
        foreach (var post in posts)
        {
            post.Category = NormalizeCategory(post.Category);
        }

        return posts;
    }

    private void SaveInternal(List<BlogPost> posts)
    {
        var json = JsonSerializer.Serialize(posts, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private static string NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? "default" : category.Trim();
    }
}
