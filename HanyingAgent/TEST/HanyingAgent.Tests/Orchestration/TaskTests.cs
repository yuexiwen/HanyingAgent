using System.Collections.Concurrent;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Responses;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations;
using HanyingAgent.Contracts.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Users.Operations;
using HanyingAgent.Contracts.Users.Operations.Requests;
using HanyingAgent.Host;
using HanyingAgent.Runtime.Conversation;
using HanyingAgent.Runtime.Orchestration;
using HanyingAgent.Runtime.Sessions;
using HanyingAgent.Runtime.Users;
using HanyingAgent.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Tests.Orchestration;

public sealed class TaskTests
{
    [Fact]
    public async Task Submit_ReturnsAcceptedSnapshotAndOwnsSessionUntilHistoryAndResultAreSaved()
    {
        var entered = Signal();
        var release = Signal();
        var model = new ModelStub(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Answer();
        });
        await using var fixture = await Fixture.Create(model);
        var session = await fixture.NewSession();
        var accepted = await fixture.Submit(session.Id, "first").WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(TaskStatus.Created, accepted.Status);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var active = await fixture.Sessions.GetAsync(session.Id, CancellationToken.None);
            Assert.Equal(SessionStatus.Active, active!.Status);
            Assert.Equal(accepted.Id, active.CurrentTaskId);
            var running = await fixture.Tasks.GetAsync(accepted.Id, CancellationToken.None);
            Assert.Equal(TaskStatus.WaitingForModel, running!.Status);
            Assert.True(running.Version > accepted.Version);
            Assert.Null(await fixture.Tasks.GetResultAsync(accepted.Id, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Submit(session.Id, "duplicate"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Sessions.CloseAsync(session.Id, CancellationToken.None));
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await fixture.Wait(accepted.Id);
        Assert.Equal(TaskStatus.Completed, result.Status);
        Assert.Equal("answer", result.FinalAnswer);
        Assert.Null(result.Error);
        await fixture.AssertIdle(session.Id);
        var history = await fixture.History(session.Id);
        Assert.Equal(2, history.Items.Count);
        Assert.All(history.Items, message => Assert.Equal(accepted.Id, message.TaskId));
        Assert.All(fixture.Logs.Entries.Where(entry => entry.Properties.ContainsKey("ExecutionId")), entry =>
        {
            Assert.Equal(accepted.Id.Value, entry.Properties["TaskId"]);
            Assert.Equal(session.Id.Value, entry.Properties["SessionId"]);
        });

        var next = await fixture.Submit(session.Id, "second");
        await fixture.Wait(next.Id);
        Assert.Equal(new[] { "system", "first", "answer", "second" },
            Texts(model.Invocations.Last()));
        Assert.Equal(4, (await fixture.History(session.Id)).Items.Count);
    }

    [Fact]
    public async Task ConcurrentSubmissions_OnlyOneWinsSameSessionButOtherSessionsRunInParallel()
    {
        var twoEntered = Signal();
        var release = Signal();
        var count = 0;
        var model = new ModelStub(async (_, token) =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                twoEntered.TrySetResult();
            }
            await release.Task.WaitAsync(token);
            return Answer();
        });
        await using var fixture = await Fixture.Create(model);
        var first = await fixture.NewSession();
        var second = await fixture.NewSession();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            try
            {
                return await fixture.Submit(first.Id, $"input-{index}");
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }));
        var winner = Assert.Single(attempts.OfType<TaskInfo>());
        var parallel = await fixture.Submit(second.Id, "other");
        try
        {
            await twoEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, model.Invocations.Count);
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(fixture.Wait(winner.Id), fixture.Wait(parallel.Id));
        Assert.Single((await fixture.Tasks.ListBySessionAsync(new(first.Id), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Cancellation_IsNotConfirmedUntilUncooperativeModelActuallyStops()
    {
        var entered = Signal();
        var release = Signal();
        var model = new ModelStub(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Answer();
        });
        await using var fixture = await Fixture.Create(model);
        var session = await fixture.NewSession();
        var task = await fixture.Submit(session.Id, "question");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var cancelling = await fixture.Tasks.CancelAsync(task.Id, CancellationToken.None);
            Assert.Equal(TaskStatus.Cancelling, cancelling.Status);
            Assert.Null(await fixture.Tasks.GetResultAsync(task.Id, CancellationToken.None));
            Assert.Equal(SessionStatus.Active,
                (await fixture.Sessions.GetAsync(session.Id, CancellationToken.None))!.Status);
            var retry = await fixture.Tasks.CancelAsync(task.Id, CancellationToken.None);
            Assert.Equal(cancelling.Version, retry.Version);
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await fixture.Wait(task.Id);
        Assert.Equal(TaskStatus.Cancelled, result.Status);
        Assert.Null(result.FinalAnswer);
        Assert.Single((await fixture.History(session.Id)).Items);
        await fixture.AssertIdle(session.Id);
    }

    [Fact]
    public async Task CancellingSubmissionTokenOrResultWait_DoesNotCancelAcceptedTask()
    {
        var entered = Signal();
        var release = Signal();
        var model = new ModelStub(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Answer();
        });
        await using var fixture = await Fixture.Create(model);
        var session = await fixture.NewSession();
        using var caller = new CancellationTokenSource();
        var accepted = await fixture.Tasks.SubmitAsync(new(session.Id, "question"), caller.Token);
        caller.Cancel();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Tasks.WaitForResultAsync(accepted.Id, caller.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Tasks.GetAsync(accepted.Id, caller.Token));
            Assert.Equal(TaskStatus.WaitingForModel,
                (await fixture.Tasks.GetAsync(accepted.Id, CancellationToken.None))!.Status);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.Equal(TaskStatus.Completed, (await fixture.Wait(accepted.Id)).Status);
    }

    [Theory]
    [InlineData(ModelFinishReason.Length, "Output truncated")]
    [InlineData(ModelFinishReason.Unknown, "Completion is unknown")]
    [InlineData(ModelFinishReason.Refused, "refused")]
    [InlineData(ModelFinishReason.ContentFiltered, "filtered")]
    [InlineData(ModelFinishReason.ToolCalls, "Tool execution")]
    public async Task NonDeliveredOutcomes_FailTaskAndPreservePartialText(ModelFinishReason reason, string error)
    {
        await using var fixture = await Fixture.Create(new ModelStub((_, _) =>
            Task.FromResult(new ModelResponse("partial", [], reason))));
        var session = await fixture.NewSession();
        var task = await fixture.Submit(session.Id, "question");
        var result = await fixture.Wait(task.Id);

        Assert.Equal(TaskStatus.Failed, result.Status);
        Assert.Equal("partial", result.FinalAnswer);
        Assert.Contains(error, result.Error);
        Assert.Equal(reason == ModelFinishReason.ToolCalls ? 1 : 2,
            (await fixture.History(session.Id)).Items.Count);
        await fixture.AssertIdle(session.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownAndUnexpectedFailures_AreObservableAndReleaseSession(bool unexpected)
    {
        await using var fixture = await Fixture.Create(new ModelStub((_, _) =>
            Task.FromException<ModelResponse>(unexpected
                ? new ApplicationException("unexpected")
                : new HttpRequestException("offline"))));
        var session = await fixture.NewSession();
        var task = await fixture.Submit(session.Id, "question");
        var result = await fixture.Wait(task.Id);

        Assert.Equal(TaskStatus.Failed, result.Status);
        Assert.Contains("Request failed", result.Error);
        Assert.Single(fixture.Model.Invocations);
        Assert.Single((await fixture.History(session.Id)).Items);
        Assert.Contains(fixture.Logs.Entries, entry => entry.Level == LogLevel.Error);
        await fixture.AssertIdle(session.Id);
    }

    [Fact]
    public async Task DependencyCancellationWithoutTaskCancellation_IsFailureNotCancelled()
    {
        await using var fixture = await Fixture.Create(new ModelStub((_, _) =>
            Task.FromException<ModelResponse>(new OperationCanceledException("dependency"))));
        var session = await fixture.NewSession();
        var task = await fixture.Submit(session.Id, "question");

        var result = await fixture.Wait(task.Id);

        Assert.Equal(TaskStatus.Failed, result.Status);
        Assert.Contains("timed out or dependency cancelled", result.Error);
        await fixture.AssertIdle(session.Id);
    }

    [Fact]
    public async Task SavingAssistantFailure_DoesNotRegenerateAnswerOrReportSuccess()
    {
        await using var fixture = await Fixture.Create(new ModelStub(), services =>
            services.AddSingleton<IConversationManager>(provider =>
                new FailingAssistantManager(ActivatorUtilities.CreateInstance<LocalConversationManager>(provider))));
        var session = await fixture.NewSession();
        var task = await fixture.Submit(session.Id, "question");

        var result = await fixture.Wait(task.Id);

        Assert.Equal(TaskStatus.Failed, result.Status);
        Assert.Single(fixture.Model.Invocations);
        Assert.Single((await fixture.History(session.Id)).Items);
        Assert.Contains(fixture.Logs.Entries, entry => entry.EventId.Id == 4003);
        await fixture.AssertIdle(session.Id);
    }

    [Fact]
    public async Task Shutdown_CancelsAndAwaitsRunningTasksThenRejectsFurtherSubmissions()
    {
        var twoEntered = Signal();
        var count = 0;
        await using var fixture = await Fixture.Create(new ModelStub(async (_, token) =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                twoEntered.TrySetResult();
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Answer();
        }));
        var first = await fixture.NewSession();
        var second = await fixture.NewSession();
        var tasks = new[] { await fixture.Submit(first.Id, "one"), await fixture.Submit(second.Id, "two") };
        await twoEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await fixture.Host.StopAsync(timeout.Token);

        foreach (var task in tasks)
        {
            Assert.Equal(TaskStatus.Cancelled,
                (await fixture.Tasks.GetResultAsync(task.Id, CancellationToken.None))!.Status);
            await fixture.AssertIdle(task.SessionId);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Submit(first.Id, "after stop"));
    }

    [Fact]
    public async Task TerminalTaskCannotBeCancelledOrReleaseTheNextTasksSession()
    {
        var entered = Signal();
        var release = Signal();
        var count = 0;
        await using var fixture = await Fixture.Create(new ModelStub(async (_, token) =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            return Answer();
        }));
        var session = await fixture.NewSession();
        var first = await fixture.Submit(session.Id, "first");
        var completed = await fixture.Wait(first.Id);
        var snapshot = await fixture.Tasks.GetAsync(first.Id, CancellationToken.None);
        var second = await fixture.Submit(session.Id, "second");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(snapshot, await fixture.Tasks.CancelAsync(first.Id, CancellationToken.None));
            var store = fixture.Host.Services.GetRequiredService<ITaskStore>();
            Assert.Equal(completed, store.FinishTask(first.Id, session.UserId, null, "late failure", true, DateTimeOffset.UtcNow));
            Assert.Equal(second.Id, (await fixture.Sessions.GetAsync(session.Id, CancellationToken.None))!.CurrentTaskId);
        }
        finally
        {
            release.TrySetResult();
        }
        await fixture.Wait(second.Id);
    }

    [Fact]
    public async Task TaskQueriesAndMessageAssociations_EnforceOwnershipAndActiveTask()
    {
        var entered = Signal();
        var release = Signal();
        await using var fixture = await Fixture.Create(new ModelStub(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Answer();
        }));
        var first = await fixture.NewSession();
        var second = await fixture.NewSession();
        var task = await fixture.Submit(first.Id, "question");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var conversations = fixture.Host.Services.GetRequiredService<IConversationManager>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.AppendAsync(new(
                new MessageId("cross-session"), second.Id, task.Id, new UserConversationContent("invalid")),
                CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.AppendAsync(new(
                new MessageId("no-task"), first.Id, null, new UserConversationContent("invalid")),
                CancellationToken.None));
            var foreign = await fixture.Host.Services.GetRequiredService<IUserManager>()
                .CreateAsync(new CreateUserRequest("other"), CancellationToken.None);
            var foreignSession = fixture.Host.Services.GetRequiredService<ISessionStore>()
                .Add(foreign.Id, "foreign", DateTimeOffset.UtcNow, CancellationToken.None);
            var foreignTask = fixture.Host.Services.GetRequiredService<ITaskStore>()
                .CreateTask(new(foreignSession.Id, "private"), foreign.Id, DateTimeOffset.UtcNow, CancellationToken.None);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                fixture.Tasks.GetAsync(foreignTask.Id, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                fixture.Tasks.GetResultAsync(foreignTask.Id, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                fixture.Tasks.CancelAsync(foreignTask.Id, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                fixture.Tasks.ListBySessionAsync(new(foreignSession.Id), CancellationToken.None));
        }
        finally
        {
            release.TrySetResult();
        }
        await fixture.Wait(task.Id);
    }

    [Fact]
    public async Task TaskPagination_IsStableAcrossNewSubmissionsAndRejectsCrossQueryCursors()
    {
        var clock = new AdjustableClock();
        await using var fixture = await Fixture.Create(new ModelStub(),
            services => services.AddSingleton<TimeProvider>(clock));
        var session = await fixture.NewSession();
        var ids = new List<TaskId>();
        for (var index = 0; index < 4; index++)
        {
            var task = await fixture.Submit(session.Id, $"question-{index}");
            ids.Add(task.Id);
            await fixture.Wait(task.Id);
        }
        var first = await fixture.Tasks.ListBySessionAsync(new(session.Id, PageSize: 2), CancellationToken.None);
        Assert.NotNull(first.ContinuationToken);
        clock.Now = clock.Now.AddDays(-1);
        var newest = await fixture.Submit(session.Id, "new");
        await fixture.Wait(newest.Id);
        var older = await fixture.Tasks.ListBySessionAsync(
            new(session.Id, PageSize: 2, ContinuationToken: first.ContinuationToken), CancellationToken.None);
        Assert.Equal(4, first.Items.Concat(older.Items).Select(task => task.Id).Distinct().Count());
        Assert.DoesNotContain(older.Items, task => task.Id == newest.Id);
        Assert.Null(older.ContinuationToken);
        var other = await fixture.NewSession();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Tasks.ListBySessionAsync(
            new(other.Id, ContinuationToken: first.ContinuationToken), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Tasks.ListBySessionAsync(
            new(session.Id, Status: TaskStatus.Completed, ContinuationToken: first.ContinuationToken), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Tasks.ListBySessionAsync(
            new(session.Id, ContinuationToken: "invalid"), CancellationToken.None));
    }

    [Fact]
    public async Task Console_CanQueryAndCancelTaskBeforeWaitingAndThenCloseSession()
    {
        await using var fixture = await Fixture.Create(new ModelStub(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Answer();
        }));
        using var input = new StringReader("question\n/tasks\n/cancel\n/wait\n/close\n/exit\n");
        using var output = new StringWriter();
        var console = ActivatorUtilities.CreateInstance<ConsoleApplication>(fixture.Host.Services, input, output);

        await console.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        var text = output.ToString();
        Assert.Contains("Task accepted:", text);
        Assert.Contains("[Cancelling]", text);
        Assert.Contains("[Cancelled]", text);
        Assert.Contains("Session closed", text);
        Assert.DoesNotContain("Assistant>", text);
        Assert.DoesNotContain("Request failed", text);
    }

    [Fact]
    public async Task ShutdownDeadline_DoesNotPretendUncooperativeExecutionHasStopped()
    {
        var entered = Signal();
        var release = Signal();
        await using var fixture = await Fixture.Create(new ModelStub(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Answer();
        }));
        var session = await fixture.NewSession();
        var task = await fixture.Submit(session.Id, "question");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var deadline = new CancellationTokenSource();
            deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Tasks.StopAsync(deadline.Token));
            Assert.Null(await fixture.Tasks.GetResultAsync(task.Id, CancellationToken.None));
            Assert.Equal(TaskStatus.Cancelling,
                (await fixture.Tasks.GetAsync(task.Id, CancellationToken.None))!.Status);
            Assert.Equal(task.Id,
                (await fixture.Sessions.GetAsync(session.Id, CancellationToken.None))!.CurrentTaskId);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.Equal(TaskStatus.Cancelled, (await fixture.Wait(task.Id)).Status);
        await fixture.AssertIdle(session.Id);
    }

    [Fact]
    public async Task CloseAndSubmitRace_HasOneAtomicWinnerWithoutOrphanTask()
    {
        var release = Signal();
        await using var fixture = await Fixture.Create(new ModelStub(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return Answer();
        }));
        var session = await fixture.NewSession();
        var start = Signal();
        TaskInfo? accepted = null;
        SessionInfo? closed = null;
        var submit = Task.Run(async () =>
        {
            await start.Task;
            try { accepted = await fixture.Submit(session.Id, "question"); }
            catch (InvalidOperationException) { }
        });
        var close = Task.Run(async () =>
        {
            await start.Task;
            try { closed = await fixture.Sessions.CloseAsync(session.Id, CancellationToken.None); }
            catch (InvalidOperationException) { }
        });
        start.SetResult();
        try
        {
            await Task.WhenAll(submit, close).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True((accepted is null) != (closed is null));
            var page = await fixture.Tasks.ListBySessionAsync(new(session.Id), CancellationToken.None);
            if (accepted is null)
            {
                Assert.Empty(page.Items);
                Assert.Equal(SessionStatus.Closed, closed!.Status);
                Assert.Null(closed.CurrentTaskId);
            }
            else
            {
                Assert.Single(page.Items);
                Assert.Equal(accepted.Id,
                    (await fixture.Sessions.GetAsync(session.Id, CancellationToken.None))!.CurrentTaskId);
            }
        }
        finally
        {
            release.TrySetResult();
        }
        if (accepted is not null)
        {
            await fixture.Wait(accepted.Id);
            await fixture.AssertIdle(session.Id);
        }
    }

    [Fact]
    public async Task InvalidOrUnsupportedOperations_AreRejectedWithoutCreatingTaskOrOccupancy()
    {
        await using var fixture = await Fixture.Create(new ModelStub());
        var session = await fixture.NewSession();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Submit(session.Id, " "));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Submit(new SessionId("unknown"), "question"));
        Assert.Null(await fixture.Tasks.GetAsync(new TaskId("unknown"), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Tasks.GetResultAsync(new TaskId("unknown"), CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Tasks.SubmitAsync(new(session.Id, "question"), cancelled.Token));
        await fixture.AssertIdle(session.Id);
        Assert.Empty((await fixture.Tasks.ListBySessionAsync(new(session.Id), CancellationToken.None)).Items);
        var task = await fixture.Submit(session.Id, "question");
        await fixture.Wait(task.Id);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            fixture.Tasks.ProvideInputAsync(new(task.Id, 1, "more"), CancellationToken.None));
        await fixture.Sessions.CloseAsync(session.Id, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Submit(session.Id, "closed"));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ModelResponse Answer() => new("answer", [], ModelFinishReason.Stop);
    private static string[] Texts(ModelInvocation invocation) => invocation.Messages.Select(message => message switch
    {
        ModelSystemMessage system => system.Content,
        ModelUserMessage user => user.Content,
        ModelAssistantMessage assistant => assistant.Text!,
        _ => throw new InvalidOperationException()
    }).ToArray();

    private sealed class ModelStub(
        Func<ModelInvocation, CancellationToken, Task<ModelResponse>>? respond = null) : ILanguageModelRuntime
    {
        public ConcurrentQueue<ModelInvocation> Invocations { get; } = new();
        public Task<ModelResponse> InvokeAsync(ModelInvocation invocation, CancellationToken token)
        {
            Invocations.Enqueue(invocation);
            return respond is null ? Task.FromResult(Answer()) : respond(invocation, token);
        }

    }

    private sealed class AdjustableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required IHost Host { get; init; }
        public required ModelStub Model { get; init; }
        public required RecordingLoggerProvider Logs { get; init; }
        public LocalTaskManager Tasks => Host.Services.GetRequiredService<LocalTaskManager>();
        public ISessionManager Sessions => Host.Services.GetRequiredService<ISessionManager>();

        public static async Task<Fixture> Create(ModelStub model, Action<IServiceCollection>? configure = null)
        {
            var logs = new RecordingLoggerProvider();
            var builder = ApplicationHost.CreateBuilder(["--Agent:SystemInstruction=system"]);
            builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateScopes = true, ValidateOnBuild = true
            }));
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Services.AddSingleton<ILanguageModelRuntime>(model);
            configure?.Invoke(builder.Services);
            var host = builder.Build();
            await host.StartAsync();
            return new Fixture { Host = host, Model = model, Logs = logs };
        }

        public async Task<SessionInfo> NewSession() => await Sessions.CreateAsync(
            new(await Host.Services.GetRequiredService<LocalUserContext>().GetUserIdAsync(CancellationToken.None), "Test"),
            CancellationToken.None);
        public Task<TaskInfo> Submit(SessionId id, string input) => Tasks.SubmitAsync(new(id, input), CancellationToken.None);
        public Task<HanyingAgent.Contracts.Tasks.Operations.Responses.TaskResult> Wait(TaskId id) =>
            Tasks.WaitForResultAsync(id, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        public Task<ConversationMessagePage> History(SessionId id) =>
            Host.Services.GetRequiredService<IConversationManager>().ListBySessionAsync(new(id), CancellationToken.None);
        public async Task AssertIdle(SessionId id)
        {
            var session = await Sessions.GetAsync(id, CancellationToken.None);
            Assert.Equal(SessionStatus.Idle, session!.Status);
            Assert.Null(session.CurrentTaskId);
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await Host.StopAsync(timeout.Token);
            }
            finally
            {
                Host.Dispose();
            }
        }
    }

    private sealed class FailingAssistantManager(IConversationManager inner) : IConversationManager
    {
        public Task<ConversationMessage> AppendAsync(AppendConversationMessageRequest request, CancellationToken token) =>
            request.Content is AssistantConversationContent
                ? Task.FromException<ConversationMessage>(new IOException("Simulated save failure."))
                : inner.AppendAsync(request, token);
        public Task<ConversationMessage?> GetAsync(MessageId id, CancellationToken token) => inner.GetAsync(id, token);
        public Task<ConversationMessagePage> ListBySessionAsync(ListConversationMessagesRequest request, CancellationToken token) =>
            inner.ListBySessionAsync(request, token);
    }
}
