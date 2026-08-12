// Sri Rama Jayam
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using CopilotCodingAssistant.Models;
using CopilotCodingAssistant.Configuration;


var settings = AppSettings.Load();

var CopilotUrl = settings.CopilotUrl;
var RequiredAccount = settings.AccountEmail;
var RequiredModelMenuText = settings.PreferredModel;
var ProfileFolderName = settings.ProfileFolderName;

var profilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    ProfileFolderName,
    "EdgeProfile");

Directory.CreateDirectory(profilePath);

var command = args.Length == 0 ? "run" : args[0].ToLowerInvariant();

try
{
    using var playwright = await Playwright.CreateAsync();

    switch (command)
    {
        case "run":
            await RunPromptPipelineAsync(playwright, headless: true, directPrompt: null);
            break;

        case "send":
            var directPrompt = string.Join(" ", args.Skip(1)).Trim();
            if (string.IsNullOrWhiteSpace(directPrompt))
                throw new InvalidOperationException("No prompt was supplied after 'send'.");

            await RunPromptPipelineAsync(playwright, headless: true, directPrompt);
            break;

        case "run-headed":
            await RunPromptPipelineAsync(playwright, headless: false, directPrompt: null);
            break;

        case "setup":
            await RunSetupAsync(playwright);
            break;

        case "inspect":
            await RunInspectionAsync(playwright, headless: true);
            break;

        case "inspect-headed":
            await RunInspectionAsync(playwright, headless: false);
            break;

        case "help":
        case "-h":
        case "--help":
            PrintHelp();
            break;

        default:
            throw new InvalidOperationException($"Unknown command: {command}");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ERROR: {exception.Message}");
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

async Task RunPromptPipelineAsync(
    IPlaywright playwright,
    bool headless,
    string? directPrompt)
{
    var prompt = directPrompt;

    if (string.IsNullOrWhiteSpace(prompt))
    {
        Console.Write("Prompt: ");
        prompt = Console.ReadLine();
    }

    if (string.IsNullOrWhiteSpace(prompt))
        throw new InvalidOperationException("No prompt was entered.");

    Console.WriteLine(headless
        ? "Launching the dedicated Copilot session in the background..."
        : "Launching the dedicated Copilot session visibly...");

    await using var context = await LaunchContextAsync(playwright, headless);
    var page = await OpenCopilotAsync(context);

    await SelectRequiredAccountIfNeededAsync(page);
    await WaitForCopilotChatAsync(page);

    Console.WriteLine("IITK account ready.");

    await StartNewChatIfAvailableAsync(page);
    await SelectRequiredModelAsync(page);

    var editor = await FindChatEditorAsync(page);
    var responseCountBefore = await CountCopyResponseButtonsAsync(page);

    await editor.FillAsync(prompt.Trim());
    await page.WaitForTimeoutAsync(250);

    var editorText = await ReadEditorTextAsync(editor);
    var expectedPrompt = NormalizeEditorText(prompt);
    var observedPrompt = NormalizeEditorText(editorText);

    if (!string.Equals(observedPrompt, expectedPrompt, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "The prompt did not remain in the editor after normalization. " +
            $"Expected length: {expectedPrompt.Length}; observed length: {observedPrompt.Length}; " +
            $"observed text: '{observedPrompt}'.");
    }

    Console.WriteLine("Prompt entered and verified through the DOM.");

    await editor.FocusAsync();
    await SubmitPromptAsync(page, editor);
    Console.WriteLine("Prompt submitted. Waiting for GPT 5.6 Think deeper...");

    await WaitForGenerationAsync(page, responseCountBefore, TimeSpan.FromMinutes(5));

    Console.WriteLine("Generation completed. Extracting the newest response...");

    var result = await ExtractLatestResponseAsync(page);

    if (string.IsNullOrWhiteSpace(result.FullText))
        throw new InvalidOperationException("The newest response container was found, but its text was empty.");

    var outputDirectory = Directory.GetCurrentDirectory();
    var textPath = Path.Combine(outputDirectory, "last-response.txt");
    var jsonPath = Path.Combine(outputDirectory, "last-response.json");
    var codeDirectory = Path.Combine(outputDirectory, "last-code-blocks");

    await File.WriteAllTextAsync(textPath, result.FullText, Encoding.UTF8);

    if (Directory.Exists(codeDirectory))
        Directory.Delete(codeDirectory, recursive: true);

    Directory.CreateDirectory(codeDirectory);

    for (var index = 0; index < result.CodeBlocks.Count; index++)
    {
        var codeBlock = result.CodeBlocks[index];
        var extension = LanguageToExtension(codeBlock.Language);
        var codePath = Path.Combine(
            codeDirectory,
            $"code-{index + 1}{extension}");

        await File.WriteAllTextAsync(codePath, codeBlock.Code, Encoding.UTF8);
    }

    var json = JsonSerializer.Serialize(
        result,
        new JsonSerializerOptions
        {
            WriteIndented = true
        });

    await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8);

    Console.WriteLine();
    Console.WriteLine("--- Copilot response ---");
    Console.WriteLine(result.FullText);
    Console.WriteLine("------------------------");
    Console.WriteLine($"Code blocks: {result.CodeBlocks.Count}");
    Console.WriteLine($"Saved text: {textPath}");
    Console.WriteLine($"Saved JSON: {jsonPath}");
    Console.WriteLine($"Saved code: {codeDirectory}");
}

async Task<IBrowserContext> LaunchContextAsync(
    IPlaywright playwright,
    bool headless)
{
    return await playwright.Chromium.LaunchPersistentContextAsync(
        profilePath,
        new BrowserTypeLaunchPersistentContextOptions
        {
            Channel = "msedge",
            Headless = headless,
            ViewportSize = new ViewportSize
            {
                Width = 1440,
                Height = 1000
            }
        });
}

async Task<IPage> OpenCopilotAsync(IBrowserContext context)
{
    var page = context.Pages.FirstOrDefault()
        ?? await context.NewPageAsync();

    await page.GotoAsync(
        CopilotUrl,
        new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120000
        });

    return page;
}

async Task RunSetupAsync(IPlaywright playwright)
{
    Console.WriteLine("Opening the dedicated Edge automation profile.");

    await using var context = await LaunchContextAsync(playwright, headless: false);
    var page = await OpenCopilotAsync(context);

    await SelectRequiredAccountIfNeededAsync(page);
    await WaitForCopilotChatAsync(page);

    Console.WriteLine("IITK account selected and Copilot Chat is ready.");
    Console.WriteLine("Press Enter to close the setup browser.");
    Console.ReadLine();
}

async Task RunInspectionAsync(IPlaywright playwright, bool headless)
{
    await using var context = await LaunchContextAsync(playwright, headless);
    var page = await OpenCopilotAsync(context);

    await SelectRequiredAccountIfNeededAsync(page);
    await WaitForCopilotChatAsync(page);
    await page.WaitForTimeoutAsync(2000);

    var bodyText = await page.Locator("body").InnerTextAsync();
    var report = new StringBuilder();

    report.AppendLine($"Title: {await page.TitleAsync()}");
    report.AppendLine($"URL: {page.Url}");
    report.AppendLine($"Contenteditable: {await page.Locator("[contenteditable='true']").CountAsync()}");
    report.AppendLine($"Textbox role: {await page.GetByRole(AriaRole.Textbox).CountAsync()}");
    report.AppendLine($"Copy Response buttons: {await CountCopyResponseButtonsAsync(page)}");
    report.AppendLine();
    report.AppendLine(bodyText.Length > 5000 ? bodyText[..5000] : bodyText);

    var outputPath = Path.GetFullPath("website-inspection.txt");
    await File.WriteAllTextAsync(outputPath, report.ToString(), Encoding.UTF8);

    Console.WriteLine(report.ToString());
    Console.WriteLine($"Saved inspection to: {outputPath}");
}

async Task SelectRequiredAccountIfNeededAsync(IPage page)
{
    var pickerVisible =
        page.Url.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase)
        || await page.GetByText(
                "Pick an account",
                new PageGetByTextOptions { Exact = true })
            .CountAsync() > 0;

    if (!pickerVisible)
        return;

    Console.WriteLine($"Selecting IITK account: {RequiredAccount}");

    var emailRegex = new Regex(
        Regex.Escape(RequiredAccount),
        RegexOptions.IgnoreCase);

    var emailText = page.GetByText(
        emailRegex,
        new PageGetByTextOptions { Exact = false });

    await emailText.First.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    var accountTile = page
        .Locator("div, button, li, [role='button'], [role='listitem']")
        .Filter(new LocatorFilterOptions
        {
            HasTextRegex = emailRegex
        })
        .Last;

    if (await accountTile.CountAsync() > 0)
        await accountTile.ClickAsync();
    else
        await emailText.First.ClickAsync();

    await page.WaitForTimeoutAsync(1000);
}

async Task WaitForCopilotChatAsync(IPage page)
{
    var deadline = DateTime.UtcNow.AddMinutes(2);

    while (DateTime.UtcNow < deadline)
    {
        if (page.Url.Contains("m365.cloud.microsoft/chat", StringComparison.OrdinalIgnoreCase))
        {
            var editors = await page.GetByRole(
                    AriaRole.Textbox,
                    new PageGetByRoleOptions
                    {
                        NameRegex = new Regex(
                            "message copilot",
                            RegexOptions.IgnoreCase)
                    })
                .CountAsync();

            var editables = await page
                .Locator("[contenteditable='true']")
                .CountAsync();

            if (editors > 0 || editables > 0)
                return;
        }

        await page.WaitForTimeoutAsync(500);
    }

    throw new TimeoutException(
        $"Copilot Chat did not become ready. Current URL: {page.Url}");
}

async Task StartNewChatIfAvailableAsync(IPage page)
{
    var newChat = page.GetByText(
        "New chat",
        new PageGetByTextOptions { Exact = true });

    if (await newChat.CountAsync() > 0 && await newChat.First.IsVisibleAsync())
    {
        await newChat.First.ClickAsync();
        await page.WaitForTimeoutAsync(750);
        Console.WriteLine("New chat opened.");
    }
}

async Task SelectRequiredModelAsync(IPage page)
{
    const string topSelectorCss = "#gptModeSwitcher";
    const string gptTriggerCss =
        "[data-test-id='gptSubMenuModelTrigger-OpenAI']";
    const string thinkModelCss =
        "div[role='menuitemradio']:has(" +
        "svg[data-testid='checkmark-Gpt_5_6_Reasoning'])";

    var topSelector = page.Locator(topSelectorCss);

    await topSelector.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    var currentModel = NormalizeEditorText(
        await topSelector.InnerTextAsync());

    Console.WriteLine($"Model before selection: {currentModel}");

    if (currentModel.Contains(
            "GPT 5.6 Think",
            StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Model already selected: GPT 5.6 Think deeper.");
        return;
    }

    if (!currentModel.Equals("Auto", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Unexpected text in #gptModeSwitcher: '{currentModel}'.");
    }

    await topSelector.ClickAsync(
        new LocatorClickOptions
        {
            Timeout = 15000
        });

    var gptTrigger = page.Locator(gptTriggerCss);

    await gptTrigger.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

    var thinkModel = page.Locator(thinkModelCss);
    var submenuOpened = false;

    // A single synthetic click was occasionally acknowledged visually without
    // opening the submenu. Retry the exact stable GPT trigger until the exact
    // concrete model item actually becomes visible.
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        Console.WriteLine($"Opening GPT submenu, attempt {attempt}...");

        await gptTrigger.ClickAsync(
            new LocatorClickOptions
            {
                Timeout = 15000,
                Force = true
            });

        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            if (await thinkModel.CountAsync() > 0 &&
                await thinkModel.IsVisibleAsync())
            {
                submenuOpened = true;
                break;
            }

            await Task.Delay(100);
        }

        if (submenuOpened)
            break;
    }

    if (!submenuOpened)
    {
        await SaveModelSelectionDiagnosticsAsync(page);
        throw new InvalidOperationException(
            "The GPT submenu did not expose GPT 5.6 Think deeper after " +
            "three clicks. The prompt was not entered or sent.");
    }

    var optionText = NormalizeEditorText(
        await thinkModel.InnerTextAsync());

    if (!optionText.Equals(
            RequiredModelMenuText,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Unexpected submenu item text: '{optionText}'.");
    }

    Console.WriteLine("Selecting GPT 5.6 Think deeper...");

    await thinkModel.ClickAsync(
        new LocatorClickOptions
        {
            Timeout = 15000,
            Force = true
        });

    // Verify only the exact stable top button supplied by the page HTML.
    // The pipeline cannot continue while this button still says Auto.
    var selectionDeadline = DateTime.UtcNow.AddSeconds(20);
    string selectedText = string.Empty;

    while (DateTime.UtcNow < selectionDeadline)
    {
        selectedText = NormalizeEditorText(
            await topSelector.InnerTextAsync());

        if (selectedText.Contains(
                "GPT 5.6 Think",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"Model selected and verified through #gptModeSwitcher: " +
                $"{selectedText}");
            return;
        }

        await Task.Delay(100);
    }

    await SaveModelSelectionDiagnosticsAsync(page);

    throw new InvalidOperationException(
        $"MODEL_SELECTION_FAILED: #gptModeSwitcher still displayed " +
        $"'{selectedText}'. The prompt was not entered or sent.");
}

async Task SaveModelSelectionDiagnosticsAsync(IPage page)
{
    const string topSelectorCss = "#gptModeSwitcher";
    const string gptTriggerCss =
        "[data-test-id='gptSubMenuModelTrigger-OpenAI']";
    const string thinkModelCss =
        "div[role='menuitemradio']:has(" +
        "svg[data-testid='checkmark-Gpt_5_6_Reasoning'])";

    var lines = new List<string>
    {
        $"URL: {page.Url}"
    };

    var topSelector = page.Locator(topSelectorCss);
    lines.Add($"Top selector count: {await topSelector.CountAsync()}");

    if (await topSelector.CountAsync() > 0)
    {
        lines.Add(
            $"Top selector text: " +
            NormalizeEditorText(await topSelector.InnerTextAsync()));
        lines.Add(
            $"Top selector expanded: " +
            await topSelector.GetAttributeAsync("aria-expanded"));
    }

    var gptTrigger = page.Locator(gptTriggerCss);
    lines.Add($"GPT trigger count: {await gptTrigger.CountAsync()}");

    if (await gptTrigger.CountAsync() > 0)
    {
        lines.Add(
            $"GPT trigger visible: {await gptTrigger.IsVisibleAsync()}");
        lines.Add(
            $"GPT trigger expanded: " +
            await gptTrigger.GetAttributeAsync("aria-expanded"));
    }

    var thinkModel = page.Locator(thinkModelCss);
    lines.Add($"Think model count: {await thinkModel.CountAsync()}");

    if (await thinkModel.CountAsync() > 0)
    {
        lines.Add(
            $"Think model visible: {await thinkModel.IsVisibleAsync()}");
        lines.Add(
            $"Think model checked: " +
            await thinkModel.GetAttributeAsync("aria-checked"));
        lines.Add(
            $"Think model text: " +
            NormalizeEditorText(await thinkModel.InnerTextAsync()));
    }

    await File.WriteAllLinesAsync(
        "model-selection-debug.txt",
        lines,
        Encoding.UTF8);

    await page.ScreenshotAsync(
        new PageScreenshotOptions
        {
            Path = "model-selection-debug.png",
            FullPage = true
        });
}

async Task<ILocator> FindChatEditorAsync(IPage page)
{
    var namedTextbox = page.GetByRole(
        AriaRole.Textbox,
        new PageGetByRoleOptions
        {
            NameRegex = new Regex(
                "message copilot",
                RegexOptions.IgnoreCase)
        });

    if (await namedTextbox.CountAsync() > 0)
        return namedTextbox.First;

    var editable = page.Locator("[contenteditable='true']");

    if (await editable.CountAsync() > 0)
        return editable.Last;

    throw new InvalidOperationException("The Message Copilot editor was not found.");
}

async Task SubmitPromptAsync(IPage page, ILocator editor)
{
    // Press Enter on the exact editor. This is a DOM-level Playwright action,
    // not a physical keyboard event, and avoids selecting an unrelated button.
    await editor.PressAsync("Enter");
}

async Task WaitForGenerationAsync(
    IPage page,
    int responseCountBefore,
    TimeSpan timeout)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    var stopObserved = false;
    var newResponseObserved = false;
    var stablePolls = 0;
    var lastCount = responseCountBefore;

    while (DateTime.UtcNow < deadline)
    {
        var stopButtons = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions
            {
                NameRegex = new Regex("stop", RegexOptions.IgnoreCase)
            });

        var stopVisible = false;
        for (var index = 0; index < await stopButtons.CountAsync(); index++)
        {
            if (await stopButtons.Nth(index).IsVisibleAsync())
            {
                stopVisible = true;
                break;
            }
        }

        if (stopVisible)
            stopObserved = true;

        var currentCount = await CountCopyResponseButtonsAsync(page);
        if (currentCount > responseCountBefore)
            newResponseObserved = true;

        if (newResponseObserved && !stopVisible)
        {
            stablePolls = currentCount == lastCount
                ? stablePolls + 1
                : 0;

            if (stablePolls >= 3)
                return;
        }
        else
        {
            stablePolls = 0;
        }

        lastCount = currentCount;
        await page.WaitForTimeoutAsync(500);
    }

    throw new TimeoutException(
        $"Timed out waiting for a new Copilot response. " +
        $"Stop observed: {stopObserved}; initial responses: {responseCountBefore}; " +
        $"current responses: {await CountCopyResponseButtonsAsync(page)}.");
}

async Task<int> CountCopyResponseButtonsAsync(IPage page)
{
    return await page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions
            {
                NameRegex = new Regex(
                    "^copy response$",
                    RegexOptions.IgnoreCase)
            })
        .CountAsync();
}

async Task<CopilotResult> ExtractLatestResponseAsync(IPage page)
{
    var copyButtons = page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions
        {
            NameRegex = new Regex(
                "^copy response$",
                RegexOptions.IgnoreCase)
        });

    var copyCount = await copyButtons.CountAsync();
    if (copyCount == 0)
        throw new InvalidOperationException("No Copy Response button was found for the new answer.");

    var latestCopyButton = copyButtons.Last;
    var diagnostics = new StringBuilder();
    ILocator current = latestCopyButton;
    ILocator? bestContainer = null;
    string bestText = string.Empty;

    // The button's accessible name is not necessarily part of innerText.
    // Walk upward and choose the smallest useful ancestor containing substantial
    // response text, while avoiding the whole page.
    for (var level = 1; level <= 14; level++)
    {
        current = current.Locator("xpath=..");

        try
        {
            var text = (await current.InnerTextAsync()).Trim();
            var html = await current.EvaluateAsync<string>(
                "element => element.tagName + '|' + (element.getAttribute('role') || '') + '|' + (element.className || '')");

            diagnostics.AppendLine($"LEVEL {level} | Length={text.Length} | {html}");
            diagnostics.AppendLine(text.Length > 1200 ? text[..1200] : text);
            diagnostics.AppendLine();

            if (text.Length >= 10 && text.Length <= 50000)
            {
                // Keep the smallest useful ancestor. Later, larger ancestors are
                // considered only if the current candidate is still too short.
                if (bestContainer is null || bestText.Length < 40)
                {
                    bestContainer = current;
                    bestText = text;
                }

                if (text.Length >= 40)
                    break;
            }
        }
        catch (Exception exception)
        {
            diagnostics.AppendLine($"LEVEL {level} failed: {exception.Message}");
        }
    }

    await File.WriteAllTextAsync(
        "response-container-debug.txt",
        diagnostics.ToString(),
        Encoding.UTF8);

    if (bestContainer is null || string.IsNullOrWhiteSpace(bestText))
    {
        await page.ScreenshotAsync(
            new PageScreenshotOptions
            {
                Path = "response-extraction-debug.png",
                FullPage = true
            });

        throw new InvalidOperationException(
            "The latest response container could not be identified. " +
            "Diagnostics were saved to response-container-debug.txt and " +
            "response-extraction-debug.png.");
    }

    var cleaned = CleanResponseText(bestText);
    var codeBlocks = new List<CodeBlock>();
    var codeLocators = bestContainer.Locator("pre code, pre");
    var codeCount = await codeLocators.CountAsync();

    for (var index = 0; index < codeCount; index++)
    {
        var codeLocator = codeLocators.Nth(index);
        var code = (await codeLocator.InnerTextAsync()).Trim();

        if (string.IsNullOrWhiteSpace(code) ||
            codeBlocks.Any(existing => existing.Code == code))
        {
            continue;
        }

        var language = await codeLocator.GetAttributeAsync("data-language")
            ?? await codeLocator.GetAttributeAsync("class")
            ?? "text";

        codeBlocks.Add(new CodeBlock(NormalizeLanguage(language), code));
    }

    // Copilot sometimes renders code as a virtualized code preview rather
    // than semantic <pre><code>. Recover that representation from the text.
    if (codeBlocks.Count == 0)
    {
        var recovered = RecoverCodePreview(cleaned);
        if (!string.IsNullOrWhiteSpace(recovered.Code))
        {
            codeBlocks.Add(recovered);
        }
    }

    return new CopilotResult(
        Model: "GPT 5.6 Think deeper",
        FullText: cleaned,
        CodeBlocks: codeBlocks,
        CapturedAtUtc: DateTime.UtcNow);
}

async Task<string> ReadEditorTextAsync(ILocator editor)
{
    try
    {
        var textContent = await editor.TextContentAsync();
        if (!string.IsNullOrEmpty(textContent))
            return textContent;
    }
    catch
    {
    }

    return await editor.InnerTextAsync();
}

string NormalizeEditorText(string? text)
{
    if (string.IsNullOrEmpty(text))
        return string.Empty;

    return text
        .Replace("\u200B", string.Empty)
        .Replace("\u200C", string.Empty)
        .Replace("\u200D", string.Empty)
        .Replace("\uFEFF", string.Empty)
        .Replace("\u00A0", " ")
        .Replace("\r\n", "\n")
        .Replace('\r', '\n')
        .Trim();
}

string CleanResponseText(string text)
{
    text = WebUtility.HtmlDecode(text);

    var lines = text
        .Replace("\r\n", "\n")
        .Replace('\r', '\n')
        .Split('\n')
        .Select(line => line.TrimEnd())
        .Where(line => !line.Trim().Equals("Copy Response", StringComparison.OrdinalIgnoreCase))
        .Where(line => !line.Trim().Equals("I like something", StringComparison.OrdinalIgnoreCase))
        .Where(line => !line.Trim().Equals("I don't like something", StringComparison.OrdinalIgnoreCase))
        .Where(line => !line.Trim().Equals("More options", StringComparison.OrdinalIgnoreCase))
        .ToList();

    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        lines.RemoveAt(0);

    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        lines.RemoveAt(lines.Count - 1);

    return string.Join(Environment.NewLine, lines).Trim();
}

CodeBlock RecoverCodePreview(string responseText)
{
    var lines = responseText
        .Replace("\r\n", "\n")
        .Replace('\r', '\n')
        .Split('\n')
        .ToList();

    var language = "text";
    var start = 0;

    if (lines.Count > 0 &&
        lines[0].Trim().Equals("Copilot said:", StringComparison.OrdinalIgnoreCase))
    {
        start = 1;
    }

    if (start < lines.Count &&
        Regex.IsMatch(lines[start].Trim(), "^[A-Za-z+#.]+$"))
    {
        language = NormalizeLanguage(lines[start].Trim());
        start++;
    }

    var codeLines = new List<string>();

    for (var index = start; index < lines.Count; index++)
    {
        var line = lines[index];

        // Virtualized code previews often emit line numbers as standalone rows.
        if (Regex.IsMatch(line.Trim(), @"^\d+$"))
            continue;

        codeLines.Add(line);
    }

    while (codeLines.Count > 0 && string.IsNullOrWhiteSpace(codeLines[0]))
        codeLines.RemoveAt(0);

    while (codeLines.Count > 0 && string.IsNullOrWhiteSpace(codeLines[^1]))
        codeLines.RemoveAt(codeLines.Count - 1);

    var code = string.Join(Environment.NewLine, codeLines).Trim();

    // Only treat the fallback as code when common code syntax exists.
    var looksLikeCode =
        code.Contains("#include", StringComparison.Ordinal)
        || code.Contains("int main", StringComparison.Ordinal)
        || code.Contains("public static", StringComparison.Ordinal)
        || code.Contains("def ", StringComparison.Ordinal)
        || code.Contains("function ", StringComparison.Ordinal)
        || code.Contains("console.log", StringComparison.Ordinal)
        || code.Contains("printf(", StringComparison.Ordinal);

    return looksLikeCode
        ? new CodeBlock(language, code)
        : new CodeBlock("text", string.Empty);
}

string NormalizeLanguage(string raw)
{
    var match = Regex.Match(raw, "language-([a-zA-Z0-9_+#.-]+)");
    if (match.Success)
        return match.Groups[1].Value.ToLowerInvariant();

    raw = raw.Trim().ToLowerInvariant();
    return string.IsNullOrWhiteSpace(raw) ? "text" : raw;
}

string LanguageToExtension(string language)
{
    return language.ToLowerInvariant() switch
    {
        "c" => ".c",
        "cpp" or "c++" => ".cpp",
        "csharp" or "cs" or "c#" => ".cs",
        "python" or "py" => ".py",
        "javascript" or "js" => ".js",
        "typescript" or "ts" => ".ts",
        "java" => ".java",
        "go" => ".go",
        "rust" => ".rs",
        "html" => ".html",
        "css" => ".css",
        "json" => ".json",
        "xml" => ".xml",
        "sql" => ".sql",
        "bash" or "shell" or "sh" => ".sh",
        "powershell" or "ps1" => ".ps1",
        "markdown" or "md" => ".md",
        _ => ".txt"
    };
}

void PrintHelp()
{
    Console.WriteLine("Copilot website MVP");
    Console.WriteLine();
    Console.WriteLine("  dotnet run");
    Console.WriteLine("      Ask for a terminal prompt and run in the background.");
    Console.WriteLine();
    Console.WriteLine("  dotnet run -- send \"Your prompt\"");
    Console.WriteLine("      Send a prompt directly in the background.");
    Console.WriteLine();
    Console.WriteLine("  dotnet run -- run-headed");
    Console.WriteLine("      Run visibly for debugging.");
    Console.WriteLine();
    Console.WriteLine("  dotnet run -- setup");
    Console.WriteLine("  dotnet run -- inspect");
    Console.WriteLine("  dotnet run -- inspect-headed");
}

