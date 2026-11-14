using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Host;
using HanyingAgent.Runtime.Execution;
using HanyingAgent.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Tests.Execution;

public sealed class HostExecutionTests
{
    [Fact]
    public async Task Program_PreservesUtf8ConsoleInputInHttpRequestAndChineseOutput()
    {
        const string question = "\u4F60\u597D\uFF0C \u4F60\u80FD\u63D0\u4F9B\u4EC0\u4E48\u529F\u80FD\uFF1F";
        const string answer = "\u4F60\u597D\uFF0C\u6211\u53EF\u4EE5\u56DE\u7B54\u95EE\u9898\u3002 \U0001F680";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(typeof(ApplicationHost).Assembly.Location);
        start.ArgumentList.Add($"--Ollama:BaseAddress=http://127.0.0.1:{port}/");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Console process.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteLineAsync(question);
            await process.StandardInput.WriteLineAsync("/wait");
            await process.StandardInput.WriteLineAsync("/exit");
            process.StandardInput.Close();

            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(timeout.Token);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = answer },
                done = true,
                done_reason = "stop"
            }));
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token);
            context.Response.Close();

            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(question, json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            Assert.Contains($"Assistant> {answer}", await stdout);
            Assert.DoesNotContain(question, await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Theory]
    [InlineData("Ollama:BaseAddress", "ftp://localhost/")]
    [InlineData("Ollama:BaseAddress", "http://localhost/prefix")]
    [InlineData("Ollama:BaseAddress", "http://user:password@localhost/")]
    [InlineData("Ollama:BaseAddress", "http://localhost/?secret=value")]
    [InlineData("Ollama:ModelName", " ")]
    [InlineData("Ollama:ContextWindowTokens", "0")]
    [InlineData("Ollama:TimeoutSeconds", "0")]
    [InlineData("Ollama:TimeoutSeconds", "2147483647")]
    [InlineData("Agent:SystemInstruction", " ")]
    [InlineData("Agent:MaxInputCharacters", "0")]
    [InlineData("Agent:MaxOutputTokens", "0")]
    [InlineData("Agent:Temperature", "-1")]
    [InlineData("Agent:Temperature", "NaN")]
    public async Task Startup_RejectsInvalidConfigurationBeforeReadingInput(string key, string value)
    {
        var builder = ApplicationHost.CreateBuilder([$"--{key}={value}"]);
        builder.Logging.ClearProviders();
        using var host = builder.Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(key, error.Message);
    }

    [Fact]
    public async Task Console_UsesConfiguredRealPipelineAndCarriesCompletedHistoryAcrossQuestions()
    {
        var handler = new RecordingHandler();
        var logs = new RecordingLoggerProvider();
        using var output = new StringWriter();
        using var host = CreateHost(handler, logs, new StringReader("\nfirst-private-input\n/wait\nsecond-private-input\n/wait\n/exit\n"), output,
            "--Ollama:BaseAddress=http://localhost/proxy/",
            "--Ollama:ModelName=configured-model",
            "--Ollama:ContextWindowTokens=2048",
            "--Agent:SystemInstruction=configured-system",
            "--Agent:Temperature=0",
            "--Agent:MaxOutputTokens=32");

        await host.StartAsync();
        await host.Services.GetRequiredService<ConsoleApplication>().RunAsync(CancellationToken.None);
        await host.StopAsync();

        Assert.Equal(2, handler.Requests.Count);
        var requestIndex = 0;
        foreach (var request in handler.Requests)
        {
            Assert.Equal("http://localhost/proxy/api/chat", request.Address);
            using var json = JsonDocument.Parse(request.Body);
            var root = json.RootElement;
            Assert.Equal("configured-model", root.GetProperty("model").GetString());
            var messages = root.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Equal(requestIndex++ == 0 ? 2 : 4, messages.Length);
            Assert.Equal("configured-system", messages[0].GetProperty("content").GetString());
            Assert.Equal("user", messages[1].GetProperty("role").GetString());
            var options = root.GetProperty("options");
            Assert.Equal(2048, options.GetProperty("num_ctx").GetInt32());
            Assert.Equal(32, options.GetProperty("num_predict").GetInt32());
            Assert.Equal(0, options.GetProperty("temperature").GetDouble());
        }
        Assert.Contains("first-private-input", handler.Requests.First().Body);
        using (var second = JsonDocument.Parse(handler.Requests.Last().Body))
        {
            var messages = second.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Equal("first-private-input", messages[1].GetProperty("content").GetString());
            Assert.Equal("private-answer", messages[2].GetProperty("content").GetString());
            Assert.Equal("second-private-input", messages[3].GetProperty("content").GetString());
        }
        Assert.Equal(2, output.ToString().Split("Assistant> private-answer").Length - 1);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain("private-input", entry.Message);
            Assert.DoesNotContain("private-answer", entry.Message);
        });
    }

    [Fact]
    public async Task ConcurrentExecutions_HaveDistinctScopesSharedAcrossContextAndModelLogs()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (index, token) =>
        {
            if (index == 2)
            {
                gate.SetResult();
            }
            await gate.Task.WaitAsync(token);
            return Response();
        });
        var logs = new RecordingLoggerProvider();
        using var host = CreateHost(handler, logs);
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var results = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<DirectExecutor>().ExecuteAsync(Request(), timeout.Token),
            secondScope.ServiceProvider.GetRequiredService<DirectExecutor>().ExecuteAsync(Request(), timeout.Token));

        Assert.All(results, result => Assert.Equal(ExecutionConclusion.Delivered, result.Conclusion));
        var entries = logs.Entries.Where(entry => entry.Category.StartsWith("HanyingAgent.", StringComparison.Ordinal)).ToArray();
        Assert.All(entries, entry => Assert.True(entry.Properties.ContainsKey("ExecutionId")));
        var executions = entries.GroupBy(entry => entry.Properties["ExecutionId"]).ToArray();
        Assert.Equal(2, executions.Length);
        foreach (var execution in executions)
        {
            Assert.Equal(
                new (int, string?)[] { (1001, "ExecutionStarted"), (2001, "ContextBuildStarted"),
                    (2002, "ContextBuildCompleted"), (3001, "ModelInvocationStarted"),
                    (3002, "ModelInvocationReturned"), (1002, "ExecutionReturned") },
                execution.Select(entry => (entry.EventId.Id, entry.EventId.Name)));
            Assert.Contains(execution, entry => entry.Category.EndsWith(".DirectExecutor", StringComparison.Ordinal));
            Assert.Contains(execution, entry => entry.Category.EndsWith(".LocalContextBuilder", StringComparison.Ordinal));
            Assert.Contains(execution, entry => entry.Category.EndsWith(".OllamaLanguageModelRuntime", StringComparison.Ordinal));
            Assert.Contains(execution, entry => entry.Properties.ContainsKey("InputCharacters"));
            Assert.Contains(execution, entry => entry.Properties.ContainsKey("InputTokens"));
        }
    }

    [Fact]
    public async Task Execution_ContextLimitFailureDoesNotInvokeModelAndLogsFailureOnce()
    {
        var handler = new RecordingHandler();
        var logs = new RecordingLoggerProvider();
        using var host = CreateHost(handler, logs, args: ["--Agent:MaxInputCharacters=4"]);
        using var scope = host.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<DirectExecutor>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(Request(), CancellationToken.None));

        Assert.Empty(handler.Requests);
        var failure = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(1003, failure.EventId.Id);
        Assert.Equal("ExecutionFailed", failure.EventId.Name);
        Assert.True(failure.Properties.ContainsKey("ExecutionId"));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.StartsWith("Execution returned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Console_ReportsFailureTruncationAndUnknownWithoutRetryingOrStoppingTheLoop()
    {
        var handler = new RecordingHandler((index, _) => Task.FromResult(index switch
        {
            1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("private-server-body")
            },
            2 => Response("partial", "length"),
            _ => Response("", "future-reason")
        }));
        var logs = new RecordingLoggerProvider();
        using var output = new StringWriter();
        using var host = CreateHost(handler, logs, new StringReader("one\n/wait\ntwo\n/wait\nthree\n/wait\n/exit\n"), output);

        await host.StartAsync();
        await host.Services.GetRequiredService<ConsoleApplication>().RunAsync(CancellationToken.None);
        await host.StopAsync();

        var text = output.ToString();
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("Request failed", text);
        Assert.Contains("Assistant> partial", text);
        Assert.Contains("Output truncated", text);
        Assert.Contains("Completion is unknown", text);
        Assert.DoesNotContain("private-server-body", text);
        Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain("private-server-body", entry.Exception ?? ""));
    }

    [Fact]
    public async Task Console_CancelsActiveRequestWithoutPrintingSuccessfulAnswer()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response();
        });
        var logs = new RecordingLoggerProvider();
        using var output = new StringWriter();
        using var host = CreateHost(handler, logs, new StringReader("one\n/wait\n"), output);
        using var cancellation = new CancellationTokenSource();

        await host.StartAsync();
        var run = host.Services.GetRequiredService<ConsoleApplication>().RunAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        await host.StopAsync();

        Assert.Contains("Cancelled. Exiting.", output.ToString());
        Assert.DoesNotContain("Assistant>", output.ToString());
        var cancelled = Assert.Single(logs.Entries,
            entry => entry.Message.StartsWith("Execution cancelled", StringComparison.Ordinal));
        Assert.Equal(1004, cancelled.EventId.Id);
        Assert.Equal("ExecutionCancelled", cancelled.EventId.Name);
        Assert.Equal(LogLevel.Information, cancelled.Level);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level == LogLevel.Error ||
            entry.Message.StartsWith("Execution returned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Console_TimeoutIsNotUserCancellationAndAllowsAnotherQuestion()
    {
        var handler = new RecordingHandler(async (index, token) =>
        {
            if (index == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return Response();
        });
        var logs = new RecordingLoggerProvider();
        using var output = new StringWriter();
        using var host = CreateHost(handler, logs, new StringReader("one\n/wait\ntwo\n/wait\n/exit\n"), output,
            "--Ollama:TimeoutSeconds=1");

        await host.StartAsync();
        await host.Services.GetRequiredService<ConsoleApplication>().RunAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await host.StopAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("Request timed out", output.ToString());
        Assert.Contains("Assistant> private-answer", output.ToString());
        Assert.DoesNotContain("Cancelled. Exiting.", output.ToString());
        var timeout = Assert.Single(logs.Entries, entry => entry.EventId.Id == 1005);
        Assert.Equal(1005, timeout.EventId.Id);
        Assert.Equal("ExecutionTimedOutOrDependencyCancelled", timeout.EventId.Name);
    }

    [Fact]
    public async Task Console_CanExitWhileWaitingForSynchronousConsoleInput()
    {
        using var input = new BlockingReader();
        using var output = new StringWriter();
        using var host = CreateHost(new RecordingHandler(), new RecordingLoggerProvider(), input, output);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var run = host.Services.GetRequiredService<ConsoleApplication>().RunAsync(cancellation.Token);
            await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("Cancelled. Exiting.", output.ToString());
        }
        finally
        {
            input.Release.Set();
            await input.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Execution_PreCancellationDoesNotInvokeModel()
    {
        var handler = new RecordingHandler();
        using var host = CreateHost(handler, new RecordingLoggerProvider());
        using var scope = host.Services.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<DirectExecutor>().ExecuteAsync(Request(), cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    private static IHost CreateHost(
        RecordingHandler handler,
        RecordingLoggerProvider logs,
        TextReader? input = null,
        TextWriter? output = null,
        params string[] args)
    {
        var builder = ApplicationHost.CreateBuilder(args);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddHttpClient("Ollama").ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.Services.AddSingleton(input ?? new StringReader("/exit\n"));
        builder.Services.AddSingleton(output ?? new StringWriter());
        return builder.Build();
    }

    private static BuildContextRequest Request() => new(
        "system", [new ModelUserMessage("private-input")], []);

    private static HttpResponseMessage Response(string text = "private-answer", string reason = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = text },
            done = true,
            done_reason = reason,
            prompt_eval_count = 12,
            eval_count = 3
        }), Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<int, CancellationToken, Task<HttpResponseMessage>>? respond = null) : HttpMessageHandler
    {
        private int _count;
        public ConcurrentQueue<(string Address, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue((request.RequestUri!.AbsoluteUri,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            var index = Interlocked.Increment(ref _count);
            return respond is null ? Response() : await respond(index, cancellationToken);
        }
    }

    private sealed class BlockingReader : TextReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();

        public override string? ReadLine()
        {
            Entered.SetResult();
            Release.Wait();
            Exited.SetResult();
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
