// Sri Rama Jayam
namespace CopilotCodingAssistant.Copilot;

public static class CopilotSelectors
{
    public const string ChatEditor =
	"#m365-chat-editor-target-element";
    public const string ModelSelector =
        "#gptModeSwitcher";

    public const string SendButton =
        "button[type='submit'][aria-label='Send']";

    public const string AutoModel =
        "div[role='menuitemradio']:has(" +
        "svg[data-testid='checkmark-Magic'])";

    public const string QuickResponseModel =
        "div[role='menuitemradio']:has(" +
        "svg[data-testid='checkmark-Chat'])";

    public const string ThinkDeeperModel =
        "div[role='menuitemradio']:has(" +
        "svg[data-testid='checkmark-Reasoning'])";

    public const string GptSubmenuTrigger =
        "[data-test-id='gptSubMenuModelTrigger-OpenAI']";

    public const string Gpt56ThinkModel =
        "div[role='menuitemradio']:has(" +
        "svg[data-testid='checkmark-Gpt_5_6_Reasoning'])";

    public const string CodeBlocks =
        "pre code, pre";
}