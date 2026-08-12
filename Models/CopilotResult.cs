// Sri Rama Jayam
namespace CopilotCodingAssistant.Models;

public sealed record CopilotResult(
    string Model,
    string FullText,
    List<CodeBlock> CodeBlocks,
    DateTime CapturedAtUtc);