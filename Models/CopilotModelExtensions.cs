// Sri Rama Jayam
namespace CopilotCodingAssistant.Models;

public static class CopilotModelExtensions
{
    public static string ToMenuText(
        this CopilotModel model)
    {
        return model switch
        {
            CopilotModel.Auto =>
                "Auto",

            CopilotModel.QuickResponse =>
                "Quick response",

            CopilotModel.ThinkDeeper =>
                "Think deeper",

            CopilotModel.Gpt56ThinkDeeper =>
                "GPT 5.6 Think deeper",

            CopilotModel.Gpt56QuickResponse =>
                "GPT 5.6 Quick response",

            CopilotModel.Gpt55QuickResponse =>
                "GPT 5.5 Quick response",

            _ => throw new ArgumentOutOfRangeException(
                nameof(model),
                model,
                "Unsupported Copilot model.")
        };
    }

    public static string ToTopSelectorText(
        this CopilotModel model)
    {
        return model switch
        {
            CopilotModel.Auto =>
                "Auto",

            CopilotModel.QuickResponse =>
                "Quick response",

            CopilotModel.ThinkDeeper =>
                "Think deeper",

            CopilotModel.Gpt56ThinkDeeper =>
                "GPT 5.6 Think",

            CopilotModel.Gpt56QuickResponse =>
                "GPT 5.6 Quick",

            CopilotModel.Gpt55QuickResponse =>
                "GPT 5.5 Quick",

            _ => throw new ArgumentOutOfRangeException(
                nameof(model),
                model,
                "Unsupported Copilot model.")
        };
    }

    public static bool UsesGptSubmenu(
        this CopilotModel model)
    {
        return model is
            CopilotModel.Gpt56ThinkDeeper or
            CopilotModel.Gpt56QuickResponse or
            CopilotModel.Gpt55QuickResponse;
    }
}