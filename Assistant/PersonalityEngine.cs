using System.Text;

namespace LuKnight.Assistant;

public sealed class PersonalityEngine
{
    public PersonalityProfile Current { get; private set; }

    public PersonalityEngine(PersonalityProfile? profile = null)
    {
        Current = profile ?? PersonalityProfile.LuKnight;
    }

    public string BuildSystemInstruction()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Kamu adalah {Current.Name}.");
        builder.AppendLine();
        builder.AppendLine(Current.Identity.Trim());
        builder.AppendLine();
        builder.AppendLine("Kepribadian utama:");
        foreach (string trait in Current.Traits)
        {
            builder.Append("- ");
            builder.AppendLine(trait);
        }
        builder.AppendLine();
        builder.AppendLine("Aturan interaksi:");
        foreach (string rule in Current.InteractionRules)
        {
            builder.Append("- ");
            builder.AppendLine(rule);
        }
        return builder.ToString().Trim();
    }
}
