using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Host.Configuration;
using HanyingAgent.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Host;

public sealed class ConsoleApplication(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentSettings> options,
    TextReader input,
    TextWriter output)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await output.WriteLineAsync("HanyingAgent - independent single-turn questions. /exit to quit; Ctrl+C to cancel and exit.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await output.WriteAsync("You> ");
                await output.FlushAsync();
                // Console's synchronous reader cannot interrupt a pending read; shutdown stops waiting for it.
                var line = await Task.Run(() => input.ReadLine(), CancellationToken.None)
                    .WaitAsync(cancellationToken);
                if (line is null || line.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var scope = scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<DirectExecutor>();
                try
                {
                    var outcome = await executor.ExecuteAsync(new BuildContextRequest(
                        settings.SystemInstruction,
                        [new ModelUserMessage(line)],
                        [],
                        new ModelOptions(Temperature: settings.Temperature, MaxOutputTokens: settings.MaxOutputTokens)),
                        cancellationToken);
                    if (!string.IsNullOrWhiteSpace(outcome.Text))
                    {
                        await output.WriteLineAsync($"Assistant> {outcome.Text}");
                    }
                    if (outcome.Conclusion != ExecutionConclusion.Delivered)
                    {
                        var status = outcome.Conclusion switch
                        {
                            ExecutionConclusion.Incomplete => "Output truncated; this is not a complete answer.",
                            ExecutionConclusion.Filtered => "Output was filtered.",
                            ExecutionConclusion.Refused => "The model refused the request.",
                            ExecutionConclusion.UnsupportedAction => "Tool execution is not supported.",
                            ExecutionConclusion.Unconfirmed => "Completion is unknown; do not treat this as a complete answer.",
                            _ => throw new InvalidOperationException("Unsupported execution conclusion.")
                        };
                        await output.WriteLineAsync($"[{status}]");
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await output.WriteLineAsync("[Request timed out or dependency cancelled. See logs; you can try another question.]");
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or
                    ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    await output.WriteLineAsync("[Request failed. See logs; you can try another question.]");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("[Cancelled. Exiting.]");
        }
    }
}
