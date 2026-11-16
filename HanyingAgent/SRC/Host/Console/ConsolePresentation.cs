using Spectre.Console;

namespace HanyingAgent.Host.Console;

internal sealed class ConsolePresentation(IAnsiConsole console)
{
    public static ConsolePresentation? Create(TextWriter output)
    {
        if (!ReferenceEquals(output, System.Console.Out) ||
            System.Console.IsOutputRedirected || System.Console.IsInputRedirected ||
            Environment.GetEnvironmentVariable("TERM") == "dumb")
        {
            return null;
        }

        return new ConsolePresentation(AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output)
        }));
    }

    public void ShowHelp()
    {
        Panel("HanyingAgent", "Local in-memory tasks. Replies appear automatically.", Color.Cyan);
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Command").AddColumn("Purpose");
        foreach (var (command, purpose) in new[]
        {
            ("/help", "Show all commands"),
            ("/logs [on|off]", "Show or switch all console logs"),
            ("/new NAME", "Create and select a session"),
            ("/session [ID]", "Inspect a session"),
            ("/sessions", "List visible sessions"),
            ("/use INDEX|ID", "Switch session"),
            ("/delete INDEX", "Hide session; retain data and running tasks"),
            ("/history [COUNT|all]", "View conversation history"),
            ("/tasks [CURSOR]", "List tasks"),
            ("/task [ID]", "Inspect a task"),
            ("/wait [ID]", "Wait for a task result"),
            ("/cancel [ID]", "Request cancellation"),
            ("/close", "Close current session"),
            ("/exit", "Exit and cancel running tasks")
        })
        {
            table.AddRow(new Text(command, new Style(Color.Cyan)), new Text(purpose));
        }
        console.Write(table);
        Line("Task commands default to the last submitted task.", Color.Grey);
    }

    public void Line(string text, Color? color = null) =>
        console.Write(new Text(text + Environment.NewLine, new Style(color ?? Color.Default)));

    public void Prompt() => console.Write(new Text("You> ", new Style(Color.Cyan, decoration: Decoration.Bold)));

    public void Panel(string title, string body, Color color) =>
        console.Write(new Panel(new Text(body))
            .Header(new PanelHeader(Markup.Escape(title)))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(color)));

    public void Details(string title, params (string Label, string Value)[] rows)
    {
        var table = new Table().Border(TableBorder.Rounded).Title(new TableTitle(Markup.Escape(title)));
        table.AddColumn("Field").AddColumn("Value");
        foreach (var (label, value) in rows)
        {
            table.AddRow(new Text(label, new Style(Color.Cyan)), new Text(value));
        }
        console.Write(table);
    }

    public void Sessions(IEnumerable<(int Index, string Id, string Name, string Status, string Task, bool Selected)> rows)
    {
        if (console.Profile.Width < 80)
        {
            foreach (var row in rows)
            {
                Line($"Session {row.Index}: {row.Id}", Color.Cyan);
                Line($"Selected: {(row.Selected ? "yes" : "no")}");
                Line($"Name: {row.Name}");
                Line($"Status: {row.Status}");
                Line(row.Task);
                Line("");
            }
            return;
        }
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var column in new[] { "#", "Selected", "Session ID", "Name", "Status", "Task" })
        {
            table.AddColumn(column);
        }
        foreach (var row in rows)
        {
            table.AddRow(new Text(row.Index.ToString()), new Text(row.Selected ? "*" : ""),
                new Text(row.Id), new Text(row.Name), new Text(row.Status, new Style(StatusColor(row.Status))),
                new Text(row.Task));
        }
        console.Write(table);
    }

    public static Color StatusColor(string status) => status switch
    {
        "Completed" => Color.Green,
        "Failed" => Color.Red,
        "Cancelled" => Color.Yellow,
        _ => Color.Cyan
    };
}
