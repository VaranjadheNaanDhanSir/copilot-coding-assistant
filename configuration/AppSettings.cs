// Sri Rama Jayam
using System.Text.Json;

namespace CopilotCodingAssistant.Configuration;

public sealed class AppSettings
{
    public string CopilotUrl { get; init; } =
        "https://m365.cloud.microsoft/chat";

    public string AccountEmail { get; init; } =
        string.Empty;

    public string PreferredModel { get; init; } =
        "GPT 5.6 Think deeper";

    public string ProfileFolderName { get; init; } =
        "CopilotWebsiteAutomation";

    public static AppSettings Load(
        string path = "appsettings.local.json")
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Configuration file '{path}' was not found. " +
                "Create it using appsettings.example.json.");
        }

        var json = File.ReadAllText(path);

        var settings =
            JsonSerializer.Deserialize<AppSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"Configuration file '{path}' is invalid.");
        }

        settings.Validate();

        return settings;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountEmail))
        {
            throw new InvalidOperationException(
                "AccountEmail is required.");
        }

        if (!Uri.TryCreate(
                CopilotUrl,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "CopilotUrl must be a valid HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(PreferredModel))
        {
            throw new InvalidOperationException(
                "PreferredModel cannot be empty.");
        }

