using HanyingAgent.Contracts.Agent.Capabilities.Skills.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Skills.Operations;

public interface ISkillManager
{
    Task<IReadOnlyList<SkillDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken);

    Task<SkillDocument> LoadAsync(string skillName, CancellationToken cancellationToken);
}
