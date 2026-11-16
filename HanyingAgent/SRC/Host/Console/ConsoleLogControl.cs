using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace HanyingAgent.Host.Console;

public sealed class ConsoleLogControl
{
    private int _enabled = 1;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    internal void ApplyTo(LoggerFilterOptions options)
    {
        // Cover the default rule too, without changing configured category/provider precedence.
        options.Rules.Insert(0, new LoggerFilterRule(null, null, options.MinLevel, null));
        for (var index = 0; index < options.Rules.Count; index++)
        {
            var rule = options.Rules[index];
            options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName,
                rule.LogLevel, (provider, category, level) =>
                    (provider != typeof(ConsoleLoggerProvider).FullName || Enabled) &&
                    (rule.Filter?.Invoke(provider, category, level) ?? true));
        }
    }
}
