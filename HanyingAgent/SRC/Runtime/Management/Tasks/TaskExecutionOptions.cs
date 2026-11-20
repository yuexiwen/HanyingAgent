using HanyingAgent.Contracts.Agent.LanguageModels.Models;

namespace HanyingAgent.Runtime.Management.Tasks;

public sealed record TaskExecutionOptions(
    string SystemInstruction,
    int MaxInputCharacters,
    int MaxHistoryTurns,
    ModelOptions ModelOptions);
