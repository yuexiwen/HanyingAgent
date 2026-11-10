namespace HanyingAgent.Contracts.LanguageModels.Models;

public enum ModelFinishReason
{
    Stop = 0,
    ToolCalls = 1,
    Length = 2,
    ContentFiltered = 3,
    Refused = 4,
    Unknown = 5
}
