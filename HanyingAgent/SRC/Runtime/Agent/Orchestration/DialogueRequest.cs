using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Management.Sessions.Models;

namespace HanyingAgent.Runtime.Agent.Orchestration;

public sealed record DialogueRequest(
    SessionId SessionId,
    string Input,
    string SystemInstruction,
    int MaxInputCharacters,
    int MaxHistoryTurns,
    ModelOptions? Options = null);
