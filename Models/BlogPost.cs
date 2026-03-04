namespace rtfm26.Models;

public sealed class BlogPost
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
