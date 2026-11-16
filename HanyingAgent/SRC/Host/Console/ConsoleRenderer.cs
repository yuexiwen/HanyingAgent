using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Host.Console;

internal sealed class ConsoleRenderer
{
    private readonly TextWriter output;
    private readonly ConsolePresentation? presentation;

    public ConsoleRenderer(TextWriter output) : this(output, ConsolePresentation.Create(output)) { }

    internal ConsoleRenderer(TextWriter output, ConsolePresentation? presentation)
    {
        this.output = output;
        this.presentation = presentation;
    }

    public Task WriteLineAsync(string text = "")
    {
        if (presentation is null)
        {
            return output.WriteLineAsync(text);
        }
        presentation.Line(text);
        return Task.CompletedTask;
    }

    public Task ShowHelpAsync()
    {
        if (presentation is not null)
        {
            presentation.ShowHelp();
            return Task.CompletedTask;
        }
        return WriteLineAsync(
        "HanyingAgent - local in-memory tasks. Replies appear automatically. /help, /logs [on|off], /new NAME, /session [ID], /sessions, /use INDEX|ID, /delete INDEX, /history [COUNT|all], /tasks [CURSOR], /task [ID], /wait [ID], /cancel [ID], /close, /exit. Task commands default to the last submitted task. /delete hides without deleting data. Exit cancels running tasks.");
    }

    public async Task ShowPromptAsync()
    {
        if (presentation is null)
        {
            await output.WriteAsync("You> ");
        }
        else
        {
            presentation.Prompt();
        }
        await output.FlushAsync();
    }

    public async Task RenderResultAsync(TaskResult result, bool includeStatus = true, bool includeBody = true)
    {
        if (presentation is not null)
        {
            if (includeStatus)
            {
                presentation.Line($"Task: {result.TaskId.Value} [{result.Status}]",
                    ConsolePresentation.StatusColor(result.Status.ToString()));
            }
            if (includeBody)
            {
                if (!string.IsNullOrWhiteSpace(result.FinalAnswer))
                {
                    presentation.Panel("Assistant", result.FinalAnswer, Spectre.Console.Color.Green);
                }
                if (result.Error is { } problem)
                {
                    presentation.Panel("Error", problem, Spectre.Console.Color.Red);
                }
            }
            return;
        }
        if (includeStatus)
        {
            await WriteLineAsync($"Task: {result.TaskId.Value} [{result.Status}]");
        }
        if (!includeBody)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(result.FinalAnswer))
        {
            await WriteLineAsync($"Assistant> {result.FinalAnswer}");
        }
        if (result.Error is { } error)
        {
            await WriteLineAsync($"[{error}]");
        }
    }

    public async Task RenderOtherSessionAsync(SessionId sessionId, TaskResult result)
    {
        await WriteLineAsync(
            $"[Session: {sessionId.Value}, Task: {result.TaskId.Value} [{result.Status}]. Use /use {sessionId.Value} to view the update.]");
        if (result.Error is { } error)
        {
            await WriteLineAsync($"[Task: {result.TaskId.Value}: {error}]");
        }
    }

    public async Task RenderSessionAsync(SessionInfo session, SessionId? current, TaskInfo? task)
    {
        if (presentation is not null)
        {
            presentation.Details("Session",
                ("ID", session.Id.Value), ("Name", session.Name), ("Status", session.Status.ToString()),
                ("Selected", session.Id == current ? "yes" : "no"),
                ("Created", session.CreatedAt.ToString("O")),
                ("Last active", session.LastActiveAt.ToString("O")),
                ("Task", DescribeSessionTask(task)));
            if (session.Status == SessionStatus.Active && task is null)
            {
                presentation.Line("[A standalone dialogue is in progress without a TaskId.]");
            }
            return;
        }
        await WriteLineAsync($"Session: {session.Id.Value} ({session.Name}) [{session.Status}]");
        await WriteLineAsync($"Selected: {(session.Id == current ? "yes" : "no")}");
        await WriteLineAsync($"Created: {session.CreatedAt:O}, last active: {session.LastActiveAt:O}");
        await WriteLineAsync(DescribeSessionTask(task));
        if (session.Status == SessionStatus.Active && task is null)
        {
            await WriteLineAsync("[A standalone dialogue is in progress without a TaskId.]");
        }
    }

    public Task RenderSessionRowAsync(int index, SessionInfo session, SessionId? current, TaskInfo? task) =>
        WriteLineAsync(
            $"{index} {(session.Id == current ? "*" : " ")} {session.Id.Value} {session.Name} [{session.Status}] | {DescribeSessionTask(task)}");

    public async Task RenderSessionListAsync(
        IReadOnlyList<(SessionInfo Session, TaskInfo? Task)> rows, SessionId? current)
    {
        if (presentation is not null)
        {
            presentation.Sessions(rows.Select((row, index) =>
                (index + 1, row.Session.Id.Value, row.Session.Name, row.Session.Status.ToString(),
                    DescribeSessionTask(row.Task), row.Session.Id == current)));
            return;
        }
        for (var index = 0; index < rows.Count; index++)
        {
            await RenderSessionRowAsync(index + 1, rows[index].Session, current, rows[index].Task);
        }
    }

    public Task RenderTaskAsync(TaskInfo task)
    {
        if (presentation is null)
        {
            return WriteLineAsync($"Task: {task.Id.Value} [{task.Status}] v{task.Version}");
        }
        presentation.Line($"Task: {task.Id.Value} [{task.Status}] v{task.Version}",
            ConsolePresentation.StatusColor(task.Status.ToString()));
        return Task.CompletedTask;
    }

    private static string DescribeSessionTask(TaskInfo? task)
    {
        if (task is null)
        {
            return "Task: none (no tasks submitted)";
        }
        var finished = task.Status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Cancelled;
        return $"Task: {task.Id.Value} [{task.Status}] | Finished: {(finished ? "yes" : "no")}";
    }

    public async Task RenderHistoryAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken token)
    {
        if (messages.Count == 0)
        {
            await WriteLineAsync("[No messages in this session.]");
        }
        for (var index = 0; index < messages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var message = messages[index];
            var text = message.Content switch
            {
                UserConversationContent user => $"User: {user.Text}",
                AssistantConversationContent assistant => $"Assistant [{assistant.FinishReason}]: {assistant.Text}",
                _ => throw new NotSupportedException("Unsupported history content.")
            };
            if (presentation is null)
            {
                await WriteLineAsync($"{message.Sequence} {text}");
            }
            else
            {
                presentation.Panel($"Message {message.Sequence}", text,
                    message.Content is UserConversationContent ? Spectre.Console.Color.Cyan : Spectre.Console.Color.Green);
            }
            if (message.Content is UserConversationContent &&
                (index + 1 == messages.Count || messages[index + 1].Content is not AssistantConversationContent ||
                 messages[index + 1].TaskId != message.TaskId))
            {
                await WriteLineAsync("[No assistant response recorded for this turn.]");
            }
        }
    }
}
