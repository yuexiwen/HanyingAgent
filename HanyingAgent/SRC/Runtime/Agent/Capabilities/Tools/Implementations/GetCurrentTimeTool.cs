using System.Globalization;
using System.Text.Json;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;

namespace HanyingAgent.Runtime.Agent.Capabilities.Tools.Implementations;

public sealed class GetCurrentTimeTool : ILocalTool
{
    private readonly TimeProvider _clock;

    public GetCurrentTimeTool(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public ToolDefinition Definition { get; } = new(
        "get_current_time",
        "Returns the current UTC date and time.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public Task<ToolResult> InvokeAsync(InvokeToolRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Call);
        if (!string.Equals(request.Call.ToolName, Definition.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("The request targets a different tool.", nameof(request));
        }
        using var arguments = JsonDocument.Parse(request.Call.ArgumentsJson);
        if (arguments.RootElement.ValueKind != JsonValueKind.Object ||
            arguments.RootElement.EnumerateObject().Any())
        {
            throw new ArgumentException("get_current_time requires an empty JSON object.", nameof(request));
        }

        var content = JsonSerializer.Serialize(new
        {
            utcTime = _clock.GetUtcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        });
        return Task.FromResult(new ToolResult(ToolCallStatus.Succeeded, content));
    }
}
