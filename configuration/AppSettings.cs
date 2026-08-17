// Sri Rama Jayam
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotCodingAssistant.Models;

namespace CopilotCodingAssistant.Configuration;

public sealed class AppSettings
{
    public string CopilotUrl { get; init; } =
        "https://m365.cloud.microsoft/chat";

    public string AccountEmail { get; init; } =
        string.Empty;

    public CopilotModel PreferredModel { get; init; } =
        CopilotModel.Gpt56ThinkDeeper;

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

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(
            new JsonStringEnumConverter());

        try
        {
            var json = File.ReadAllText(path);

            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    options);

            if (settings is null)
            {
                throw new InvalidOperationException(
                    $"Configuration file '{path}' is empty " +
                    "or invalid.");
            }

            settings.Validate();

            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "appsettings.local.json contains an " +
                "invalid property or preferredModel value.",
                exception);
        }
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

        if (!Enum.IsDefined(PreferredModel))
        {
            throw new InvalidOperationException(
                $"Unsupported preferred model: " +
                $"{PreferredModel}.");
        }

        if (string.IsNullOrWhiteSpace(
                ProfileFolderName))
        {
            throw new InvalidOperationException(
                "ProfileFolderName cannot be empty.");
        }
    }
}