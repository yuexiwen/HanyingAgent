namespace HanyingAgent.Host.Configuration;

public sealed class OllamaSettings
{
    public const string SectionName = "Ollama";

    public string BaseAddress { get; set; } = "http://127.0.0.1:11434/";
    public string ModelName { get; set; } = "qwen2.5:1.5b";
    public int ContextWindowTokens { get; set; } = 4096;
    public int TimeoutSeconds { get; set; } = 120;

    public static bool IsValidAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.UserInfo.Length == 0 && uri.AbsolutePath.EndsWith('/');
}
