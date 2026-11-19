using HanyingAgent.Contracts.Context.Operations;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Responses;
using HanyingAgent.Contracts.Tools.Models;
using HanyingAgent.Contracts.Tools.Operations.Requests;
using HanyingAgent.Host;
using HanyingAgent.Host.Configuration;
using HanyingAgent.Runtime.Execution;
using HanyingAgent.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Tests.Execution;

public sealed class DirectExecutorTests
{
    [Theory]
    [InlineData(ModelFinishReason.Stop, " answer \n", ExecutionConclusion.Delivered)]
    [InlineData(ModelFinishReason.Length, "partial", ExecutionConclusion.Incomplete)]
    [InlineData(ModelFinishReason.Length, null, ExecutionConclusion.Incomplete)]
    [InlineData(ModelFinishReason.Unknown, "partial", ExecutionConclusion.Unconfirmed)]
    [InlineData(ModelFinishReason.Unknown, null, ExecutionConclusion.Unconfirmed)]
    [InlineData(ModelFinishReason.Refused, "I cannot help.", ExecutionConclusion.Refused)]
    [InlineData(ModelFinishReason.Refused, null, ExecutionConclusion.Refused)]
    [InlineData(ModelFinishReason.ContentFiltered, null, ExecutionConclusion.Filtered)]
    [InlineData(ModelFinishReason.ToolCalls, null, ExecutionConclusion.UnsupportedAction)]
    public async Task ExecuteAsync_InterpretsResponseAndPreservesMetadata(
        ModelFinishReason reason, string? text, ExecutionConclusion expected)
    {
        var response = new ModelResponse(text, [], reason, new ModelUsage(12, 3), "provider-reason");
        var model = new StubModel((_, _) => Task.FromResult(response));
        using var logs = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var executor = new DirectExecutor(model, factory.CreateLogger<DirectExecutor>());
        var request = Request();
        using var cancellation = new CancellationTokenSource();

        var outcome = await executor.ExecuteAsync(request, cancellation.Token);

        Assert.Equal(expected, outcome.Conclusion);
        Assert.Equal(text, outcome.Text);
        Assert.Equal(reason, outcome.FinishReason);
        Assert.Same(response.Usage, outcome.Usage);
        Assert.Equal(response.ProviderFinishReason, outcome.ProviderFinishReason);
        Assert.Same(request, model.Invocation);
        Assert.Equal(cancellation.Token, model.Token);
        Assert.Equal(1, model.Calls);
        var returned = Assert.Single(logs.Entries,
            entry => entry.Message.StartsWith("Execution returned", StringComparison.Ordinal));
        Assert.Equal(1002, returned.EventId.Id);
        Assert.Equal("ExecutionReturned", returned.EventId.Name);
        Assert.Equal(expected, returned.Properties["Conclusion"]);
        Assert.Equal(expected == ExecutionConclusion.Delivered ? LogLevel.Information : LogLevel.Warning, returned.Level);
    }

    [Theory]
    [InlineData(ModelFinishReason.Stop)]
    [InlineData(ModelFinishReason.Length)]
    [InlineData(ModelFinishReason.Unknown)]
    public async Task ExecuteAsync_ToolPayloadTakesPrecedenceOverFinishReason(ModelFinishReason reason)
    {
        var model = new StubModel((_, _) => Task.FromResult(new ModelResponse(
            "not a completed action",
            [new ToolCallRequest(new ToolCallId("call-1"), "weather", "{}")],
            reason)));
        var executor = CreateExecutor(model);

        var outcome = await executor.ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(ExecutionConclusion.UnsupportedAction, outcome.Conclusion);
        Assert.Equal("not a completed action", outcome.Text);
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData("null-response")]
    [InlineData("null-calls")]
    [InlineData("empty-stop")]
    [InlineData("blank-stop")]
    [InlineData("unknown-enum")]
    public async Task ExecuteAsync_RejectsMalformedResponsesAndLogsFailureOnce(string kind)
    {
        var response = kind switch
        {
            "null-response" => null!,
            "null-calls" => new ModelResponse("text", null!, ModelFinishReason.Stop),
            "empty-stop" => new ModelResponse(null, [], ModelFinishReason.Stop),
            "blank-stop" => new ModelResponse(" \r\n", [], ModelFinishReason.Stop),
            _ => new ModelResponse("text", [], (ModelFinishReason)999)
        };
        var model = new StubModel((_, _) => Task.FromResult(response));
        using var logs = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var executor = new DirectExecutor(model, factory.CreateLogger<DirectExecutor>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(Request(), CancellationToken.None));

        var failure = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(1003, failure.EventId.Id);
        Assert.Equal("ExecutionFailed", failure.EventId.Name);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.StartsWith("Execution returned", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("timeout")]
    public async Task ExecuteAsync_PropagatesOriginalDependencyFailureWithoutRetry(string stage)
    {
        Exception failure = stage == "timeout"
            ? new OperationCanceledException("dependency timeout")
            : new HttpRequestException("dependency unavailable");
        var model = new StubModel((_, _) => Task.FromException<ModelResponse>(failure));
        var executor = CreateExecutor(model);

        var actual = await Assert.ThrowsAnyAsync<Exception>(() =>
            executor.ExecuteAsync(Request(), CancellationToken.None));

        Assert.Same(failure, actual);
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_CancellationBeforeInvocationOrAfterModelReturnsStopsExecution(bool beforeInvocation)
    {
        using var cancellation = new CancellationTokenSource();
        if (beforeInvocation)
        {
            cancellation.Cancel();
        }
        var model = new StubModel((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new ModelResponse("answer", [], ModelFinishReason.Stop));
        });
        var executor = CreateExecutor(model);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(Request(), cancellation.Token));

        Assert.Equal(beforeInvocation ? 0 : 1, model.Calls);
    }

    [Theory]
    [InlineData(ModelFinishReason.Refused, null, "The model refused the request.")]
    [InlineData(ModelFinishReason.ContentFiltered, null, "Output was filtered.")]
    [InlineData(ModelFinishReason.ToolCalls, "explanation", "Tool execution is not supported.")]
    public async Task Console_DisplaysExecutionConclusionWithoutEmptyAssistantAnswer(
        ModelFinishReason reason, string? text, string expectedMessage)
    {
        var model = new StubModel((_, _) => Task.FromResult(new ModelResponse(text, [], reason)));
        using var output = new StringWriter();
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Services.AddSingleton<ILanguageModelRuntime>(model);
        builder.Services.AddSingleton<IContextBuilder>(new StubContext());
        builder.Services.AddSingleton<TextReader>(new StringReader("question\n/wait\n/exit\n"));
        builder.Services.AddSingleton<TextWriter>(output);
        builder.Logging.ClearProviders();
        using var host = builder.Build();
        var console = host.Services.GetRequiredService<ConsoleApplication>();

        await host.StartAsync();
        await console.RunAsync(CancellationToken.None);
        await host.StopAsync();

        Assert.Contains(expectedMessage, output.ToString());
        if (text is null)
        {
            Assert.DoesNotContain("Assistant>", output.ToString());
        }
        else
        {
            Assert.Contains($"Assistant> {text}", output.ToString());
        }
    }

    private static DirectExecutor CreateExecutor(ILanguageModelRuntime model) =>
        new(model, Microsoft.Extensions.Logging.Abstractions.NullLogger<DirectExecutor>.Instance);

    private static ModelInvocation Request() =>
        new([new ModelSystemMessage("system"), new ModelUserMessage("input")], []);

    private sealed class StubContext : IContextBuilder
    {
        public ModelInvocation Invocation { get; } = new([new ModelUserMessage("assembled")], []);

        public Task<ModelInvocation> BuildAsync(BuildContextRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Invocation);
    }

    private sealed class StubModel(
        Func<ModelInvocation, CancellationToken, Task<ModelResponse>> invoke) : ILanguageModelRuntime
    {
        public int Calls { get; private set; }
        public ModelInvocation? Invocation { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<ModelResponse> InvokeAsync(ModelInvocation invocation, CancellationToken cancellationToken)
        {
            Calls++;
            Invocation = invocation;
            Token = cancellationToken;
            return invoke(invocation, cancellationToken);
        }
    }
}
