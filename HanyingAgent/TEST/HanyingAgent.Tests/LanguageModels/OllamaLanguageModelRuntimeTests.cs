using System.Net;
using System.Text;
using System.Text.Json;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.Tools.Models;
using HanyingAgent.Contracts.Tools.Operations.Requests;
using HanyingAgent.Infrastructure.LanguageModels;

namespace HanyingAgent.Tests.LanguageModels;

public sealed class OllamaLanguageModelRuntimeTests
{
    private const string SuccessResponse =
        """{"message":{"role":"assistant","content":"answer"},"done":true,"done_reason":"stop"}""";

    [Fact]
    public void Constructor_RejectsMissingClient()
    {
        Assert.Throws<ArgumentNullException>(() => new OllamaLanguageModelRuntime(null!, "model"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_RejectsBlankDefaultModel(string? name)
    {
        using var client = Client(new RecordingHandler());
        Assert.ThrowsAny<ArgumentException>(() => new OllamaLanguageModelRuntime(client, name!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsNonPositiveContextWindow(int size)
    {
        using var client = Client(new RecordingHandler());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaLanguageModelRuntime(client, "model", size));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ftp://localhost/")]
    [InlineData("http://localhost/prefix")]
    [InlineData("http://localhost/?key=value")]
    [InlineData("http://localhost/#fragment")]
    public void Constructor_RejectsInvalidBaseAddress(string? address)
    {
        using var client = new HttpClient(new RecordingHandler())
        {
            BaseAddress = address is null ? null : new Uri(address)
        };
        Assert.Throws<ArgumentException>(() => new OllamaLanguageModelRuntime(client, "model"));
    }

    [Fact]
    public async Task InvokeAsync_MapsExactNativeRequestWithoutLeakingInternalMessageShape()
    {
        var handler = new RecordingHandler();
        using var client = Client(handler);
        ILanguageModelRuntime runtime = new OllamaLanguageModelRuntime(client, "default-model");
        var invocation = new ModelInvocation(
        [
            new ModelSystemMessage("system"),
            new ModelUserMessage("previous\n\"quoted\"\\\u4F60"),
            new ModelAssistantMessage("prior answer", []),
            new ModelUserMessage("current")
        ], []);
        var original = JsonSerializer.Serialize(invocation);

        var result = await runtime.InvokeAsync(invocation, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("http://localhost:11434/api/chat"), handler.Uri);
        Assert.Equal("application/json", handler.ContentType);
        using var json = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        var root = json.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("default-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var options = root.GetProperty("options");
        Assert.Equal(4096, options.GetProperty("num_ctx").GetInt32());
        Assert.Single(options.EnumerateObject());
        var messages = root.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(new[] { "system", "user", "assistant", "user" },
            messages.Select(message => message.GetProperty("role").GetString()));
        Assert.Equal(new[] { "system", "previous\n\"quoted\"\\\u4F60", "prior answer", "current" },
            messages.Select(message => message.GetProperty("content").GetString()));
        Assert.All(messages, message => Assert.Equal(2, message.EnumerateObject().Count()));
        Assert.Equal("answer", result.Text);
        Assert.Equal(ModelFinishReason.Stop, result.FinishReason);
        Assert.Equal("stop", result.ProviderFinishReason);
        Assert.Empty(result.ToolCalls);
        Assert.Null(result.Usage);
        Assert.Equal(original, JsonSerializer.Serialize(invocation));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http://localhost:11434/")]
    [InlineData("https://models.example.test/ollama/")]
    public async Task InvokeAsync_MapsOverridesAndPreservesEndpointPrefix(string address)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        var runtime = new OllamaLanguageModelRuntime(client, "default", contextWindowTokens: 2048);

        await runtime.InvokeAsync(Invocation() with
        {
            Options = new ModelOptions("requested", 0, 64)
        }, CancellationToken.None);

        Assert.Equal(new Uri(new Uri(address), "api/chat"), handler.Uri);
        using var json = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        Assert.Equal("requested", json.RootElement.GetProperty("model").GetString());
        var options = json.RootElement.GetProperty("options");
        Assert.Equal(2048, options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(0, options.GetProperty("temperature").GetDouble());
        Assert.Equal(64, options.GetProperty("num_predict").GetInt32());
    }

    public static IEnumerable<object[]> InvalidInvocations()
    {
        yield return [null!];
        yield return [Invocation() with { Messages = null! }];
        yield return [Invocation() with { Tools = null! }];
        yield return [Invocation() with { Tools = [null!] }];
        yield return [Invocation() with { Messages = [] }];
        yield return [Invocation() with { Messages = [null!] }];
        yield return [Invocation() with { Messages = [new ModelSystemMessage(null!)] }];
        yield return [Invocation() with { Messages = [new ModelUserMessage("")] }];
        yield return [Invocation() with { Messages = [new ModelUserMessage(" \r\n")] }];
        yield return [Invocation() with { Messages = [new ModelAssistantMessage(null, [])] }];
        yield return [Invocation() with { Messages = [new ModelAssistantMessage(" ", [])] }];
        yield return [Invocation() with { Messages = [new ModelAssistantMessage("text", null!)] }];
        yield return [Invocation() with { Options = new ModelOptions("") }];
        yield return [Invocation() with { Options = new ModelOptions(" ") }];
        yield return [Invocation() with { Options = new ModelOptions(Temperature: -0.1) }];
        yield return [Invocation() with { Options = new ModelOptions(Temperature: double.NaN) }];
        yield return [Invocation() with { Options = new ModelOptions(Temperature: double.PositiveInfinity) }];
        yield return [Invocation() with { Options = new ModelOptions(Temperature: double.NegativeInfinity) }];
        yield return [Invocation() with { Options = new ModelOptions(MaxOutputTokens: 0) }];
        yield return [Invocation() with { Options = new ModelOptions(MaxOutputTokens: -1) }];
    }

    [Theory]
    [MemberData(nameof(InvalidInvocations))]
    public async Task InvokeAsync_RejectsInvalidInputBeforeSending(ModelInvocation invocation)
    {
        var handler = new RecordingHandler();
        using var client = Client(handler);
        var runtime = new OllamaLanguageModelRuntime(client, "model");

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            runtime.InvokeAsync(invocation, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    public static IEnumerable<object[]> UnsupportedInvocations()
    {
        yield return [Invocation() with { Tools = [new ToolDefinition("read", "read data", "{}")] }];
        yield return [Invocation() with
        {
            Messages = [new ModelAssistantMessage("calling",
                [new ToolCallRequest(new ToolCallId("call-1"), "read", "{}")])]
        }];
        yield return [Invocation() with { Messages = [new ModelToolMessage(null!)] }];
        yield return [Invocation() with { Messages = [new UnknownMessage()] }];
    }

    [Theory]
    [MemberData(nameof(UnsupportedInvocations))]
    public async Task InvokeAsync_RejectsUnsupportedProtocolsBeforeSending(ModelInvocation invocation)
    {
        var handler = new RecordingHandler();
        using var client = Client(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(invocation, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("stop", " complete \n", ModelFinishReason.Stop)]
    [InlineData("length", "partial", ModelFinishReason.Length)]
    [InlineData("length", "", ModelFinishReason.Length)]
    [InlineData("new-provider-reason", "text", ModelFinishReason.Unknown)]
    [InlineData("Stop", "text", ModelFinishReason.Unknown)]
    [InlineData(null, "", ModelFinishReason.Unknown)]
    public async Task InvokeAsync_PreservesFinishReasonAndDoesNotInventCompletion(
        string? reason, string content, ModelFinishReason expected)
    {
        var json = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content },
            done = true,
            done_reason = reason
        });
        var handler = new RecordingHandler((_, _) => Task.FromResult(Reply(json)));
        using var client = Client(handler);

        var result = await new OllamaLanguageModelRuntime(client, "model")
            .InvokeAsync(Invocation(), CancellationToken.None);

        Assert.Equal(expected, result.FinishReason);
        Assert.Equal(reason, result.ProviderFinishReason);
        Assert.Equal(content.Length == 0 ? null : content, result.Text);
        Assert.Empty(result.ToolCalls);
        Assert.Null(result.Usage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(12, 9)]
    [InlineData(null, 9)]
    [InlineData(12, null)]
    public async Task InvokeAsync_PreservesKnownZeroAndUnknownTokenUsage(int? input, int? output)
    {
        var json = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = "answer" },
            done = true,
            done_reason = "stop",
            prompt_eval_count = input,
            eval_count = output
        });
        var handler = new RecordingHandler((_, _) => Task.FromResult(Reply(json)));
        using var client = Client(handler);

        var result = await new OllamaLanguageModelRuntime(client, "model")
            .InvokeAsync(Invocation(), CancellationToken.None);

        Assert.Equal(input is null && output is null ? null : new ModelUsage(input, output), result.Usage);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"done":true,"done_reason":"stop"}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text"},"done_reason":"stop"}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text"},"done":false}""")]
    [InlineData("""{"message":{"role":"user","content":"text"},"done":true}""")]
    [InlineData("""{"message":{"content":"text"},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant"},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","content":null},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","content":" "},"done":true,"done_reason":"stop"}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text","tool_calls":{}},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text","images":"unexpected"},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text"},"done":true,"prompt_eval_count":-1}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text"},"done":true,"eval_count":-1}""")]
    [InlineData("""{"error":"generation failed"}""")]
    public async Task InvokeAsync_RejectsInvalidSuccessPayloads(string json)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Reply(json)));
        using var client = Client(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), CancellationToken.None));

        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"done":"true"}""")]
    [InlineData("""{"done":true,"message":"text"}""")]
    [InlineData("""{"done":true,"eval_count":2147483648}""")]
    [InlineData("""{"done":true,"eval_count":1.5}""")]
    [InlineData("""{"done":true,"done_reason":1}""")]
    [InlineData("""{"done":false}{"done":true}""")]
    public async Task InvokeAsync_PropagatesMalformedJsonInsteadOfReturningUnknown(string json)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Reply(json)));
        using var client = Client(handler);

        await Assert.ThrowsAsync<JsonException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"message":{"role":"assistant","content":"text","tool_calls":[{"function":{"name":"read","arguments":{}}}]},"done":true,"done_reason":"stop"}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text","tool_calls":[]},"done":true,"done_reason":"tool_calls"}""")]
    [InlineData("""{"message":{"role":"assistant","content":"text","images":["image-data"]},"done":true}""")]
    public async Task InvokeAsync_RejectsUnrequestedToolsAndMultimodalOutput(string json)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Reply(json)));
        using var client = Client(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_AcceptsEmptyOptionalArraysAndDoesNotExposeThinkingAsAnswer()
    {
        const string json =
            """{"message":{"role":"assistant","content":"answer","tool_calls":[],"images":[],"thinking":"internal trace"},"done":true,"done_reason":"stop"}""";
        var handler = new RecordingHandler((_, _) => Task.FromResult(Reply(json)));
        using var client = Client(handler);

        var result = await new OllamaLanguageModelRuntime(client, "model")
            .InvokeAsync(Invocation(), CancellationToken.None);

        Assert.Equal("answer", result.Text);
        Assert.Empty(result.ToolCalls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    public async Task InvokeAsync_PropagatesHttpStatusWithoutRetryOrSensitiveBody(int status)
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Reply("""{"error":"sensitive provider body"}""", (HttpStatusCode)status)));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), CancellationToken.None));

        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain("sensitive provider body", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesTransportFailureWithoutRetry()
    {
        var expected = new HttpRequestException("network unavailable");
        var handler = new RecordingHandler((_, _) => throw expected);
        using var client = Client(handler);

        var actual = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvokeAsync_PreCancelledDoesNotSend()
    {
        var handler = new RecordingHandler();
        using var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InvokeAsync_CancellationInterruptsPendingRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Reply();
        });
        using var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        var task = new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvokeAsync_HttpClientTimeoutCoversResponseBodyNotJustHeaders()
    {
        var content = new BlockingContent();
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var client = Client(handler);
        client.Timeout = TimeSpan.FromMilliseconds(200);
        var task = new OllamaLanguageModelRuntime(client, "model").InvokeAsync(Invocation(), CancellationToken.None);

        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(content.Disposed);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_DisposesResponseOnSuccessAndParsingFailureButNotInjectedClient(bool valid)
    {
        var contents = new List<TrackingContent>();
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new TrackingContent(valid ? SuccessResponse : "invalid json");
            contents.Add(content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = Client(handler);
        var runtime = new OllamaLanguageModelRuntime(client, "model");

        if (valid)
        {
            await runtime.InvokeAsync(Invocation(), CancellationToken.None);
        }
        else
        {
            await Assert.ThrowsAsync<JsonException>(() => runtime.InvokeAsync(Invocation(), CancellationToken.None));
        }
        Assert.True(Assert.Single(contents).Disposed);

        using var response = await client.GetAsync("api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    private static ModelInvocation Invocation() => new([new ModelUserMessage("hello")], []);

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:11434/") };

    private static HttpResponseMessage Reply(string json = SuccessResponse, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record UnknownMessage : ModelMessage;

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is { } content
                ? await content.ReadAsStringAsync(cancellationToken)
                : null;
            return send is null ? Reply() : await send(request, cancellationToken);
        }
    }

    private sealed class TrackingContent(string text) : StringContent(text, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
