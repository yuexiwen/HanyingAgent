namespace HanyingAgent.Infrastructure.Mcp;

public sealed class McpSettings
{
    public const string SectionName = "Mcp";
    public List<McpServerOptions> Servers { get; set; } = [];
}

public sealed class McpServerOptions
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 60;
    public McpStdioOptions Stdio { get; set; } = new();
}

public sealed class McpStdioOptions
{
    public string Command { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; } = [];
}
