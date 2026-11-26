namespace HanyingAgent.Host.Configuration;

public sealed class PromptSettings
{
    public const string SectionName = "Prompts";
    public string? Directory { get; set; }
    public int MaxFileBytes { get; set; } = 65536;
    public int MaxPromptCount { get; set; } = 100;
    public int MaxOutputCharacters { get; set; } = 65536;
}
