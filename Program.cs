using System.Net;
using rtfm26.Models;
using rtfm26.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BlogStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapGet("/", (BlogStore store) =>
{
    var posts = store.List();
    return Results.Content(RenderHome(posts), "text/html");
});

app.MapGet("/post/{slug}", (string slug, BlogStore store) =>
{
    var post = store.GetBySlug(slug);
    return post is null
        ? Results.NotFound("Post not found.")
        : Results.Content(RenderPost(post), "text/html");
});

app.MapGet("/admin", (BlogStore store) =>
{
    var posts = store.List();
    return Results.Content(RenderAdmin(posts), "text/html");
});

app.MapPost("/admin/save", async (HttpRequest request, BlogStore store) =>
{
    var form = await request.ReadFormAsync();

    var title = form["title"].ToString().Trim();
    var slug = Slugify(form["slug"].ToString().Trim());
    var content = form["content"].ToString().Trim();
    var idText = form["id"].ToString().Trim();

    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(content))
    {
        return Results.BadRequest("Title, slug, and content are required.");
    }

    Guid id;
    if (!Guid.TryParse(idText, out id))
    {
        id = Guid.NewGuid();
    }

    var existing = store.GetById(id);
    var post = existing ?? new BlogPost { Id = id };
    post.Title = title;
    post.Slug = slug;
    post.Content = content;
    store.Save(post);

    return Results.Redirect("/admin");
});

app.MapPost("/admin/delete/{id:guid}", (Guid id, BlogStore store) =>
{
    store.Delete(id);
    return Results.Redirect("/admin");
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));
app.MapGet("/error", () => Results.Problem("An unexpected error occurred."));

app.Run();

static string RenderHome(IReadOnlyList<BlogPost> posts)
{
    var items = string.Join(Environment.NewLine, posts.Select(p =>
        $"<li><a href=\"/post/{WebUtility.HtmlEncode(p.Slug)}\">{WebUtility.HtmlEncode(p.Title)}</a><small>{p.UpdatedUtc:yyyy-MM-dd}</small></li>"));

    var content = posts.Count == 0 ? "<p>No posts yet. Go to <a href=\"/admin\">/admin</a> to write the first post.</p>" : $"<ul>{items}</ul>";

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>rtfm26 blog</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 900px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); }
    ul { list-style: none; padding: 0; }
    li { margin: 0.8rem 0; display: flex; gap: 1rem; }
    a { color: #1d4ed8; text-decoration: none; }
    a:hover { text-decoration: underline; }
    small { color: #64748b; }
  </style>
</head>
<body>
  <main>
    <h1>rtfm26 blog</h1>
    <p><a href="/admin">Open editor</a></p>
    {{content}}
  </main>
</body>
</html>
""";
}

static string RenderPost(BlogPost post)
{
    var title = WebUtility.HtmlEncode(post.Title);
    var body = WebUtility.HtmlEncode(post.Content).Replace("\n", "<br/>");

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{title}}</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 900px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); line-height: 1.6; }
    a { color: #1d4ed8; text-decoration: none; }
  </style>
</head>
<body>
  <main>
    <p><a href="/">Back to posts</a> | <a href="/admin">Edit</a></p>
    <h1>{{title}}</h1>
    <p><small>Updated {{post.UpdatedUtc:yyyy-MM-dd HH:mm}} UTC</small></p>
    <article>{{body}}</article>
  </main>
</body>
</html>
""";
}

static string RenderAdmin(IReadOnlyList<BlogPost> posts)
{
    var postList = string.Join(Environment.NewLine, posts.Select(p =>
        $$"""
<li>
  <strong>{{WebUtility.HtmlEncode(p.Title)}}</strong>
  <a href="/post/{{WebUtility.HtmlEncode(p.Slug)}}">view</a>
  <form method="post" action="/admin/delete/{{p.Id}}" style="display:inline;">
    <button type="submit">delete</button>
  </form>
</li>
"""));

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Blog editor</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 900px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); }
    input, textarea, button { width: 100%; box-sizing: border-box; margin: 0.35rem 0 1rem; padding: 0.6rem; font: inherit; }
    textarea { min-height: 220px; }
    ul { list-style: none; padding: 0; }
    li { margin: 0.7rem 0; display: flex; gap: 0.8rem; align-items: center; }
    button { width: auto; cursor: pointer; }
  </style>
</head>
<body>
  <main>
    <p><a href="/">Back to blog</a></p>
    <h1>Blog editor</h1>
    <form method="post" action="/admin/save">
      <input type="hidden" name="id" value="" />
      <label>Title</label>
      <input name="title" placeholder="My post title" required />
      <label>Slug</label>
      <input name="slug" placeholder="my-post-title" required />
      <label>Content</label>
      <textarea name="content" placeholder="Write your post..." required></textarea>
      <button type="submit">Save post</button>
    </form>
    <h2>Existing posts</h2>
    <ul>
      {{postList}}
    </ul>
  </main>
</body>
</html>
""";
}

static string Slugify(string value)
{
    var chars = value.ToLowerInvariant()
        .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
        .ToArray();

    var slug = new string(chars);
    while (slug.Contains("--", StringComparison.Ordinal))
    {
        slug = slug.Replace("--", "-", StringComparison.Ordinal);
    }

    return slug.Trim('-');
}
