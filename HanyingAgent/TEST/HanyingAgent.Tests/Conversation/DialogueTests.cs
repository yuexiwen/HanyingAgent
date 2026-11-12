using System.Text.Json;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Responses;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Contracts.Users.Operations;
using HanyingAgent.Contracts.Users.Operations.Requests;
using HanyingAgent.Host;
using HanyingAgent.Host.Configuration;
using HanyingAgent.Runtime.Context;
using HanyingAgent.Runtime.Conversation;
using HanyingAgent.Runtime.Sessions;
using HanyingAgent.Runtime.Users;
using HanyingAgent.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Tests.Conversation;

public sealed class DialogueTests
{
    [Fact]
    public async Task Dialogue_IncludesPreviousTurnOnceAndIsolatesOtherSessions()
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model);
        var first = await fixture.NewSession("A");
        var second = await fixture.NewSession("B");

        await fixture.Respond(first.Id, "my name is Yue");
        await fixture.Respond(first.Id, "what is my name?");
        await fixture.Respond(second.Id, "what is my name?");

        Assert.Equal(new[] { "system", "my name is Yue", "answer", "what is my name?" },
            Texts(model.Invocations[1]));
        Assert.Equal(new[] { "system", "what is my name?" }, Texts(model.Invocations[2]));
        var history = await fixture.Read(first.Id);
        Assert.Equal(4, history.Items.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, history.Items.Select(message => message.Sequence));
        Assert.All(history.Items, message => Assert.Null(message.TaskId));
        Assert.All(fixture.Logs.Entries.Where(entry => entry.Properties.ContainsKey("ExecutionId")),
            entry => Assert.True(entry.Properties.ContainsKey("SessionId")));
    }

    [Fact]
    public async Task Window_RetainsExactlyTenMostRecentCompletedTurnsAndDoesNotDeleteOlderHistory()
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model);
        var session = await fixture.NewSession();
        for (var index = 0; index < 12; index++)
        {
            await fixture.Append(session.Id, new UserConversationContent($"user-{index}"));
            await fixture.Append(session.Id, Assistant($"answer-{index}"));
        }

        await fixture.Respond(session.Id, "current");

        var messages = Texts(Assert.Single(model.Invocations));
        Assert.Equal(22, messages.Length);
        Assert.Equal("user-2", messages[1]);
        Assert.Equal("answer-11", messages[^2]);
        Assert.Equal("current", messages[^1]);
        var newest = await fixture.Read(session.Id);
        Assert.NotNull(newest.ContinuationToken);
        var older = await fixture.Conversations.ListBySessionAsync(
            new ListConversationMessagesRequest(session.Id, ContinuationToken: newest.ContinuationToken), CancellationToken.None);
        Assert.Contains(older.Items, message => message.Content == new UserConversationContent("user-0"));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public async Task Window_UsesExactCombinedCharacterBoundaryAndDropsWholePairs(int limit, bool includesPair)
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model, $"--Agent:MaxInputCharacters={limit}");
        var session = await fixture.NewSession();
        await fixture.Append(session.Id, new UserConversationContent("u"));
        await fixture.Append(session.Id, Assistant("a"));

        await fixture.Respond(session.Id, "x");

        // Fixture's system instruction is "system" (six characters), plus current "x".
        Assert.Equal(includesPair ? 4 : 2, Assert.Single(model.Invocations).Messages.Count);
    }

    [Fact]
    public async Task OversizedCurrentInput_IsRecordedButNeverSentOrTruncated()
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model, "--Agent:MaxInputCharacters=7");
        var session = await fixture.NewSession();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Respond(session.Id, "too long"));

        Assert.Empty(model.Invocations);
        Assert.IsType<UserConversationContent>(Assert.Single((await fixture.Read(session.Id)).Items).Content);
        await fixture.Respond(session.Id, "x");
        Assert.Equal(new[] { "system", "x" }, Texts(Assert.Single(model.Invocations)));
    }

    [Theory]
    [InlineData(ModelFinishReason.Length)]
    [InlineData(ModelFinishReason.Unknown)]
    [InlineData(ModelFinishReason.Refused)]
    [InlineData(ModelFinishReason.ContentFiltered)]
    public async Task NonCompletedResponses_AreStoredWithReasonButOmittedFromFutureContext(ModelFinishReason reason)
    {
        var model = new ModelStub((index, _) => Task.FromResult(
            new ModelResponse(index == 1 ? "partial" : "answer", [], index == 1 ? reason : ModelFinishReason.Stop)));
        using var fixture = new Fixture(model);
        var session = await fixture.NewSession();

        await fixture.Respond(session.Id, "first");
        await fixture.Respond(session.Id, "second");

        Assert.Equal(new[] { "system", "second" }, Texts(model.Invocations[1]));
        var history = (await fixture.Read(session.Id)).Items;
        var assistant = Assert.IsType<AssistantConversationContent>(history[1].Content);
        Assert.Equal(reason, assistant.FinishReason);
        Assert.Equal("partial", assistant.Text);
    }

    [Fact]
    public async Task FailureAndCancellation_LeaveUserOnlyAndReleaseTheTurnLease()
    {
        using var cancellation = new CancellationTokenSource();
        var model = new ModelStub((index, _) =>
        {
            if (index == 1)
            {
                return Task.FromException<ModelResponse>(new HttpRequestException("offline"));
            }
            if (index == 2)
            {
                cancellation.Cancel();
            }
            return Task.FromResult(new ModelResponse("answer", [], ModelFinishReason.Stop));
        });
        using var fixture = new Fixture(model);
        var session = await fixture.NewSession();

        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Respond(session.Id, "failed"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Respond(session.Id, "cancelled", cancellation.Token));
        await fixture.Respond(session.Id, "recovered");

        Assert.Equal(new[] { "system", "recovered" }, Texts(model.Invocations[2]));
        var records = (await fixture.Read(session.Id)).Items;
        Assert.Equal(4, records.Count);
        Assert.Single(records, message => message.Content is AssistantConversationContent);
        Assert.Equal(SessionStatus.Closed, (await fixture.Sessions.CloseAsync(session.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ActiveTurn_RejectsConcurrentTurnAndCloseWithoutFakingActiveTaskStatus()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ModelStub(async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return new ModelResponse("answer", [], ModelFinishReason.Stop);
        });
        using var fixture = new Fixture(model);
        var session = await fixture.NewSession();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = fixture.Respond(session.Id, "one", timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Respond(session.Id, "two"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sessions.CloseAsync(session.Id, CancellationToken.None));
            var snapshot = await fixture.Sessions.GetAsync(session.Id, CancellationToken.None);
            Assert.Equal(SessionStatus.Idle, snapshot!.Status);
            Assert.Null(snapshot.CurrentTaskId);
        }
        finally
        {
            release.TrySetResult();
            await running;
        }
        await fixture.Sessions.CloseAsync(session.Id, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Respond(session.Id, "closed"));
        Assert.Single(model.Invocations);
    }

    [Fact]
    public async Task MessageRetries_AreIdempotentAcrossEquivalentSnapshotsAndConflictOnChangedPayload()
    {
        using var fixture = new Fixture(new ModelStub());
        var session = await fixture.NewSession();
        var request = new AppendConversationMessageRequest(
            new MessageId("stable"), session.Id, null, Assistant("answer"));

        var first = await fixture.Conversations.AppendAsync(request, CancellationToken.None);
        var equivalent = request with { Content = Assistant("answer") };
        var retry = await fixture.Conversations.AppendAsync(equivalent, CancellationToken.None);
        Assert.Equal(first, retry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Conversations.AppendAsync(
            request with { Content = Assistant("changed") }, CancellationToken.None));
        Assert.Single((await fixture.Read(session.Id)).Items);
        await fixture.Sessions.CloseAsync(session.Id, CancellationToken.None);
        Assert.Equal(first, await fixture.Conversations.AppendAsync(equivalent, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Append(session.Id, new UserConversationContent("new")));
        Assert.Equal(first, await fixture.Conversations.GetAsync(first.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentAppendAndPagination_HaveUniqueSequencesStablePagesAndSessionBoundCursors()
    {
        using var fixture = new Fixture(new ModelStub());
        var session = await fixture.NewSession();
        await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(() =>
            fixture.Append(session.Id, new UserConversationContent($"message-{index}")))));
        var page = await fixture.Read(session.Id);
        Assert.Equal(Enumerable.Range(21, 20).Select(value => (long)value), page.Items.Select(message => message.Sequence));
        await fixture.Append(session.Id, new UserConversationContent("later"));
        var older = await fixture.Conversations.ListBySessionAsync(new ListConversationMessagesRequest(
            session.Id, ContinuationToken: page.ContinuationToken), CancellationToken.None);
        Assert.Equal(Enumerable.Range(1, 20).Select(value => (long)value), older.Items.Select(message => message.Sequence));
        Assert.Null(older.ContinuationToken);
        var other = await fixture.NewSession("Other");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Conversations.ListBySessionAsync(
            new ListConversationMessagesRequest(other.Id, ContinuationToken: page.ContinuationToken), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Conversations.ListBySessionAsync(
            new ListConversationMessagesRequest(session.Id, ContinuationToken: "invalid!"), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentRetries_AllocateOneSequenceAndSnapshotsDoNotExposeMutableToolLists()
    {
        using var fixture = new Fixture(new ModelStub());
        var session = await fixture.NewSession();
        var calls = new List<HanyingAgent.Contracts.Tools.Operations.Requests.ToolCallRequest>();
        var request = new AppendConversationMessageRequest(new MessageId("retry"), session.Id, null,
            new AssistantConversationContent("answer", calls, ModelFinishReason.Stop));

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            fixture.Conversations.AppendAsync(request, CancellationToken.None))));
        calls.Add(new HanyingAgent.Contracts.Tools.Operations.Requests.ToolCallRequest(
            new HanyingAgent.Contracts.Tools.Models.ToolCallId("tool"), "unexpected", "{}"));

        Assert.All(results, result => Assert.Equal(1, result.Sequence));
        var history = await fixture.Read(session.Id);
        Assert.Empty(Assert.IsType<AssistantConversationContent>(Assert.Single(history.Items).Content).ToolCalls);
        var next = await fixture.Append(session.Id, new UserConversationContent("next"));
        Assert.Equal(2, next.Sequence);
        Assert.Single(history.Items);
    }

    [Fact]
    public async Task PreCancellationAndInvalidRequests_DoNotMutateHistory()
    {
        using var fixture = new Fixture(new ModelStub());
        var session = await fixture.NewSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Conversations.AppendAsync(
            new AppendConversationMessageRequest(new MessageId("cancelled"), session.Id, null,
                new UserConversationContent("input")), cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Append(session.Id, new UserConversationContent(" ")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Conversations.ListBySessionAsync(
            new ListConversationMessagesRequest(session.Id, 0), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Conversations.ListBySessionAsync(
            new ListConversationMessagesRequest(session.Id, 201), CancellationToken.None));
        Assert.Empty((await fixture.Read(session.Id)).Items);
    }

    [Fact]
    public async Task UserProfiles_KeepStableIdentityAndSessionOwnershipWhenNameChanges()
    {
        using var fixture = new Fixture(new ModelStub());
        var session = await fixture.NewSession();
        var users = fixture.Host.Services.GetRequiredService<IUserManager>();
        var user = await users.GetAsync(session.UserId, CancellationToken.None);
        Assert.NotNull(user);
        var updated = await users.UpdateAsync(new UpdateUserRequest(session.UserId, "Renamed"), CancellationToken.None);

        Assert.Equal(user.Id, updated.Id);
        Assert.Equal(user.CreatedAt, updated.CreatedAt);
        Assert.Equal("Renamed", updated.DisplayName);
        Assert.Equal(session.UserId, (await fixture.Sessions.GetAsync(session.Id, CancellationToken.None))!.UserId);
        Assert.Single(await users.ListAsync(CancellationToken.None));
        Assert.Null(await users.GetAsync(new HanyingAgent.Contracts.Users.Models.UserId("unknown"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => users.CreateAsync(new CreateUserRequest(" "), CancellationToken.None));
    }

    [Fact]
    public async Task LocalOwner_IsStableAndForeignUserIdsDoNotGrantAccess()
    {
        using var fixture = new Fixture(new ModelStub());
        var local = await fixture.Identity.GetUserIdAsync(CancellationToken.None);
        var userIds = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => fixture.Identity.GetUserIdAsync(CancellationToken.None)));
        Assert.All(userIds, id => Assert.Equal(local, id));
        var users = fixture.Host.Services.GetRequiredService<IUserManager>();
        var foreign = await users.CreateAsync(new CreateUserRequest("Other"), CancellationToken.None);
        var store = fixture.Host.Services.GetRequiredService<ISessionStore>();
        var foreignSession = store.Add(foreign.Id, "Foreign", DateTimeOffset.UtcNow, CancellationToken.None);
        var historyStore = fixture.Host.Services.GetRequiredService<IConversationStore>();
        var foreignMessage = historyStore.Append(new AppendConversationMessageRequest(
            new MessageId("foreign"), foreignSession.Id, null, new UserConversationContent("private")),
            foreign.Id, DateTimeOffset.UtcNow, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Sessions.CreateAsync(new CreateSessionRequest(foreign.Id, "forbidden"), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Sessions.GetAsync(foreignSession.Id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Sessions.CloseAsync(foreignSession.Id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Read(foreignSession.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Conversations.GetAsync(foreignMessage.Id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Append(foreignSession.Id, new UserConversationContent("forbidden")));
        Assert.Empty(await fixture.Sessions.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Window_SkipsDanglingInputsAndInvalidRepliesAndBoundsHistoryScanning()
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model);
        var session = await fixture.NewSession();
        await fixture.Append(session.Id, new UserConversationContent("old"));
        await fixture.Append(session.Id, Assistant("old answer"));
        for (var index = 0; index < 205; index++)
        {
            await fixture.Append(session.Id, new UserConversationContent("unanswered"));
        }

        await fixture.Respond(session.Id, "current");

        Assert.Equal(new[] { "system", "current" }, Texts(Assert.Single(model.Invocations)));
        var selected = Assert.Single(fixture.Logs.Entries, entry => entry.EventId.Id == 2003);
        Assert.Equal("ScanLimit", selected.Properties["SelectionReason"]);
        Assert.Equal(200, selected.Properties["ScannedMessages"]);
    }

    [Fact]
    public async Task SavingAssistantFailure_IsExplicitAndDoesNotRetryModelOrLeaveSessionBusy()
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model);
        var session = await fixture.NewSession();
        var wrapper = new FailingAssistantStore(fixture.Conversations);
        using var scope = fixture.Host.Services.CreateScope();
        var coordinator = ActivatorUtilities.CreateInstance<DialogueCoordinator>(scope.ServiceProvider,
            wrapper, new ConversationWindow(wrapper, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConversationWindow>.Instance));

        await Assert.ThrowsAsync<IOException>(() => coordinator.RespondAsync(
            fixture.Request(session.Id, "question"), CancellationToken.None));

        Assert.Single(model.Invocations);
        Assert.Single((await fixture.Read(session.Id)).Items);
        var failure = Assert.Single(fixture.Logs.Entries, entry => entry.EventId.Id == 4003);
        Assert.Equal("SaveAssistant", failure.Properties["Phase"]);
        await fixture.Sessions.CloseAsync(session.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Console_CommandsSwitchSessionsShowHistoryAndNeverInvokeModelForCommands()
    {
        var model = new ModelStub();
        using var fixture = new Fixture(model);
        var previous = await fixture.NewSession("Previous");
        await fixture.Respond(previous.Id, "prior");
        using var output = new StringWriter();
        using var input = new StringReader(
            $"/use {previous.Id.Value}\nfollow-up\n/history\n/new Other\n/history\nfresh\n/sessions\n/close\n" +
            $"cannot-send\n/use {previous.Id.Value}\nback\n/unknown\n/exit\n");
        var console = ActivatorUtilities.CreateInstance<ConsoleApplication>(fixture.Host.Services, input, output);

        await console.RunAsync(CancellationToken.None);

        Assert.Equal(4, model.Invocations.Count);
        Assert.Contains("prior", Texts(model.Invocations[1]));
        Assert.Equal(new[] { "system", "fresh" }, Texts(model.Invocations[2]));
        Assert.Contains("follow-up", Texts(model.Invocations[3]));
        Assert.Contains("User: prior", output.ToString());
        Assert.Contains("[Stop]", output.ToString());
        Assert.Contains("Session closed", output.ToString());
        Assert.Contains("No messages in this session.", output.ToString());
        Assert.Contains("Request failed", output.ToString());
        Assert.DoesNotContain(model.Invocations, invocation => Texts(invocation).Contains("cannot-send"));
    }

    private static AssistantConversationContent Assistant(string text) => new(text, [], ModelFinishReason.Stop);

    private static string[] Texts(ModelInvocation invocation) => invocation.Messages.Select(message => message switch
    {
        ModelSystemMessage system => system.Content,
        ModelUserMessage user => user.Content,
        ModelAssistantMessage assistant => assistant.Text!,
        _ => throw new InvalidOperationException("Unexpected message type.")
    }).ToArray();

    private sealed class Fixture : IDisposable
    {
        public IHost Host { get; }
        public RecordingLoggerProvider Logs { get; } = new();
        public IConversationManager Conversations => Host.Services.GetRequiredService<IConversationManager>();
        public ISessionManager Sessions => Host.Services.GetRequiredService<ISessionManager>();
        public LocalUserContext Identity => Host.Services.GetRequiredService<LocalUserContext>();

        public Fixture(ModelStub model, params string[] args)
        {
            var builder = ApplicationHost.CreateBuilder(["--Agent:SystemInstruction=system", .. args]);
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(Logs);
            builder.Services.AddSingleton<ILanguageModelRuntime>(model);
            Host = builder.Build();
        }

        public async Task<SessionInfo> NewSession(string name = "Test") =>
            await Sessions.CreateAsync(new CreateSessionRequest(
                await Identity.GetUserIdAsync(CancellationToken.None), name), CancellationToken.None);

        public Task<ConversationMessage> Append(SessionId sessionId, ConversationContent content) =>
            Conversations.AppendAsync(new AppendConversationMessageRequest(
                new MessageId(Guid.NewGuid().ToString("N")), sessionId, null, content), CancellationToken.None);

        public Task<ConversationMessagePage> Read(SessionId id) =>
            Conversations.ListBySessionAsync(new ListConversationMessagesRequest(id), CancellationToken.None);

        public async Task<ExecutionOutcome> Respond(SessionId id, string input, CancellationToken token = default)
        {
            using var scope = Host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DialogueCoordinator>().RespondAsync(Request(id, input), token);
        }

        public DialogueRequest Request(SessionId id, string input)
        {
            var settings = Host.Services.GetRequiredService<IOptions<AgentSettings>>().Value;
            return new DialogueRequest(id, input, settings.SystemInstruction,
                settings.MaxInputCharacters, settings.MaxHistoryTurns,
                new ModelOptions(Temperature: settings.Temperature, MaxOutputTokens: settings.MaxOutputTokens));
        }

        public void Dispose() => Host.Dispose();
    }

    private sealed class ModelStub(Func<int, CancellationToken, Task<ModelResponse>>? respond = null) : ILanguageModelRuntime
    {
        public List<ModelInvocation> Invocations { get; } = [];

        public Task<ModelResponse> InvokeAsync(ModelInvocation invocation, CancellationToken cancellationToken)
        {
            Invocations.Add(invocation);
            return respond is null
                ? Task.FromResult(new ModelResponse("answer", [], ModelFinishReason.Stop))
                : respond(Invocations.Count, cancellationToken);
        }
    }

    private sealed class FailingAssistantStore(IConversationManager inner) : IConversationManager
    {
        public Task<ConversationMessage> AppendAsync(AppendConversationMessageRequest request, CancellationToken cancellationToken) =>
            request.Content is AssistantConversationContent
                ? Task.FromException<ConversationMessage>(new IOException("Simulated persistence failure."))
                : inner.AppendAsync(request, cancellationToken);

        public Task<ConversationMessage?> GetAsync(MessageId id, CancellationToken cancellationToken) =>
            inner.GetAsync(id, cancellationToken);

        public Task<ConversationMessagePage> ListBySessionAsync(
            ListConversationMessagesRequest request, CancellationToken cancellationToken) =>
            inner.ListBySessionAsync(request, cancellationToken);
    }
}
