using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.Sessions.Models;

namespace HanyingAgent.Runtime.Orchestration;

public sealed record DialogueRequest(
    SessionId SessionId,
    string Input,
    string SystemInstruction,
    int MaxInputCharacters,
    int MaxHistoryTurns,
    ModelOptions? Options = null);
