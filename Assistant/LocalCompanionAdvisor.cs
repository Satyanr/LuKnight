using LuKnight.Services;
using LuKnight.Models;

namespace LuKnight.Assistant;


public enum CompanionSuggestionKind
{
    Coding,
    Browsing,
    Creative,
    Office,
    Files,
    Communication
}


public sealed record CompanionSuggestionCandidate(
    string Key,
    CompanionSuggestionKind Kind,
    string Message,
    string SuggestedPrompt);


public sealed class LocalCompanionAdvisor
{
    public CompanionSuggestionCandidate?
        Evaluate(
            AssistantRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(
            context);


        //
        // Proactive companion respects
        // existing user/runtime state.
        //

        if (!context.RuntimeAvailable ||
            !context.CharacterVisible ||
            context.ChatOpen ||
            !context.ApplicationContextEnabled)
        {
            return null;
        }


        if (string.IsNullOrWhiteSpace(
                context.PrimaryApplicationKind) ||
            !Enum.TryParse(
                context.PrimaryApplicationKind,
                ignoreCase:
                    true,
                out DesktopApplicationKind kind))
        {
            return null;
        }


        return kind switch
        {
            DesktopApplicationKind.CodeEditor =>
                new CompanionSuggestionCandidate(
                    "app:code-editor",
                    CompanionSuggestionKind.Coding,
                    "Sedang coding? Aku bisa bantu menelusuri error, merencanakan perubahan, atau menjelaskan bagian kode kalau kamu memberikan konteksnya.",
                    "Bantu saya dengan pekerjaan coding yang sedang saya kerjakan."),


            DesktopApplicationKind.Browser =>
                new CompanionSuggestionCandidate(
                    "app:browser",
                    CompanionSuggestionKind.Browsing,
                    "Sedang browsing? Aku bisa bantu membandingkan, menjelaskan, atau merangkum informasi yang kamu berikan.",
                    "Bantu saya dengan informasi yang sedang saya cari."),


            DesktopApplicationKind.Creative =>
                new CompanionSuggestionCandidate(
                    "app:creative",
                    CompanionSuggestionKind.Creative,
                    "Sedang mengerjakan desain? Aku bisa bantu menyusun ide, checklist, penamaan aset, atau langkah ekspor.",
                    "Bantu saya merencanakan pekerjaan desain yang sedang saya kerjakan."),


            DesktopApplicationKind.Office =>
                new CompanionSuggestionCandidate(
                    "app:office",
                    CompanionSuggestionKind.Office,
                    "Sedang mengerjakan dokumen atau spreadsheet? Aku bisa bantu merapikan isi, menghitung, atau menyusun langkah kerja.",
                    "Bantu saya dengan dokumen atau spreadsheet yang sedang saya kerjakan."),


            DesktopApplicationKind.FileManager =>
                new CompanionSuggestionCandidate(
                    "app:file-manager",
                    CompanionSuggestionKind.Files,
                    "Sedang mengelola file? Aku bisa bantu mencari file atau menyusun langkah pengorganisasian.",
                    "Bantu saya mengelola file yang sedang saya kerjakan."),


            DesktopApplicationKind.Communication =>
                new CompanionSuggestionCandidate(
                    "app:communication",
                    CompanionSuggestionKind.Communication,
                    "Sedang menggunakan aplikasi komunikasi? Aku bisa bantu menyusun balasan atau ringkasan jika kamu memberikan konteksnya.",
                    "Bantu saya menyusun pesan atau ringkasan komunikasi."),


            _ =>
                null
        };
    }
    public CompanionSuggestionCandidate?
        Evaluate(
            AssistantRuntimeContext context,
            CompanionSettings preferences,
            AssistantCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        ArgumentNullException.ThrowIfNull(
            preferences);

        ArgumentNullException.ThrowIfNull(
            capabilities);


        if (!preferences.Enabled ||
            !capabilities.Contains(
                "feature:chat"))
        {
            return null;
        }


        CompanionSuggestionCandidate?
            candidate =
                Evaluate(
                    context);


        if (candidate is null)
            return null;


        bool allowed =
            candidate.Kind switch
            {
                CompanionSuggestionKind.Coding =>
                    preferences.Coding,

                CompanionSuggestionKind.Browsing =>
                    preferences.Browsing,

                CompanionSuggestionKind.Creative =>
                    preferences.Creative,

                CompanionSuggestionKind.Office =>
                    preferences.Office,

                CompanionSuggestionKind.Files =>
                    preferences.Files,

                CompanionSuggestionKind.Communication =>
                    preferences.Communication,

                _ =>
                    false
            };


        return allowed
            ? candidate
            : null;
    }
}
