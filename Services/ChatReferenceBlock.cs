namespace LuKnight.Services;

public sealed record ChatReferenceBlock(
    string Kind,
    string Name,
    string Content,
    bool Truncated = false,
    string? MimeType = null,
    string? Base64Data = null)
{
    public bool HasInlineData =>
        !string.IsNullOrWhiteSpace(MimeType) &&
        !string.IsNullOrWhiteSpace(Base64Data);
}
