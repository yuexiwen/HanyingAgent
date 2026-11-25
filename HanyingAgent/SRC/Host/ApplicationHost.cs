using HanyingAgent.Runtime.Agent.Capabilities.Memory;
using HanyingAgent.Runtime.Agent.Capabilities.Tools;
using HanyingAgent.Runtime.Agent.Capabilities.Tools.Implementations;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations;
using HanyingAgent.Contracts.Agent.Context.Operations;
using HanyingAgent.Contracts.Agent.LanguageModels.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations;
using HanyingAgent.Host.Configuration;
using HanyingAgent.Infrastructure.LanguageModels;
using HanyingAgent.Infrastructure.Persistence;
using HanyingAgent.Runtime.Agent.Context;
using HanyingAgent.Runtime.Agent.Execution;
using HanyingAgent.Contracts.Management.Users.Operations;
using HanyingAgent.Contracts.Management.Sessions.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations;
using HanyingAgent.Runtime.Management.Users;
using HanyingAgent.Runtime.Management.Sessions;
using HanyingAgent.Runtime.Agent.Capabilities.Conversation;
using HanyingAgent.Runtime.Agent.Orchestration;
using HanyingAgent.Runtime.Management.Tasks;
using HanyingAgent.Contracts.Management.Tasks.Operations;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using HanyingAgent.Host.Console;

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
        var consoleLogs = new ConsoleLogControl();
        builder.Services.AddSingleton(consoleLogs);
        builder.Services.PostConfigure<LoggerFilterOptions>(consoleLogs.ApplyTo);
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
            .Validate(settings => settings.MaxHistoryTurns is > 0 and <= 100,
                "Agent:MaxHistoryTurns must be between 1 and 100.")
            .Validate(settings => settings.HistoryBudgetPercent is > 0 and < 100,
                "Agent:HistoryBudgetPercent must be between 1 and 99.")
            .Validate(settings => settings.TokenSafetyMargin >= 0,
                "Agent:TokenSafetyMargin must be nonnegative.")
            .Validate<IOptions<OllamaSettings>>((settings, model) =>
                (long)settings.MaxOutputTokens + settings.TokenSafetyMargin < model.Value.ContextWindowTokens,
                "Agent:MaxOutputTokens + Agent:TokenSafetyMargin must be smaller than Ollama:ContextWindowTokens.")
            .ValidateOnStart();

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<ILocalTool, GetCurrentTimeTool>();
        builder.Services.AddSingleton<IToolManager, LocalToolManager>();
        builder.Services.AddSingleton<IWorkingMemoryStore, InMemoryWorkingMemoryStore>();
        builder.Services.AddSingleton<IWorkingMemoryManager, LocalWorkingMemoryManager>();
        builder.Services.AddSingleton<IModelTokenCounter>(services =>
        {
            var model = services.GetRequiredService<IOptions<OllamaSettings>>().Value;
            var agent = services.GetRequiredService<IOptions<AgentSettings>>().Value;
            return new ConservativeModelTokenCounter(model.ModelName, model.ContextWindowTokens,
                agent.MaxOutputTokens, agent.TokenSafetyMargin);
        });
        builder.Services.AddSingleton<IContextBuilder>(services => new LocalContextBuilder(
            services.GetRequiredService<IModelTokenCounter>(),
            services.GetRequiredService<IOptions<AgentSettings>>().Value.MaxInputCharacters,
            services.GetRequiredService<ILogger<LocalContextBuilder>>(),
            services.GetRequiredService<IOptions<AgentSettings>>().Value.HistoryBudgetPercent));
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
        builder.Services.AddSingleton(_ => new InMemoryAgentState());
        builder.Services.AddSingleton<IUserStore>(services =>
            new InMemoryUserStore(services.GetRequiredService<InMemoryAgentState>()));
        builder.Services.AddSingleton<ISessionStore>(services =>
            new InMemorySessionStore(services.GetRequiredService<InMemoryAgentState>()));
        builder.Services.AddSingleton<IConversationStore>(services =>
            new InMemoryConversationStore(services.GetRequiredService<InMemoryAgentState>()));
        builder.Services.AddSingleton<ITaskStore>(services =>
            new InMemoryTaskStore(services.GetRequiredService<InMemoryAgentState>()));
        builder.Services.AddSingleton<IUserManager, LocalUserManager>();
        builder.Services.AddSingleton<LocalUserContext>();
        builder.Services.AddSingleton<LocalSessionManager>();
        builder.Services.AddSingleton<ISessionManager>(services => services.GetRequiredService<LocalSessionManager>());
        builder.Services.AddSingleton<IConversationManager, LocalConversationManager>();
        builder.Services.AddSingleton<ConversationWindow>();
        builder.Services.AddSingleton<ConversationHistoryReader>();
        builder.Services.AddTransient<DialogueOrchestrator>();
        builder.Services.AddSingleton(services =>
        {
            var settings = services.GetRequiredService<IOptions<AgentSettings>>().Value;
            return new TaskExecutionOptions(settings.SystemInstruction, settings.MaxInputCharacters,
                settings.MaxHistoryTurns,
                new ModelOptions(Temperature: settings.Temperature, MaxOutputTokens: settings.MaxOutputTokens));
        });
        builder.Services.AddSingleton<LocalTaskManager>();
        builder.Services.AddSingleton<ITaskManager>(services => services.GetRequiredService<LocalTaskManager>());
        builder.Services.AddHostedService(services => services.GetRequiredService<LocalTaskManager>());
        builder.Services.AddSingleton<TextReader>(System.Console.In);
        builder.Services.AddSingleton<TextWriter>(System.Console.Out);
        builder.Services.AddSingleton<ConsoleApplication>();
        builder.Services.AddHostedService<ToolRegistrationValidator>();
        return builder;
    }

    private sealed class ToolRegistrationValidator(IToolManager tools) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = tools;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
