using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using HanyingAgent.Host.Console;
using Spectre.Console;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Tests.Execution;

public sealed class ConsolePresentationTests
{
    [Theory]
    [InlineData("/help", false)]
    [InlineData("  /HELP  ", false)]
    [InlineData("/help", true)]
    public async Task HelpCommand_DisplaysHelpWithoutRequiringSelectedSession(string command, bool rich)
    {
        using var output = new StringWriter();
        var renderer = rich ? RichRenderer(output) : new ConsoleRenderer(output);
        var state = new ConsoleState();
        var handler = new ConsoleCommandHandler(null!, null!, null!, state, renderer, new ConsoleLogControl(), null!);

        await handler.HandleAsync(command, new HanyingAgent.Contracts.Users.Models.UserId("user"),
            CancellationToken.None, CancellationToken.None);

        foreach (var name in new[] { "/help", "/logs", "/remember", "/new", "/session", "/sessions", "/use", "/delete",
            "/history", "/tasks", "/task", "/wait", "/cancel", "/close", "/exit" })
        {
            Assert.Contains(name, output.ToString());
        }
        Assert.Null(state.Current);
        Assert.Empty(state.Pending);
        Assert.False(output.ToString().Contains('\u001b'));
    }

    [Fact]
    public async Task HelpCommand_RejectsUnexpectedArguments()
    {
        using var output = new StringWriter();
        var handler = new ConsoleCommandHandler(null!, null!, null!, new ConsoleState(),
            new ConsoleRenderer(output), new ConsoleLogControl(), null!);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync("/help extra",
            new HanyingAgent.Contracts.Users.Models.UserId("user"),
            CancellationToken.None, CancellationToken.None));

        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task RedirectedWriter_PreservesPlainTextWithoutTerminalEscapes()
    {
        using var output = new StringWriter();
        Assert.Null(ConsolePresentation.Create(output));
        var renderer = new ConsoleRenderer(output);

        await renderer.RenderResultAsync(Result());
        await renderer.ShowPromptAsync();

        Assert.Equal($"Task: task-1 [Completed]{Environment.NewLine}" +
            $"Assistant> [red]你好[/] [link=https://example.com]literal[/]{Environment.NewLine}You> ",
            output.ToString());
        Assert.False(output.ToString().Contains('\u001b'));
    }

    [Fact]
    public async Task RichOutput_RendersPanelsAndTreatsModelTextAsLiteral()
    {
        using var output = new StringWriter();
        var renderer = RichRenderer(output);

        await renderer.RenderResultAsync(Result() with { Error = "[red]error[/]" });

        var text = output.ToString();
        Assert.Contains("Assistant", text);
        Assert.Contains("[red]你好[/]", text);
        Assert.Contains("[link=https://example.com]literal[/]", text);
        Assert.Contains("[red]error[/]", text);
        Assert.Contains("Error", text);
        Assert.Contains("Completed", text);
        Assert.False(text.Contains('\u001b'));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task RichOutput_RespectsDuplicateNotificationFlags(bool status, bool body)
    {
        using var output = new StringWriter();
        await RichRenderer(output).RenderResultAsync(Result(), status, body);

        Assert.Equal(status, output.ToString().Contains("task-1"));
        Assert.Equal(body, output.ToString().Contains("Assistant"));
    }

    [Fact]
    public async Task RichHelp_ShowsAllCommandsAndPrompt()
    {
        using var output = new StringWriter();
        var renderer = RichRenderer(output);
        await renderer.ShowHelpAsync();
        await renderer.ShowPromptAsync();

        foreach (var command in new[] { "/help", "/logs", "/remember", "/new", "/session", "/sessions", "/use", "/delete",
            "/history", "/tasks", "/task", "/wait", "/cancel", "/close", "/exit" })
        {
            Assert.Contains(command, output.ToString());
        }
        Assert.Contains("HanyingAgent", output.ToString());
        Assert.EndsWith("You> ", output.ToString());
    }

    [Theory]
    [InlineData(32)]
    [InlineData(120)]
    public void Tables_RenderLiteralNamesAndFullIdsAtDifferentWidths(int width)
    {
        using var output = new StringWriter();
        var console = CreateConsole(output, width);
        var presentation = new ConsolePresentation(console);
        presentation.Sessions([(1, "0123456789abcdef0123456789abcdef", "[red]name[/]", "Idle", "none", true)]);
        presentation.Details("[blue]literal title[/]", ("Name", "[green]literal value[/]"));

        var text = output.ToString();
        var compact = new string(text.Where(character =>
            !char.IsWhiteSpace(character) && character != '│').ToArray());
        Assert.Contains("Selected", compact);
        Assert.Contains("literal", compact);
        Assert.Contains("0123456789abcdef0123456789abcdef", compact);
        Assert.False(text.Contains('\u001b'));
    }

    private static TaskResult Result() => new(new TaskId("task-1"), TaskStatus.Completed,
        "[red]你好[/] [link=https://example.com]literal[/]", null, DateTimeOffset.UtcNow);

    private static ConsoleRenderer RichRenderer(StringWriter output) =>
        new(output, new ConsolePresentation(CreateConsole(output, 120)));

    private static IAnsiConsole CreateConsole(StringWriter output, int width)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output)
        });
        console.Profile.Width = width;
        return console;
    }
}
