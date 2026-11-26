using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HanyingAgent.Infrastructure.Mcp;

public sealed class McpConnectionManager : IHostedService, IAsyncDisposable, IDisposable
{
    private readonly Dictionary<string, Connection> _connections = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpConnectionManager> _logger;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _stopping;

    public McpConnectionManager(IOptions<McpSettings> options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpConnectionManager>();
        _shutdownToken = _shutdown.Token;
        ArgumentNullException.ThrowIfNull(options.Value.Servers);
        foreach (var server in options.Value.Servers)
        {
            var snapshot = Snapshot(server);
            if (!_connections.TryAdd(snapshot.Name, new Connection(snapshot)))
            {
                throw new ArgumentException($"Duplicate MCP server name: {snapshot.Name}.", nameof(options));
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask().WaitAsync(cancellationToken);

    // Serialize access per server so discovery, calls, connection creation and disposal cannot race.
    internal async Task<T> ExecuteAsync<T>(
        string serverName, Func<McpClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        if (!_connections.TryGetValue(serverName, out var connection))
        {
            throw new KeyNotFoundException($"MCP server is not registered: {serverName}.");
        }
        if (!connection.Options.Enabled)
        {
            throw new InvalidOperationException($"MCP server is disabled: {serverName}.");
        }

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownToken);
        await connection.Gate.WaitAsync(waiting.Token);
        try
        {
            waiting.Token.ThrowIfCancellationRequested();
            if (connection.Client is { } previous && previous.Completion.IsCompleted)
            {
                connection.Client = null;
                await previous.DisposeAsync();
                _logger.LogWarning("MCP connection closed: {ServerName}; reconnecting for this new request", serverName);
            }
            connection.Client ??= await ConnectAsync(connection.Options, waiting.Token);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(waiting.Token);
            request.CancelAfter(TimeSpan.FromSeconds(connection.Options.RequestTimeoutSeconds));
            return await operation(connection.Client, request.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "MCP access failed for {ServerName}; no automatic tool-call retry", serverName);
            throw;
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    private async Task<McpClient> ConnectAsync(McpServerOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ConnectionTimeoutSeconds));
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var (key, value) in options.Stdio.Environment)
        {
            environment[key] = value;
        }
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = options.Name,
            Command = options.Stdio.Command,
            Arguments = options.Stdio.Arguments.ToArray(),
            WorkingDirectory = options.Stdio.WorkingDirectory,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment
        }, _loggerFactory);
        _logger.LogInformation("Connecting to MCP server {ServerName} via stdio", options.Name);
        var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "HanyingAgent", Version = "1.0" },
            InitializationTimeout = TimeSpan.FromSeconds(options.ConnectionTimeoutSeconds)
        }, _loggerFactory, timeout.Token);
        _logger.LogInformation("MCP server connected: {ServerName}", options.Name);
        return client;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            Volatile.Write(ref _stopping, 1);
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task DisposeCoreAsync()
    {
        List<Exception> errors = [];
        try
        {
            await _shutdown.CancelAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MCP cancellation callbacks failed during shutdown");
            errors.Add(exception);
        }
        foreach (var (name, connection) in _connections)
        {
            await connection.Gate.WaitAsync();
            try
            {
                if (connection.Client is { } client)
                {
                    connection.Client = null;
                    await client.DisposeAsync();
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "MCP connection cleanup failed for {ServerName}", name);
                errors.Add(exception);
            }
            finally
            {
                connection.Gate.Release();
            }
        }
        _shutdown.Dispose();
        if (errors.Count != 0)
        {
            throw new AggregateException("MCP connection cleanup failed.", errors);
        }
    }

    private static McpServerOptions Snapshot(McpServerOptions server)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(server.Name);
        ArgumentNullException.ThrowIfNull(server.Stdio);
        ArgumentException.ThrowIfNullOrWhiteSpace(server.Stdio.Command);
        if (server.ConnectionTimeoutSeconds <= 0 || server.ConnectionTimeoutSeconds > int.MaxValue / 1000 ||
            server.RequestTimeoutSeconds <= 0 || server.RequestTimeoutSeconds > int.MaxValue / 1000)
        {
            throw new ArgumentException($"MCP server {server.Name} must have positive, supported timeouts.");
        }
        if (server.Stdio.WorkingDirectory is { } directory &&
            (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)))
        {
            throw new ArgumentException($"MCP server {server.Name} working directory must be an existing absolute path.");
        }
        ArgumentNullException.ThrowIfNull(server.Stdio.Arguments);
        ArgumentNullException.ThrowIfNull(server.Stdio.Environment);
        if (server.Stdio.Arguments.Any(argument => argument is null) ||
            server.Stdio.Environment.Any(item => string.IsNullOrWhiteSpace(item.Key) ||
                item.Key.Contains('=') || item.Key.Contains('\0') || item.Value is null || item.Value.Contains('\0')))
        {
            throw new ArgumentException($"MCP server {server.Name} has invalid arguments or environment entries.");
        }
        return new McpServerOptions
        {
            Name = server.Name,
            Enabled = server.Enabled,
            ConnectionTimeoutSeconds = server.ConnectionTimeoutSeconds,
            RequestTimeoutSeconds = server.RequestTimeoutSeconds,
            Stdio = new McpStdioOptions
            {
                Command = server.Stdio.Command,
                Arguments = server.Stdio.Arguments.ToList(),
                WorkingDirectory = server.Stdio.WorkingDirectory,
                Environment = new Dictionary<string, string>(server.Stdio.Environment)
            }
        };
    }

    private sealed class Connection(McpServerOptions options)
    {
        public McpServerOptions Options { get; } = options;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public McpClient? Client { get; set; }
    }
}
