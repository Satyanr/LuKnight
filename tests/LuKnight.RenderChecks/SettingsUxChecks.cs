using System.IO;
using System.Xml.Linq;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static void
        CheckSettingsUx()
    {
        AppSettings armed =
            new()
            {
                Chat =
                    new ChatSettings
                    {
                        UseApplicationContext =
                            true,

                        UseFileContext =
                            true,

                        UseDesktopActions =
                            true
                    },

                Companion =
                    new CompanionSettings
                    {
                        Enabled =
                            true
                    }
            };


        AppSettings disabled =
            SettingsCapabilityPolicy
                .SetApplicationAwareness(
                    armed,
                    false);


        Require(
            !disabled.Chat
                .UseApplicationContext &&
            !disabled.Companion
                .Enabled,
            "Disabling application awareness left proactive companion armed.");


        Require(
            disabled.Chat
                .UseFileContext &&
            disabled.Chat
                .UseDesktopActions,
            "Application-awareness dependency changed unrelated permissions.");


        AppSettings reenabled =
            SettingsCapabilityPolicy
                .SetApplicationAwareness(
                    disabled,
                    true);


        Require(
            reenabled.Chat
                .UseApplicationContext &&
            !reenabled.Companion
                .Enabled,
            "Re-enabling application awareness silently re-enabled proactive companion.");


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


        string xaml =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "Views",
                    "SettingsWindow.xaml"));


        Require(
            !xaml.Contains(
                "Content=\"Save\"",
                StringComparison.Ordinal),
            "Settings still exposes a redundant manual Save button.");


        Require(
            xaml.Contains(
                "Perubahan disimpan otomatis",
                StringComparison.Ordinal),
            "Settings no longer communicates autosave behavior.");


        Require(
            xaml.Contains(
                "Product.AccessSummary",
                StringComparison.Ordinal) &&
            xaml.Contains(
                "Product.ContextProviderNotice",
                StringComparison.Ordinal),
            "Settings has no consolidated privacy/access summary.");


        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement markup = XElement.Parse(xaml);
        XElement voiceSubmission = markup
            .Descendants(presentation + "ComboBox")
            .Single(control => (string?)control.Attribute("ItemsSource") ==
                "{Binding Product.VoiceSubmissionModes}");

        Require(
            (string?)voiceSubmission.Attribute("IsEnabled") ==
                "{Binding Product.UseVoiceInput}",
            "After transcription remains active while Voice Input is off.");

        Require(
            xaml.Contains(
                "tidak aktif kembali otomatis",
                StringComparison.Ordinal),
            "Proactive dependency behavior is not disclosed to the user.");


        Require(
            xaml.Contains(
                "konfirmasi dua tahap",
                StringComparison.Ordinal),
            "Desktop action confirmation behavior is not explained.");
    }
}
