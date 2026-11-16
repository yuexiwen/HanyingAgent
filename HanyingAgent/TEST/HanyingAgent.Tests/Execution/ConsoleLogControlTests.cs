using HanyingAgent.Host;
using HanyingAgent.Host.Console;
using HanyingAgent.Contracts.Users.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Tests.Execution;

public sealed class ConsoleLogControlTests
{
    [Theory]
    [InlineData("/logs", true)]
    [InlineData("/logs off", false)]
    [InlineData(" /LOGS OFF ", false)]
    [InlineData("/logs on", true)]
    public async Task Command_ReportsStateAndPreservesOrdinaryOutput(string command, bool expected)
    {
        using var output = new StringWriter();
        var control = new ConsoleLogControl();
        var renderer = new ConsoleRenderer(output);
        var handler = new ConsoleCommandHandler(null!, null!, null!, new ConsoleState(), renderer, control);

        await handler.HandleAsync(command, new UserId("user"), CancellationToken.None, CancellationToken.None);
        await renderer.WriteLineAsync("[Task failed.]");

        Assert.Equal(expected, control.Enabled);
        Assert.Contains($"Console logs: {(expected ? "on" : "off")}.", output.ToString());
        Assert.Contains("[Task failed.]", output.ToString());
    }

    [Theory]
    [InlineData("/logs invalid")]
    [InlineData("/logs off extra")]
    public async Task Command_RejectsInvalidArgumentsWithoutChangingState(string command)
    {
        using var output = new StringWriter();
        var control = new ConsoleLogControl();
        var handler = new ConsoleCommandHandler(null!, null!, null!, new ConsoleState(),
            new ConsoleRenderer(output), control);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            command, new UserId("user"), CancellationToken.None, CancellationToken.None));

        Assert.True(control.Enabled);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void Filter_SwitchesExistingLoggerAtAllLevelsAndRestoresConfiguredThreshold()
    {
        var builder = ApplicationHost.CreateBuilder(
            ["--Logging:LogLevel:Default=Warning", "--Logging:LogLevel:Test.Category=Information"]);
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test.Category");
        var other = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Other.Category");
        var control = host.Services.GetRequiredService<ConsoleLogControl>();

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(other.IsEnabled(LogLevel.Information));
        Assert.True(other.IsEnabled(LogLevel.Warning));
        control.Enabled = false;
        foreach (var level in Enum.GetValues<LogLevel>())
        {
            Assert.False(logger.IsEnabled(level));
            Assert.False(other.IsEnabled(level));
        }
        control.Enabled = true;
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(other.IsEnabled(LogLevel.Information));
        Assert.True(other.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Filter_DoesNotSuppressOtherProvidersOrAlterRuleMetadata()
    {
        var control = new ConsoleLogControl { Enabled = false };
        var options = new LoggerFilterOptions { MinLevel = LogLevel.Warning };
        options.Rules.Add(new LoggerFilterRule("Console", "Example", LogLevel.Debug,
            (_, _, level) => level != LogLevel.Trace));

        control.ApplyTo(options);

        var rule = options.Rules[1];
        Assert.Equal("Console", rule.ProviderName);
        Assert.Equal("Example", rule.CategoryName);
        Assert.Equal(LogLevel.Debug, rule.LogLevel);
        Assert.False(rule.Filter!("Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider",
            "Example", LogLevel.Error));
        Assert.True(rule.Filter("AnotherProvider", "Example", LogLevel.Error));
        control.Enabled = true;
        Assert.False(rule.Filter("Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider",
            "Example", LogLevel.Trace));
    }
}
