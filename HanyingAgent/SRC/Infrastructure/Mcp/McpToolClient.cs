using ModelContextProtocol.Protocol;

namespace HanyingAgent.Infrastructure.Mcp;

public sealed class McpToolClient
{
    private readonly McpConnectionManager _connections;

    public McpToolClient(McpConnectionManager connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _connections = connections;
    }

    public Task<IReadOnlyList<Tool>> ListToolsAsync(string serverName, CancellationToken cancellationToken) =>
        _connections.ExecuteAsync<IReadOnlyList<Tool>>(serverName, async (client, token) =>
        {
            if (client.ServerCapabilities.Tools is null)
            {
                throw new NotSupportedException($"MCP server {serverName} does not advertise tools.");
            }
            List<Tool> tools = [];
            HashSet<string> cursors = new(StringComparer.Ordinal);
            string? cursor = null;
            do
            {
                var page = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, token);
                tools.AddRange(page.Tools);
                cursor = page.NextCursor;
                if (cursor is not null && !cursors.Add(cursor))
                {
                    throw new InvalidOperationException($"MCP server {serverName} returned a repeated tools cursor.");
                }
            } while (cursor is not null);
            return tools.ToArray();
        }, cancellationToken);

    public Task<CallToolResult> CallToolAsync(
        string serverName, string toolName, IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var snapshot = arguments is null ? null : new Dictionary<string, object?>(arguments);
        return _connections.ExecuteAsync(serverName, async (client, token) =>
        {
            if (client.ServerCapabilities.Tools is null)
            {
                throw new NotSupportedException($"MCP server {serverName} does not advertise tools.");
            }
            return await client.CallToolAsync(toolName, snapshot, cancellationToken: token);
        }, cancellationToken);
    }
}
