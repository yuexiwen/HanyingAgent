using System.Collections.Frozen;
using System.Text.RegularExpressions;
using HanyingAgent.Contracts.Agent.Capabilities.Skills.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Skills.Operations;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace HanyingAgent.Runtime.Agent.Capabilities.Skills;

public sealed partial class LocalSkillManager : ISkillManager
{
    private readonly string? _directory;
    private readonly int _maxFileBytes;
    private readonly int _maxSkillCount;
    private readonly ILogger<LocalSkillManager> _logger;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private FrozenDictionary<string, SkillDocument>? _documents;

    public LocalSkillManager(
        string? directory, int maxFileBytes, int maxSkillCount, ILogger<LocalSkillManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (directory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            if (!Path.IsPathFullyQualified(directory))
            {
                throw new ArgumentException("Skill directory must be an absolute path.", nameof(directory));
            }
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfEqual(maxFileBytes, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSkillCount);
        _directory = directory;
        _maxFileBytes = maxFileBytes;
        _maxSkillCount = maxSkillCount;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_documents is not null)
            {
                return;
            }
            Dictionary<string, SkillDocument> documents = new(StringComparer.Ordinal);
            if (_directory is not null)
            {
                LocalMarkdownFiles.RejectLinkedPath(_directory);
                foreach (var directory in Directory.EnumerateDirectories(_directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (documents.Count >= _maxSkillCount)
                    {
                        throw new InvalidDataException("Skill count exceeds the configured limit.");
                    }
                    LocalMarkdownFiles.RejectLinkedPath(directory);
                    var path = Path.Combine(directory, "SKILL.md");
                    LocalMarkdownFiles.RejectLinkedPath(path);
                    var text = await LocalMarkdownFiles.ReadBoundedAsync(path, _maxFileBytes, cancellationToken);
                    var document = Parse(text);
                    if (documents.ContainsKey(document.Definition.Name))
                    {
                        throw new InvalidDataException($"Duplicate Skill name: {document.Definition.Name}.");
                    }
                    if (!string.Equals(Path.GetFileName(directory), document.Definition.Name, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Skill directory must match its name: {document.Definition.Name}.");
                    }
                    documents.Add(document.Definition.Name, document);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _documents, documents.ToFrozenDictionary(StringComparer.Ordinal));
            _logger.LogInformation("Skill catalog initialized with {SkillCount} entries", documents.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Skill catalog initialization cancelled");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Skill catalog initialization failed");
            throw;
        }
        finally
        {
            _initialization.Release();
        }
    }

    public Task<IReadOnlyList<SkillDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var documents = GetDocuments();
        IReadOnlyList<SkillDefinition> definitions = Array.AsReadOnly(documents.Values
            .Select(document => document.Definition)
            .OrderBy(definition => definition.Name, StringComparer.Ordinal).ToArray());
        return Task.FromResult(definitions);
    }

    public Task<SkillDocument> LoadAsync(string skillName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        if (!NamePattern().IsMatch(skillName))
        {
            throw new ArgumentException("Invalid Skill name.", nameof(skillName));
        }
        if (!GetDocuments().TryGetValue(skillName, out var document))
        {
            throw new KeyNotFoundException($"Skill is not available: {skillName}.");
        }
        return Task.FromResult(document);
    }

    private FrozenDictionary<string, SkillDocument> GetDocuments() =>
        Volatile.Read(ref _documents) ??
        throw new InvalidOperationException("Skill catalog has not been initialized.");

    private static SkillDocument Parse(string text)
    {
        var (mapping, instructions) = LocalMarkdownFiles.Parse(text);
        Dictionary<string, string> metadata = new(StringComparer.Ordinal);
        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode { Value: "name" or "description" } scalarKey ||
                value is not YamlScalarNode { Value: { } scalarValue } ||
                string.IsNullOrWhiteSpace(scalarValue) || !metadata.TryAdd(scalarKey.Value, scalarValue))
            {
                throw new InvalidDataException("Skill metadata must contain unique name and description strings only.");
            }
        }
        if (!metadata.TryGetValue("name", out var name) || !NamePattern().IsMatch(name) ||
            !metadata.TryGetValue("description", out var description))
        {
            throw new InvalidDataException("Skill requires a valid name and nonblank description.");
        }
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new InvalidDataException($"Skill instructions must not be blank: {name}.");
        }
        return new SkillDocument(new SkillDefinition(name, description), instructions);
    }

    [GeneratedRegex(@"\A(?=.{1,64}\z)[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
