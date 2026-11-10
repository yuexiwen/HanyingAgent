namespace HanyingAgent.Contracts.Memory.Models;

/// <summary>
/// Defines ownership, not retention duration or storage technology.
/// </summary>
public enum MemoryScope
{
    Task = 0,
    Session = 1,
    User = 2,
    Global = 3
}
