using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.FileProviders;
using rtfm26.Models;
using rtfm26.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BlogStore>();
builder.Services.AddSingleton<PageStore>();
builder.Services.AddSingleton<SiteSettingsStore>();

var adminEmail = builder.Configuration["Admin:AllowedEmail"] ?? string.Empty;
var tenantId = builder.Configuration["Authentication:Microsoft:TenantId"] ?? string.Empty;
var clientId = builder.Configuration["Authentication:Microsoft:ClientId"] ?? string.Empty;
var clientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"] ?? string.Empty;
var callbackPath = builder.Configuration["Authentication:Microsoft:CallbackPath"] ?? "/signin-oidc";

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Microsoft";
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/forbidden";
})
.AddOpenIdConnect("Microsoft", options =>
{
    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    options.ClientId = clientId;
    options.ClientSecret = clientSecret;
    options.CallbackPath = callbackPath;
    options.ResponseType = "code";
    options.SaveTokens = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var email = GetUserEmail(context.User);
            return !string.IsNullOrWhiteSpace(adminEmail) &&
                   email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase);
        });
    });
});

var app = builder.Build();
var imagesPath = Path.Combine(app.Environment.ContentRootPath, "images");
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads");
Directory.CreateDirectory(imagesPath);
Directory.CreateDirectory(uploadsPath);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesPath),
    RequestPath = "/images"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext http, BlogStore blogStore, PageStore pageStore, SiteSettingsStore settingsStore) =>
{
    var posts = blogStore.List();
    var pages = pageStore.List();
    var navItems = settingsStore.GetNavigationItems();
    var isAuthenticated = http.User.Identity?.IsAuthenticated ?? false;
    return Results.Content(
        RenderHome(
            posts,
            pages,
            navItems,
            isAuthenticated,
            IsAdminUser(http.User, adminEmail),
            GetUserEmail(http.User),
            settingsStore.GetHomeRecentPostCount()),
        "text/html");
});

app.MapGet("/post/{slug}", (HttpContext http, string slug, BlogStore blogStore, PageStore pageStore, SiteSettingsStore settingsStore) =>
{
    var post = blogStore.GetBySlug(slug);
    return post is null
        ? Results.NotFound("Post not found.")
        : Results.Content(
            RenderPost(
                post,
                pageStore.List(),
                settingsStore.GetNavigationItems(),
                http.User.Identity?.IsAuthenticated ?? false,
                IsAdminUser(http.User, adminEmail),
                GetUserEmail(http.User)),
            "text/html");
});

app.MapGet("/page/{slug}", (HttpContext http, string slug, BlogStore blogStore, PageStore pageStore, SiteSettingsStore settingsStore) =>
{
    var page = pageStore.GetBySlug(slug);
    var allPosts = blogStore.List();
    var matchingPosts = page is null
        ? []
        : allPosts
            .Where(p => p.Category.Equals(page.Category, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.UpdatedUtc)
            .ToList();

    return page is null
        ? Results.NotFound("Page not found.")
        : Results.Content(
            RenderPage(
                page,
                matchingPosts,
                pageStore.List(),
                settingsStore.GetNavigationItems(),
                http.User.Identity?.IsAuthenticated ?? false,
                IsAdminUser(http.User, adminEmail),
                GetUserEmail(http.User)),
            "text/html");
});

app.MapGet("/admin", (HttpContext http, HttpRequest request, BlogStore blogStore, PageStore pageStore, SiteSettingsStore settingsStore) =>
{
    var posts = blogStore.List();
    var pages = pageStore.List();
    var uploaded = request.Query["uploaded"].ToString();

    BlogPost? editingPost = null;
    if (Guid.TryParse(request.Query["editPost"].ToString(), out var postId))
    {
        editingPost = blogStore.GetById(postId);
    }

    SitePage? editingPage = null;
    if (Guid.TryParse(request.Query["editPage"].ToString(), out var pageId))
    {
        editingPage = pageStore.GetById(pageId);
    }

    int? importedCount = null;
    if (int.TryParse(request.Query["imported"].ToString(), out var imported))
    {
        importedCount = imported;
    }

    var navItems = settingsStore.GetNavigationItems();
    var navEditorText = SerializeNavigationItemsForEditor(navItems);

    return Results.Content(
        RenderAdmin(
            posts,
            pages,
            navItems,
            navEditorText,
            uploaded,
            editingPost,
            editingPage,
            importedCount,
            settingsStore.GetHomeRecentPostCount(),
            http.User.Identity?.IsAuthenticated ?? false,
            IsAdminUser(http.User, adminEmail),
            GetUserEmail(http.User)),
        "text/html");
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/settings/navigation", async (HttpRequest request, SiteSettingsStore settingsStore) =>
{
    var form = await request.ReadFormAsync();
    var text = form["navigationItems"].ToString();
    var items = ParseNavigationItemsFromEditor(text);
    settingsStore.SetNavigationItems(items);
    return Results.Redirect("/admin");
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/settings/home", async (HttpRequest request, SiteSettingsStore settingsStore) =>
{
    var form = await request.ReadFormAsync();
    var input = form["homeRecentPostCount"].ToString().Trim();
    if (!int.TryParse(input, out var count))
    {
        return Results.BadRequest("Invalid post count.");
    }

    settingsStore.SetHomeRecentPostCount(count);
    return Results.Redirect("/admin");
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/save", async (HttpRequest request, BlogStore blogStore) =>
{
    var form = await request.ReadFormAsync();

    var title = form["title"].ToString().Trim();
    var slug = Slugify(form["slug"].ToString().Trim());
    var category = NormalizeCategory(form["category"].ToString());
    var content = form["content"].ToString().Trim();
    var idText = form["id"].ToString().Trim();

    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(content))
    {
        return Results.BadRequest("Title, slug, and content are required.");
    }

    if (!Guid.TryParse(idText, out var id))
    {
        id = Guid.NewGuid();
    }

    var post = blogStore.GetById(id) ?? new BlogPost { Id = id };
    post.Title = title;
    post.Slug = slug;
    post.Category = category;
    post.Content = content;
    blogStore.Save(post);

    return Results.Redirect("/admin?editPost=" + id);
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/delete/{id:guid}", (Guid id, BlogStore blogStore) =>
{
    blogStore.Delete(id);
    return Results.Redirect("/admin");
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/page/save", async (HttpRequest request, PageStore pageStore) =>
{
    var form = await request.ReadFormAsync();

    var title = form["title"].ToString().Trim();
    var slugInput = form["slug"].ToString().Trim();
    var slug = string.IsNullOrWhiteSpace(slugInput) ? string.Empty : Slugify(slugInput);
    var category = NormalizeCategory(form["category"].ToString());
    var content = form["content"].ToString().Trim();
    var idText = form["id"].ToString().Trim();

    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
    {
        return Results.BadRequest("Title and content are required.");
    }

    if (!Guid.TryParse(idText, out var id))
    {
        id = Guid.NewGuid();
    }

    var page = pageStore.GetById(id) ?? new SitePage { Id = id };
    page.Title = title;
    page.Slug = slug;
    page.Category = category;
    page.Content = content;
    pageStore.Save(page);

    return Results.Redirect("/admin?editPage=" + id);
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/page/delete/{id:guid}", (Guid id, PageStore pageStore) =>
{
    pageStore.Delete(id);
    return Results.Redirect("/admin");
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Invalid upload request.");
    }

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    const long maxBytes = 10 * 1024 * 1024;
    if (file.Length > maxBytes)
    {
        return Results.BadRequest("File is too large. Max size is 10 MB.");
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".pdf", ".txt", ".md", ".doc", ".docx"
    };

    if (!allowedExtensions.Contains(extension))
    {
        return Results.BadRequest("Unsupported file type.");
    }

    var safeBaseName = Regex.Replace(Path.GetFileNameWithoutExtension(file.FileName).ToLowerInvariant(), @"[^a-z0-9\-]+", "-").Trim('-');
    if (string.IsNullOrWhiteSpace(safeBaseName))
    {
        safeBaseName = "file";
    }

    var storedName = $"{safeBaseName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}";
    var savedPath = Path.Combine(uploadsPath, storedName);

    await using (var stream = File.Create(savedPath))
    {
        await file.CopyToAsync(stream);
    }

    return Results.Redirect($"/admin?uploaded={Uri.EscapeDataString($"/uploads/{storedName}")}");
}).RequireAuthorization("AdminOnly");

app.MapPost("/admin/import/posts-csv", async (HttpRequest request, BlogStore blogStore) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Invalid import request.");
    }

    var form = await request.ReadFormAsync();
    var file = form.Files["csvFile"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("No CSV file uploaded.");
    }

    var extension = Path.GetExtension(file.FileName);
    if (!".csv".Equals(extension, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only .csv files are supported.");
    }

    string csvText;
    using (var reader = new StreamReader(file.OpenReadStream()))
    {
        csvText = await reader.ReadToEndAsync();
    }

    var rows = ParseCsv(csvText);
    if (rows.Count < 2)
    {
        return Results.BadRequest("CSV must include a header row and at least one data row.");
    }

    var header = rows[0].Select(h => h.Trim()).ToList();
    var titleIndex = header.FindIndex(h => h.Equals("title", StringComparison.OrdinalIgnoreCase));
    var slugIndex = header.FindIndex(h => h.Equals("slug", StringComparison.OrdinalIgnoreCase));
    var categoryIndex = header.FindIndex(h => h.Equals("category", StringComparison.OrdinalIgnoreCase));
    var contentIndex = header.FindIndex(h => h.Equals("content", StringComparison.OrdinalIgnoreCase));

    if (titleIndex < 0 || contentIndex < 0)
    {
        return Results.BadRequest("CSV headers must include at least: title, content (slug optional).");
    }

    var imported = 0;
    for (var i = 1; i < rows.Count; i++)
    {
        var row = rows[i];
        if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
        {
            continue;
        }

        var title = GetCsvValue(row, titleIndex).Trim();
        var slug = slugIndex >= 0 ? Slugify(GetCsvValue(row, slugIndex).Trim()) : string.Empty;
        var category = categoryIndex >= 0 ? NormalizeCategory(GetCsvValue(row, categoryIndex)) : "default";
        var content = GetCsvValue(row, contentIndex).Trim();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
            continue;
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = Slugify(title);
        }

        var existing = blogStore.GetBySlug(slug);
        var post = existing ?? new BlogPost();
        post.Title = title;
        post.Slug = slug;
        post.Category = category;
        post.Content = content;
        blogStore.Save(post);
        imported++;
    }

    return Results.Redirect($"/admin?imported={imported}");
}).RequireAuthorization("AdminOnly");

app.MapGet("/contact", (HttpContext http, BlogStore blogStore, PageStore pageStore, SiteSettingsStore settingsStore) =>
{
    return Results.Content(
        RenderContact(
            pageStore.List(),
            settingsStore.GetNavigationItems(),
            http.User.Identity?.IsAuthenticated ?? false,
            IsAdminUser(http.User, adminEmail),
            GetUserEmail(http.User)),
        "text/html");
});

app.MapGet("/login", (HttpContext http) =>
{
    var returnUrl = NormalizeReturnUrl(http.Request.Query["returnUrl"].ToString());
    return Results.Content(RenderLogin(returnUrl), "text/html");
});

app.MapPost("/login", (HttpContext http) =>
{
    var returnUrl = NormalizeReturnUrl(http.Request.Query["returnUrl"].ToString());
    var authProperties = new AuthenticationProperties { RedirectUri = returnUrl };
    return Results.Challenge(authProperties, ["Microsoft"]);
});

app.MapPost("/logout", () =>
{
    var authProperties = new AuthenticationProperties { RedirectUri = "/" };
    return Results.SignOut(authProperties, [CookieAuthenticationDefaults.AuthenticationScheme, "Microsoft"]);
});

app.MapGet("/forbidden", (HttpContext http) =>
{
    var email = WebUtility.HtmlEncode(GetUserEmail(http.User));
    return Results.Content($"""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /><title>Access denied</title></head>
<body style="font-family: Segoe UI, Arial, sans-serif; margin: 2rem;">
  <h1>Access denied</h1>
  <p>Signed in as <strong>{email}</strong>, but this account is not allowed to edit.</p>
  <p><a href="/">Back to blog</a></p>
</body>
</html>
""", "text/html");
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));
app.MapGet("/error", () => Results.Problem("An unexpected error occurred."));

app.Run();

static string RenderHome(IReadOnlyList<BlogPost> posts, IReadOnlyList<SitePage> pages, IReadOnlyList<SiteSettingsStore.NavigationItem> customNavItems, bool isAuthenticated, bool isAdmin, string userEmail, int homeRecentPostCount)
{
    var embeddedPages = pages.Where(p => string.IsNullOrWhiteSpace(p.Slug)).ToList();
    var recentPosts = posts.Take(homeRecentPostCount).ToList();
    var items = string.Join(Environment.NewLine, recentPosts.Select(p =>
        $"<li><a href=\"/post/{WebUtility.HtmlEncode(p.Slug)}\">{WebUtility.HtmlEncode(p.Title)}</a><small>{FormatCentralTime(p.UpdatedUtc)}</small></li>"));
    var postsContent = recentPosts.Count == 0
        ? "<p>No posts yet. Use the editor to write the first post.</p>"
        : $"<p><small>Showing latest {recentPosts.Count} post(s).</small></p><ul>{items}</ul>";
    var embeddedContent = string.Join(Environment.NewLine, embeddedPages.Select(p =>
        $$"""
<section style="margin-top:2rem;">
  <h2>{{WebUtility.HtmlEncode(p.Title)}}</h2>
  <div>{{RenderMarkup(p.Content)}}</div>
</section>
"""));
    var content = postsContent + embeddedContent;
    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>rtfm26 blog</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #c7e8e1; color: #1f2937; }
    main { max-width: 960px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); }
    .hero { height: 220px; border-radius: 10px; background: url('/images/header.jpg') center/cover no-repeat; margin-bottom: 0.75rem; }
    .menu { display: flex; gap: 0.8rem; flex-wrap: wrap; padding: 0.75rem; background: #f1f5f9; border-radius: 8px; margin-bottom: 1rem; }
    .menu a { color: #1d4ed8; text-decoration: none; font-weight: 600; }
    ul { list-style: none; padding: 0; }
    li { margin: 0.8rem 0; display: flex; gap: 1rem; }
    a { color: #1d4ed8; text-decoration: none; }
    a:hover { text-decoration: underline; }
    small { color: #64748b; }
  </style>
</head>
<body>
  <main>
    <div class="hero"></div>
    <h1>Read the F*****g Manual!</h1>
    {{RenderMenuBar(pages, customNavItems, isAuthenticated, isAdmin, userEmail)}}
    {{content}}
  </main>
</body>
</html>
""";
}

static string RenderPost(BlogPost post, IReadOnlyList<SitePage> pages, IReadOnlyList<SiteSettingsStore.NavigationItem> customNavItems, bool isAuthenticated, bool isAdmin, string userEmail)
{
    var title = WebUtility.HtmlEncode(post.Title);
    var body = RenderMarkup(post.Content);

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{title}}</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 960px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); line-height: 1.6; }
    .menu { display: flex; gap: 0.8rem; flex-wrap: wrap; padding: 0.75rem; background: #f1f5f9; border-radius: 8px; margin-bottom: 1rem; }
    .menu a { color: #1d4ed8; text-decoration: none; font-weight: 600; }
  </style>
</head>
<body>
  <main>
    {{RenderMenuBar(pages, customNavItems, isAuthenticated, isAdmin, userEmail)}}
    <h1>{{title}}</h1>
    <p><small>Category: {{WebUtility.HtmlEncode(post.Category)}}</small></p>
    <p><small>Updated {{FormatCentralTime(post.UpdatedUtc)}}</small></p>
    <article>{{body}}</article>
  </main>
</body>
</html>
""";
}

static string RenderPage(SitePage page, IReadOnlyList<BlogPost> matchingPosts, IReadOnlyList<SitePage> pages, IReadOnlyList<SiteSettingsStore.NavigationItem> customNavItems, bool isAuthenticated, bool isAdmin, string userEmail)
{
    var title = WebUtility.HtmlEncode(page.Title);
    var body = RenderMarkup(page.Content);
    var postItems = matchingPosts.Count == 0
        ? "<p><small>No posts in this category yet.</small></p>"
        : "<ul>" + string.Join(Environment.NewLine, matchingPosts.Select(p =>
            $"<li><a href=\"/post/{WebUtility.HtmlEncode(p.Slug)}\">{WebUtility.HtmlEncode(p.Title)}</a> <small>{FormatCentralTime(p.UpdatedUtc)}</small></li>")) + "</ul>";

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{title}}</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 960px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); line-height: 1.6; }
    .menu { display: flex; gap: 0.8rem; flex-wrap: wrap; padding: 0.75rem; background: #f1f5f9; border-radius: 8px; margin-bottom: 1rem; }
    .menu a { color: #1d4ed8; text-decoration: none; font-weight: 600; }
    ul { list-style: none; padding: 0; }
    li { margin: 0.6rem 0; }
  </style>
</head>
<body>
  <main>
    {{RenderMenuBar(pages, customNavItems, isAuthenticated, isAdmin, userEmail)}}
    <h1>{{title}}</h1>
    <p><small>Category: {{WebUtility.HtmlEncode(page.Category)}}</small></p>
    <p><small>Updated {{FormatCentralTime(page.UpdatedUtc)}}</small></p>
    <article>{{body}}</article>
    <h2>Posts in {{WebUtility.HtmlEncode(page.Category)}}</h2>
    {{postItems}}
  </main>
</body>
</html>
""";
}

static string RenderAdmin(IReadOnlyList<BlogPost> posts, IReadOnlyList<SitePage> pages, IReadOnlyList<SiteSettingsStore.NavigationItem> customNavItems, string navEditorText, string uploadedPath, BlogPost? editingPost, SitePage? editingPage, int? importedCount, int homeRecentPostCount, bool isAuthenticated, bool isAdmin, string userEmail)
{
    var postList = string.Join(Environment.NewLine, posts.Select(p =>
        $$"""<li><strong>{{WebUtility.HtmlEncode(p.Title)}}</strong> <small>({{WebUtility.HtmlEncode(p.Category)}})</small> <a href="/admin?editPost={{p.Id}}">edit</a> <a href="/post/{{WebUtility.HtmlEncode(p.Slug)}}">view</a> <form method="post" action="/admin/delete/{{p.Id}}" style="display:inline;"><button type="submit">delete</button></form></li>"""));
    var pageList = string.Join(Environment.NewLine, pages.Select(p =>
        $$"""<li><strong>{{WebUtility.HtmlEncode(p.Title)}}</strong> <small>({{WebUtility.HtmlEncode(p.Category)}})</small> <a href="/admin?editPage={{p.Id}}">edit</a> {{(string.IsNullOrWhiteSpace(p.Slug) ? "<small>embedded on home</small>" : $"<a href=\"/page/{WebUtility.HtmlEncode(p.Slug)}\">view</a>")}} <form method="post" action="/admin/page/delete/{{p.Id}}" style="display:inline;"><button type="submit">delete</button></form></li>"""));

    var postId = editingPost?.Id.ToString() ?? "";
    var postTitle = editingPost is null ? "" : WebUtility.HtmlEncode(editingPost.Title);
    var postSlug = editingPost is null ? "" : WebUtility.HtmlEncode(editingPost.Slug);
    var postCategory = editingPost is null ? "default" : WebUtility.HtmlEncode(editingPost.Category);
    var postContent = editingPost is null ? "" : WebUtility.HtmlEncode(editingPost.Content);
    var pageId = editingPage?.Id.ToString() ?? "";
    var pageTitle = editingPage is null ? "" : WebUtility.HtmlEncode(editingPage.Title);
    var pageSlug = editingPage is null ? "" : WebUtility.HtmlEncode(editingPage.Slug);
    var pageCategory = editingPage is null ? "default" : WebUtility.HtmlEncode(editingPage.Category);
    var pageContent = editingPage is null ? "" : WebUtility.HtmlEncode(editingPage.Content);
    var importBanner = importedCount is null ? "" : $$"""<p style="padding:.6rem;border:1px solid #bbf7d0;background:#f0fdf4;border-radius:8px;">CSV import completed: <strong>{{importedCount}}</strong> post(s) processed.</p>""";

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Admin</title>
  <style>
    body {
      font-family: Segoe UI, Arial, sans-serif;
      margin: 0;
      background-color: #c7e8e1;
      background-image: url('/images/book-corner.png');
      background-repeat: no-repeat;
      background-position: right bottom;
      background-attachment: fixed;
      background-size: 420px auto;
      color: #1f2937;
    }
    main { max-width: 1100px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); }
    .menu { display: flex; gap: 0.8rem; flex-wrap: wrap; padding: 0.75rem; background: #f1f5f9; border-radius: 8px; margin-bottom: 1rem; }
    .menu a { color: #1d4ed8; text-decoration: none; font-weight: 600; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
    input, textarea, button { width: 100%; box-sizing: border-box; margin: 0.35rem 0 1rem; padding: 0.6rem; font: inherit; }
    textarea { min-height: 200px; }
    button { width: auto; cursor: pointer; }
    ul { list-style: none; padding: 0; }
    li { margin: 0.6rem 0; display: flex; gap: 0.7rem; align-items: center; flex-wrap: wrap; }
    .drop-zone { border: 2px dashed #93c5fd; border-radius: 10px; background: #eff6ff; padding: 1rem; text-align: center; margin: 0.5rem 0 1rem; color: #1e3a8a; }
    .drop-zone.dragover { background: #dbeafe; border-color: #2563eb; }
    .toolbar { display: flex; gap: 0.4rem; flex-wrap: wrap; margin-bottom: 0.5rem; }
    .toolbar button { margin: 0; }
    .inline-buttons { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-top: 0.5rem; }
  </style>
</head>
<body>
  <main>
    {{RenderMenuBar(pages, customNavItems, isAuthenticated, isAdmin, userEmail)}}
    <h1>Content Admin</h1>
    {{importBanner}}
    <h2>Navigation Bar</h2>
    <form method="post" action="/admin/settings/navigation">
      <label>Custom links (one per line: Label|/path-or-url)</label>
      <textarea name="navigationItems" style="min-height:120px;">{{WebUtility.HtmlEncode(navEditorText)}}</textarea>
      <button type="submit">Save Navigation</button>
    </form>
    {{RenderUploadPanel(uploadedPath)}}
    <div class="grid">
      <section>
        <h2>Post Editor</h2>
        <form method="post" action="/admin/save">
          <input type="hidden" name="id" value="{{postId}}" />
          <label>Title</label>
          <input name="title" required value="{{postTitle}}" />
          <label>Slug</label>
          <input name="slug" required value="{{postSlug}}" />
          <label>Category</label>
          <input name="category" required value="{{postCategory}}" />
          <label>Content (Markdown)</label>
          {{RenderToolbar("post-content")}}
          <textarea id="post-content" name="content" required>{{postContent}}</textarea>
          <button type="submit">{{(editingPost is null ? "Create post" : "Update post")}}</button>
        </form>
        <h3>Posts</h3>
        <ul>{{postList}}</ul>
      </section>
      <section>
        <h2>Page Manager</h2>
        <form method="post" action="/admin/page/save">
          <input type="hidden" name="id" value="{{pageId}}" />
          <label>Title</label>
          <input name="title" required value="{{pageTitle}}" />
          <label>Slug</label>
          <input name="slug" value="{{pageSlug}}" />
          <p><small>Leave empty to display this page directly on the home page.</small></p>
          <label>Category</label>
          <input name="category" required value="{{pageCategory}}" />
          <label>Content (Markdown)</label>
          {{RenderToolbar("page-content")}}
          <textarea id="page-content" name="content" required>{{pageContent}}</textarea>
          <button type="submit">{{(editingPage is null ? "Create page" : "Update page")}}</button>
        </form>
        <h3>Pages</h3>
        <ul>{{pageList}}</ul>
      </section>
    </div>
  </main>
  <script>
    (function () {
      const fileInput = document.getElementById('upload-file');
      const dropZone = document.getElementById('upload-drop-zone');
      const uploadForm = document.getElementById('upload-form');
      let activeEditor = document.getElementById('post-content');
      document.querySelectorAll('textarea').forEach(function (ta) { ta.addEventListener('focus', function () { activeEditor = ta; }); });

      if (dropZone && fileInput && uploadForm) {
        dropZone.addEventListener('click', function () { fileInput.click(); });
        fileInput.addEventListener('change', function () { if (fileInput.files.length > 0) uploadForm.submit(); });
        ['dragenter', 'dragover'].forEach(function (n) {
          dropZone.addEventListener(n, function (e) { e.preventDefault(); e.stopPropagation(); dropZone.classList.add('dragover'); });
        });
        ['dragleave', 'drop'].forEach(function (n) {
          dropZone.addEventListener(n, function (e) { e.preventDefault(); e.stopPropagation(); dropZone.classList.remove('dragover'); });
        });
        dropZone.addEventListener('drop', function (e) {
          if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            fileInput.files = e.dataTransfer.files;
            uploadForm.submit();
          }
        });
      }

      function wrapSelection(textarea, before, after) {
        const start = textarea.selectionStart || 0;
        const end = textarea.selectionEnd || 0;
        const value = textarea.value;
        const selected = value.substring(start, end) || 'text';
        textarea.value = value.substring(0, start) + before + selected + after + value.substring(end);
        const caret = start + before.length + selected.length + after.length;
        textarea.focus();
        textarea.setSelectionRange(caret, caret);
      }

      document.querySelectorAll('[data-tool]').forEach(function (btn) {
        btn.addEventListener('click', function () {
          const target = document.getElementById(btn.getAttribute('data-target'));
          if (!target) return;
          wrapSelection(target, btn.getAttribute('data-before') || '', btn.getAttribute('data-after') || '');
        });
      });

      document.querySelectorAll('[data-insert]').forEach(function (btn) {
        btn.addEventListener('click', function () {
          const text = btn.getAttribute('data-insert') || '';
          const targetId = btn.getAttribute('data-target');
          const target = targetId ? document.getElementById(targetId) : activeEditor;
          if (!target) return;
          const prefix = target.value.length > 0 && !target.value.endsWith('\n') ? '\n\n' : '';
          target.value += prefix + text;
          target.focus();
        });
      });
    })();
  </script>
</body>
</html>
""";
}

static string RenderToolbar(string targetId)
{
    return $$"""
<div class="toolbar">
  <button type="button" data-tool="1" data-target="{{targetId}}" data-before="# " data-after="">H1</button>
  <button type="button" data-tool="1" data-target="{{targetId}}" data-before="## " data-after="">H2</button>
  <button type="button" data-tool="1" data-target="{{targetId}}" data-before="**" data-after="**">Bold</button>
  <button type="button" data-tool="1" data-target="{{targetId}}" data-before="*" data-after="*">Italic</button>
  <button type="button" data-tool="1" data-target="{{targetId}}" data-before="- " data-after="">List</button>
  <button type="button" data-tool="1" data-target="{{targetId}}" data-before="[" data-after="](https://)">Link</button>
</div>
""";
}

static string RenderUploadPanel(string uploadedPath)
{
    var uploadNote = "";
    if (!string.IsNullOrWhiteSpace(uploadedPath) && uploadedPath.StartsWith("/uploads/", StringComparison.Ordinal))
    {
        var safePath = WebUtility.HtmlEncode(uploadedPath);
        uploadNote = $$"""
<div style="padding:0.8rem; background:#ecfeff; border:1px solid #bae6fd; border-radius:8px; margin-bottom:1rem;">
  <strong>Upload saved:</strong> <a href="{{safePath}}" target="_blank" rel="noopener">{{safePath}}</a>
  <div class="inline-buttons">
    <button type="button" data-insert="![Alt text]({{safePath}})">Insert Image</button>
    <button type="button" data-insert="[Download file]({{safePath}})">Insert File Link</button>
  </div>
</div>
""";
    }

    return $$"""
{{uploadNote}}
<h2>Upload file/image</h2>
<form id="upload-form" method="post" action="/admin/upload" enctype="multipart/form-data">
  <div id="upload-drop-zone" class="drop-zone">Drag and drop file here, or click to select.</div>
  <input id="upload-file" type="file" name="file" required />
  <button type="submit">Upload</button>
</form>
<p><small>Allowed: png, jpg, jpeg, gif, webp, svg, pdf, txt, md, doc, docx (max 10 MB)</small></p>
""";
}

static string RenderMenuBar(IReadOnlyList<SitePage> pages, IReadOnlyList<SiteSettingsStore.NavigationItem> customNavItems, bool isAuthenticated, bool isAdmin, string userEmail)
{
    var pageLinks = string.Join(" ", pages
        .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
        .Select(p => $"<a href=\"/page/{WebUtility.HtmlEncode(p.Slug)}\">{WebUtility.HtmlEncode(p.Title)}</a>"));
    var customLinks = string.Join(" ", customNavItems.Select(x =>
        $"<a href=\"{WebUtility.HtmlEncode(x.Url)}\">{WebUtility.HtmlEncode(x.Label)}</a>"));
    var authLinks = isAuthenticated
        ? $$"""<span style="color:#334155;">{{WebUtility.HtmlEncode(userEmail)}}</span>{{(isAdmin ? "<a href=\"/admin\">Add Post</a>" : "")}}<form method="post" action="/logout" style="display:inline;margin:0;"><button type="submit" style="font:inherit;padding:.25rem .6rem;cursor:pointer;">Sign out</button></form>"""
        : "<a href=\"/login?returnUrl=/admin\">Sign in</a>";

    return $$"""<nav class="menu"><a href="/">Home</a><a href="/contact">Contact</a>{{customLinks}}{{pageLinks}}{{authLinks}}</nav>""";
}

static string RenderMarkup(string raw)
{
    var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    return Markdown.ToHtml(raw ?? string.Empty, pipeline);
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

static string NormalizeCategory(string? category)
{
    return string.IsNullOrWhiteSpace(category) ? "default" : category.Trim();
}

static string FormatCentralTime(DateTimeOffset utcTime)
{
    TimeZoneInfo timeZone;
    try
    {
        timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    }
    catch (TimeZoneNotFoundException)
    {
        timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    }

    var local = TimeZoneInfo.ConvertTime(utcTime, timeZone);
    return local.ToString("MMMM d, yyyy 'at' h:mm tt 'CT'");
}

static string RenderContact(IReadOnlyList<SitePage> pages, IReadOnlyList<SiteSettingsStore.NavigationItem> customNavItems, bool isAuthenticated, bool isAdmin, string userEmail)
{
    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Contact</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 960px; margin: 2rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); }
    .menu { display: flex; gap: 0.8rem; flex-wrap: wrap; padding: 0.75rem; background: #f1f5f9; border-radius: 8px; margin-bottom: 1rem; }
    .menu a { color: #1d4ed8; text-decoration: none; font-weight: 600; }
  </style>
</head>
<body>
  <main>
    {{RenderMenuBar(pages, customNavItems, isAuthenticated, isAdmin, userEmail)}}
    <h1>Contact</h1>
    <p>For feedback or corrections, contact the site owner.</p>
    <p>Email: <a href="mailto:contact@example.com">contact@example.com</a></p>
  </main>
</body>
</html>
""";
}

static string RenderLogin(string returnUrl)
{
    var safeReturnUrl = WebUtility.HtmlEncode(NormalizeReturnUrl(returnUrl));
    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Sign in</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f7fb; color: #1f2937; }
    main { max-width: 580px; margin: 3rem auto; background: white; padding: 2rem; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.08); }
    button { padding: 0.7rem 1rem; font: inherit; cursor: pointer; }
  </style>
</head>
<body>
  <main>
    <h1>Sign in</h1>
    <p>Use your Microsoft account to access the blog editor.</p>
    <form method="post" action="/login?returnUrl={{safeReturnUrl}}">
      <button type="submit">Continue with Microsoft</button>
    </form>
  </main>
</body>
</html>
""";
}

static string GetUserEmail(ClaimsPrincipal user)
{
    return user.FindFirstValue("preferred_username")
        ?? user.FindFirstValue(ClaimTypes.Upn)
        ?? user.FindFirstValue(ClaimTypes.Email)
        ?? user.Identity?.Name
        ?? "unknown";
}

static bool IsAdminUser(ClaimsPrincipal user, string adminEmail)
{
    if (!user.Identity?.IsAuthenticated ?? true)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(adminEmail))
    {
        return false;
    }

    return GetUserEmail(user).Equals(adminEmail, StringComparison.OrdinalIgnoreCase);
}

static string NormalizeReturnUrl(string returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/admin";
    }

    if (!returnUrl.StartsWith("/", StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal))
    {
        return "/admin";
    }

    return returnUrl;
}

static string SerializeNavigationItemsForEditor(IReadOnlyList<SiteSettingsStore.NavigationItem> items)
{
    return string.Join(
        Environment.NewLine,
        items.Select(i => $"{i.Label}|{i.Url}"));
}

static List<SiteSettingsStore.NavigationItem> ParseNavigationItemsFromEditor(string text)
{
    var items = new List<SiteSettingsStore.NavigationItem>();
    if (string.IsNullOrWhiteSpace(text))
    {
        return items;
    }

    var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    foreach (var raw in lines)
    {
        var line = raw.Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var separator = line.IndexOf('|');
        if (separator <= 0 || separator >= line.Length - 1)
        {
            continue;
        }

        var label = line[..separator].Trim();
        var url = line[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
        {
            continue;
        }

        items.Add(new SiteSettingsStore.NavigationItem
        {
            Label = label,
            Url = url
        });
    }

    return items;
}

static string GetCsvValue(List<string> row, int index)
{
    if (index < 0 || index >= row.Count)
    {
        return string.Empty;
    }

    return row[index];
}

static List<List<string>> ParseCsv(string csv)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var field = new System.Text.StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < csv.Length; i++)
    {
        var c = csv[i];

        if (c == '"')
        {
            if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
            {
                field.Append('"');
                i++;
            }
            else
            {
                inQuotes = !inQuotes;
            }

            continue;
        }

        if (c == ',' && !inQuotes)
        {
            row.Add(field.ToString());
            field.Clear();
            continue;
        }

        if ((c == '\n' || c == '\r') && !inQuotes)
        {
            if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
            {
                i++;
            }

            row.Add(field.ToString());
            field.Clear();
            rows.Add(row);
            row = new List<string>();
            continue;
        }

        field.Append(c);
    }

    if (field.Length > 0 || row.Count > 0)
    {
        row.Add(field.ToString());
        rows.Add(row);
    }

    return rows;
}
