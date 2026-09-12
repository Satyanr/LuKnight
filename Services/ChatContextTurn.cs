namespace LuKnight.Services;

public enum ChatContextRole
{
    User,
    Assistant
}

public sealed record ChatContextTurn(
    ChatContextRole Role,
    string Text);
