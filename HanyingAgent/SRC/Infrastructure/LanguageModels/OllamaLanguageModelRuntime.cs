using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Responses;
using HanyingAgent.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HanyingAgent.Infrastructure.LanguageModels;

public sealed class OllamaLanguageModelRuntime : ILanguageModelRuntime
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly string _defaultModelName;
    private readonly int _contextWindowTokens;
    private readonly ILogger<OllamaLanguageModelRuntime> _logger;

    public OllamaLanguageModelRuntime(
        HttpClient client,
        string defaultModelName,
        int contextWindowTokens = 4096,
        ILogger<OllamaLanguageModelRuntime>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModelName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextWindowTokens);
        if (client.BaseAddress is not { IsAbsoluteUri: true } address ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
            address.Query.Length != 0 || address.Fragment.Length != 0 ||
            !address.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException(
                "HttpClient.BaseAddress must be an absolute HTTP(S) URL ending in '/' without a query or fragment.",
                nameof(client));
        }

        _client = client;
        _endpoint = new Uri(address, "api/chat");
        _defaultModelName = defaultModelName;
        _contextWindowTokens = contextWindowTokens;
        _logger = logger ?? NullLogger<OllamaLanguageModelRuntime>.Instance;
    }

    public async Task<ModelResponse> InvokeAsync(
        ModelInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.Messages);
        ArgumentNullException.ThrowIfNull(invocation.Tools);
        var tools = invocation.Tools.ToArray();
        if (tools.Any(tool => tool is null))
        {
            throw new ArgumentException("Tools must not contain null entries.", nameof(invocation));
        }
        if (tools.Length != 0)
        {
            throw new NotSupportedException("The text-only Ollama adapter does not support tool definitions.");
        }

        var source = invocation.Messages.ToArray();
        if (source.Length == 0)
        {
            throw new ArgumentException("At least one model message is required.", nameof(invocation));
        }
        var messages = source.Select(ToOllamaMessage).ToArray();
        var options = invocation.Options;
        var modelName = options?.ModelName ?? _defaultModelName;
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        if (options?.Temperature is { } temperature && (!double.IsFinite(temperature) || temperature < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(invocation), "Temperature must be finite and nonnegative.");
        }
        if (options?.MaxOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(invocation), "MaxOutputTokens must be positive.");
        }

        var payload = new OllamaChatRequest(modelName, messages, false,
            new OllamaOptions(_contextWindowTokens, options?.Temperature, options?.MaxOutputTokens));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };
        var started = Stopwatch.GetTimestamp();
        _logger.LogInformation(LogEvents.ModelInvocationStarted, "Model invocation started: {ModelName}, {MessageCount} messages",
            modelName, messages.Length);
        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            SerializerOptions, cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("Ollama returned a null response.");
        }
        if (result.Error is not null)
        {
            throw new InvalidOperationException("Ollama reported a generation error.");
        }
        if (result.Done is not true)
        {
            throw new InvalidOperationException("Ollama did not return a completed non-streaming response.");
        }
        if (result.Message is not { Role: "assistant", Content: { } content } message)
        {
            throw new InvalidOperationException("Ollama returned an invalid assistant message.");
        }

        RejectUnsupportedOutput(message.ToolCalls, "tool calls");
        RejectUnsupportedOutput(message.Images, "images");
        if (result.DoneReason == "tool_calls")
        {
            throw new NotSupportedException("Ollama requested tools, but tool calling is disabled.");
        }
        var finishReason = result.DoneReason switch
        {
            "stop" => ModelFinishReason.Stop,
            "length" => ModelFinishReason.Length,
            _ => ModelFinishReason.Unknown
        };
        if (finishReason == ModelFinishReason.Stop && string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama reported Stop without nonblank assistant text.");
        }
        if (result.PromptEvalCount is < 0 || result.EvalCount is < 0)
        {
            throw new InvalidOperationException("Ollama returned a negative token count.");
        }

        var usage = result.PromptEvalCount is null && result.EvalCount is null
            ? null
            : new ModelUsage(result.PromptEvalCount, result.EvalCount);
        _logger.LogInformation(
            LogEvents.ModelInvocationReturned,
            "Model invocation returned: {FinishReason}, input tokens {InputTokens}, output tokens {OutputTokens}, {ElapsedMs} ms",
            finishReason, result.PromptEvalCount, result.EvalCount, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new ModelResponse(
            string.IsNullOrWhiteSpace(content) ? null : content, [], finishReason, usage, result.DoneReason);
    }

    private static OllamaMessage ToOllamaMessage(ModelMessage message)
    {
        switch (message)
        {
            case null:
                throw new ArgumentException("Messages must not contain null entries.", nameof(message));
            case ModelSystemMessage system:
                ArgumentException.ThrowIfNullOrWhiteSpace(system.Content);
                return new OllamaMessage("system", system.Content);
            case ModelUserMessage user:
                ArgumentException.ThrowIfNullOrWhiteSpace(user.Content);
                return new OllamaMessage("user", user.Content);
            case ModelAssistantMessage assistant:
                ArgumentNullException.ThrowIfNull(assistant.ToolCalls);
                if (assistant.ToolCalls.Count != 0)
                {
                    throw new NotSupportedException("The text-only Ollama adapter does not support assistant tool calls.");
                }
                ArgumentException.ThrowIfNullOrWhiteSpace(assistant.Text);
                return new OllamaMessage("assistant", assistant.Text);
            default:
                throw new NotSupportedException("The Ollama adapter supports only system, user and text-only assistant messages.");
        }
    }

    private static void RejectUnsupportedOutput(JsonElement? value, string feature)
    {
        if (value is not { } element)
        {
            return;
        }
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Ollama returned an invalid {feature} field.");
        }
        if (element.GetArrayLength() != 0)
        {
            throw new NotSupportedException($"The text-only Ollama adapter cannot return {feature}.");
        }
    }

    private sealed record OllamaChatRequest(
        string Model, IReadOnlyList<OllamaMessage> Messages, bool Stream, OllamaOptions Options);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaOptions(int NumCtx, double? Temperature, int? NumPredict);

    private sealed record OllamaChatResponse
    {
        public OllamaAssistantMessage? Message { get; init; }
        public bool? Done { get; init; }
        public string? DoneReason { get; init; }
        public int? PromptEvalCount { get; init; }
        public int? EvalCount { get; init; }
        public string? Error { get; init; }
    }

    private sealed record OllamaAssistantMessage
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
        public JsonElement? ToolCalls { get; init; }
        public JsonElement? Images { get; init; }
    }
}
