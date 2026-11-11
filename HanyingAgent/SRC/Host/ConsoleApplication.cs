using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations.Requests;
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
                    var response = await executor.ExecuteAsync(new BuildContextRequest(
                        settings.SystemInstruction,
                        [new ModelUserMessage(line)],
                        [],
                        new ModelOptions(Temperature: settings.Temperature, MaxOutputTokens: settings.MaxOutputTokens)),
                        cancellationToken);
                    await output.WriteLineAsync($"Assistant> {response.Text}");
                    if (response.FinishReason != ModelFinishReason.Stop)
                    {
                        var status = response.FinishReason switch
                        {
                            ModelFinishReason.Length => "Output truncated; this is not a complete answer.",
                            ModelFinishReason.ContentFiltered => "Output was filtered.",
                            ModelFinishReason.Refused => "The model refused the request.",
                            ModelFinishReason.ToolCalls => "Tool execution is not supported.",
                            _ => "Completion is unknown; do not treat this as a complete answer."
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
