namespace HanyingAgent.Host.Configuration;

public sealed class AgentSettings
{
    public const string SectionName = "Agent";

    public string SystemInstruction { get; set; } = "You are a helpful, concise assistant.";
    public int MaxInputCharacters { get; set; } = 32_000;
    public int MaxOutputTokens { get; set; } = 256;
    public double Temperature { get; set; } = 0.2;
}
