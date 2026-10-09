using System.IO;
using System.Xml.Linq;

internal static partial class Program
{
    private static void
        CheckDocumentationUx()
    {
        string root =
            AppContext.BaseDirectory;


        while (!File.Exists(
                   Path.Combine(
                       root,
                       "LuKnight.csproj")))
        {
            root =
                Directory
                    .GetParent(
                        root)?
                    .FullName
                ?? throw new
                    InvalidOperationException(
                        "Repository not found.");
        }


        string Read(
            string relative) =>
                File.ReadAllText(
                    Path.Combine(
                        root,
                        relative));


        string readme =
            Read(
                "README.md");

        string guide =
            Read(
                "USER_GUIDE.md");

        string settingsDoc =
            Read(
                "SETTINGS_WINDOW.md");

        string xaml =
            Read(
                "Views/SettingsWindow.xaml");

        string code =
            Read(
                "Views/SettingsWindow.xaml.cs");


        Require(
            !readme.Contains(
                "future UI Automation",
                StringComparison.OrdinalIgnoreCase) &&
            !readme.Contains(
                "future Proactive Behavior",
                StringComparison.OrdinalIgnoreCase),
            "README still describes implemented capabilities as future work.");


        Require(
            readme.Contains(
                "fresh installation",
                StringComparison.OrdinalIgnoreCase) &&
            readme.Contains(
                "**OFF**",
                StringComparison.Ordinal),
            "README does not document safe fresh-install defaults.");


        Require(
            guide.Contains(
                "Gemini tidak menjadi Windows executor.",
                StringComparison.Ordinal) &&
            guide.Contains(
                "two-stage confirmation",
                StringComparison.OrdinalIgnoreCase) &&
            guide.Contains(
                "Scheduled time bukan permission",
                StringComparison.Ordinal),
            "User guide is missing core execution safety boundaries.");


        Require(
            settingsDoc.Contains(
                "autosave",
                StringComparison.OrdinalIgnoreCase) &&
            settingsDoc.Contains(
                "Observe only",
                StringComparison.Ordinal) &&
            settingsDoc.Contains(
                "Sensitive",
                StringComparison.Ordinal),
            "Settings documentation is stale.");


        Require(
            readme.Contains("[`USER_GUIDE.md`](USER_GUIDE.md)", StringComparison.Ordinal) &&
            readme.Contains("Phase 12G", StringComparison.Ordinal),
            "README lacks the user guide link or release acceptance boundary.");

        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";


        XElement markup =
            XElement.Parse(
                xaml);


        XElement Named(
            string name) =>
                markup
                    .Descendants()
                    .Single(
                        element =>
                            (string?)element
                                .Attribute(
                                    x + "Name") ==
                            name);


        foreach (string controlName
                 in new[]
                 {
                     "ApiKeyInput",
                     "ApplicationAwarenessCheck",
                     "ProactiveSuggestionsCheck",
                     "ScreenContextCheck",
                     "VoiceInputCheck",
                     "DesktopActionsCheck",
                     "DesktopPermissionCombo",
                     "CompleteFirstRunButton"
                 })
        {
            XElement element =
                Named(
                    controlName);


            Require(
                !string.IsNullOrWhiteSpace(
                    (string?)element.Attribute(
                        "AutomationProperties.Name")),
                $"{controlName} has no accessibility name.");
        }


        Require(
            markup.Descendants(
                    presentation +
                    "ListBox")
                .Any(
                    element =>
                        (string?)element
                            .Attribute(
                                x + "Name") ==
                            "Navigation" &&
                        !string.IsNullOrWhiteSpace(
                            (string?)element
                                .Attribute(
                                    "AutomationProperties.Name"))),
            "Settings navigation has no accessibility name.");


        foreach (string content in new[]
                 { "Clear Long-Term Memory", "Delete selected local model" })
        {
            XElement button = markup.Descendants(presentation + "Button")
                .Single(element => (string?)element.Attribute("Content") == content);
            Require(
                !string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.Name")) &&
                !string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.HelpText")),
                $"{content} lacks an accessibility name or help text.");
        }

        Require(
            xaml.Contains(
                "IsKeyboardFocused",
                StringComparison.Ordinal),
            "Settings controls no longer expose keyboard focus indication.");


        Require(
            code.Contains(
                "Navigation.Focus()",
                StringComparison.Ordinal) &&
            code.Contains(
                "Key.Escape",
                StringComparison.Ordinal),
            "Settings keyboard entry/escape behavior is missing.");
    }
}
