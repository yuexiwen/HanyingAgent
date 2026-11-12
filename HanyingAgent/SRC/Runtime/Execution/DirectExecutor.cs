using System.Diagnostics;
using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Responses;
using HanyingAgent.Diagnostics;
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

    public async Task<ExecutionOutcome> ExecuteAsync(
        BuildContextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = _logger.BeginScope("ExecutionId:{ExecutionId}", Guid.NewGuid().ToString("N"));
        var started = Stopwatch.GetTimestamp();
        _logger.LogInformation(LogEvents.ExecutionStarted, "Execution started");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invocation = await _contextBuilder.BuildAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _model.InvokeAsync(invocation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = InterpretResponse(response);
            _logger.Log(
                outcome.Conclusion == ExecutionConclusion.Delivered ? LogLevel.Information : LogLevel.Warning,
                LogEvents.ExecutionReturned,
                "Execution returned with {Conclusion}, model finish reason {FinishReason} after {ElapsedMs} ms",
                outcome.Conclusion, outcome.FinishReason, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(LogEvents.ExecutionCancelled, "Execution cancelled after {ElapsedMs} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(LogEvents.ExecutionTimedOutOrDependencyCancelled, exception,
                "Execution timed out or dependency cancelled after {ElapsedMs} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError(LogEvents.ExecutionFailed, exception, "Execution failed after {ElapsedMs} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    private static ExecutionOutcome InterpretResponse(ModelResponse response)
    {
        if (response is null || response.ToolCalls is null || !Enum.IsDefined(response.FinishReason))
        {
            throw new InvalidOperationException("The model returned an invalid response.");
        }

        var conclusion = response.ToolCalls.Count != 0 || response.FinishReason == ModelFinishReason.ToolCalls
            ? ExecutionConclusion.UnsupportedAction
            : response.FinishReason switch
            {
                ModelFinishReason.Stop when !string.IsNullOrWhiteSpace(response.Text) => ExecutionConclusion.Delivered,
                ModelFinishReason.Stop => throw new InvalidOperationException("The model reported Stop without nonblank text."),
                ModelFinishReason.Length => ExecutionConclusion.Incomplete,
                ModelFinishReason.Unknown => ExecutionConclusion.Unconfirmed,
                ModelFinishReason.Refused => ExecutionConclusion.Refused,
                ModelFinishReason.ContentFiltered => ExecutionConclusion.Filtered,
                _ => throw new InvalidOperationException("The model returned an unsupported finish reason.")
            };

        return new ExecutionOutcome(
            conclusion, response.Text, response.FinishReason, response.Usage, response.ProviderFinishReason);
    }
}
