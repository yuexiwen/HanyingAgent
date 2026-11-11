using System.Diagnostics;
using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Responses;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Runtime.Execution;

public sealed class DirectExecutor
{
    private readonly IContextBuilder _contextBuilder;
    private readonly ILanguageModelRuntime _model;
    private readonly ILogger<DirectExecutor> _logger;

    public DirectExecutor(
        IContextBuilder contextBuilder,
        ILanguageModelRuntime model,
        ILogger<DirectExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(logger);
        _contextBuilder = contextBuilder;
        _model = model;
        _logger = logger;
    }

    public async Task<ModelResponse> ExecuteAsync(
        BuildContextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = _logger.BeginScope("ExecutionId:{ExecutionId}", Guid.NewGuid().ToString("N"));
        var started = Stopwatch.GetTimestamp();
        _logger.LogInformation("Execution started");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invocation = await _contextBuilder.BuildAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _model.InvokeAsync(invocation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation(
                "Execution returned with {FinishReason} after {ElapsedMs} ms",
                response.FinishReason, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Execution cancelled after {ElapsedMs} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(exception, "Execution timed out or dependency cancelled after {ElapsedMs} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError(exception, "Execution failed after {ElapsedMs} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
