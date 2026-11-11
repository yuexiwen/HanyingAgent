using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Context.Operations;
using HanyingAgent.Contracts.LanguageModels.Operations;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Host.Configuration;
using HanyingAgent.Infrastructure.LanguageModels;
using HanyingAgent.Infrastructure.Persistence;
using HanyingAgent.Runtime.Context;
using HanyingAgent.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Host;

public static class ApplicationHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.Services.Configure<ConsoleLoggerOptions>(
            options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddOptions<OllamaSettings>()
            .Bind(builder.Configuration.GetSection(OllamaSettings.SectionName))
            .Validate(settings => OllamaSettings.IsValidAddress(settings.BaseAddress),
                "Ollama:BaseAddress must be an absolute HTTP(S) URL ending in '/', without credentials, query or fragment.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ModelName),
                "Ollama:ModelName must not be blank.")
            .Validate(settings => settings.ContextWindowTokens > 0,
                "Ollama:ContextWindowTokens must be positive.")
            .Validate(settings => settings.TimeoutSeconds > 0 && settings.TimeoutSeconds <= int.MaxValue / 1000,
                "Ollama:TimeoutSeconds must fit a positive HttpClient timeout.")
            .ValidateOnStart();
        builder.Services.AddOptions<AgentSettings>()
            .Bind(builder.Configuration.GetSection(AgentSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.SystemInstruction),
                "Agent:SystemInstruction must not be blank.")
            .Validate(settings => settings.MaxInputCharacters > 0,
                "Agent:MaxInputCharacters must be positive.")
            .Validate(settings => settings.MaxOutputTokens > 0,
                "Agent:MaxOutputTokens must be positive.")
            .Validate(settings => double.IsFinite(settings.Temperature) && settings.Temperature >= 0,
                "Agent:Temperature must be finite and nonnegative.")
            .ValidateOnStart();

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IWorkingMemoryStore, InMemoryWorkingMemoryStore>();
        builder.Services.AddSingleton<IWorkingMemoryManager, LocalWorkingMemoryManager>();
        builder.Services.AddSingleton<IContextBuilder>(services => new LocalContextBuilder(
            services.GetRequiredService<IWorkingMemoryManager>(),
            services.GetRequiredService<IOptions<AgentSettings>>().Value.MaxInputCharacters,
            services.GetRequiredService<ILogger<LocalContextBuilder>>()));
        builder.Services.AddHttpClient("Ollama", (services, client) =>
        {
            var settings = services.GetRequiredService<IOptions<OllamaSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        }).AddTypedClient<ILanguageModelRuntime>((client, services) =>
        {
            var settings = services.GetRequiredService<IOptions<OllamaSettings>>().Value;
            return new OllamaLanguageModelRuntime(
                client, settings.ModelName, settings.ContextWindowTokens,
                services.GetRequiredService<ILogger<OllamaLanguageModelRuntime>>());
        });
        builder.Services.AddTransient<DirectExecutor>();
        builder.Services.AddSingleton<TextReader>(Console.In);
        builder.Services.AddSingleton<TextWriter>(Console.Out);
        builder.Services.AddSingleton<ConsoleApplication>();
        return builder;
    }
}
