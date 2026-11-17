using HanyingAgent.Contracts.LanguageModels.Models;

namespace HanyingAgent.Runtime.Tasks;

public sealed record TaskExecutionOptions(
    string SystemInstruction,
    int MaxInputCharacters,
    int MaxHistoryTurns,
    ModelOptions ModelOptions);
