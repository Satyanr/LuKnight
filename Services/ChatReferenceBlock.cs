namespace LuKnight.Services;

public sealed record ChatReferenceBlock(
    string Kind,
    string Name,
    string Content,
    bool Truncated = false);
