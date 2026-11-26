namespace HanyingAgent.Host.Configuration;

public sealed class SkillSettings
{
    public const string SectionName = "Skills";
    public string? Directory { get; set; }
    public int MaxFileBytes { get; set; } = 65536;
    public int MaxSkillCount { get; set; } = 100;
}
