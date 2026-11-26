using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using HanyingAgent.Contracts.Agent.Capabilities.Prompts.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Prompts.Operations;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace HanyingAgent.Runtime.Agent.Capabilities.Prompts;

public sealed partial class LocalPromptManager : IPromptManager
{
    private readonly string? _directory;
    private readonly int _maxFileBytes;
    private readonly int _maxPromptCount;
    private readonly int _maxOutputCharacters;
    private readonly ILogger<LocalPromptManager> _logger;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private FrozenDictionary<string, Template>? _templates;

    public LocalPromptManager(
        string? directory, int maxFileBytes, int maxPromptCount, int maxOutputCharacters,
        ILogger<LocalPromptManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (directory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            if (!Path.IsPathFullyQualified(directory))
            {
                throw new ArgumentException("Prompt directory must be an absolute path.", nameof(directory));
            }
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfEqual(maxFileBytes, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPromptCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputCharacters);
        _directory = directory;
        _maxFileBytes = maxFileBytes;
        _maxPromptCount = maxPromptCount;
        _maxOutputCharacters = maxOutputCharacters;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_templates is not null)
            {
                return;
            }
            Dictionary<string, Template> templates = new(StringComparer.Ordinal);
            if (_directory is not null)
            {
                LocalMarkdownFiles.RejectLinkedPath(_directory);
                foreach (var directory in Directory.EnumerateDirectories(_directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (templates.Count >= _maxPromptCount)
                    {
                        throw new InvalidDataException("Prompt count exceeds the configured limit.");
                    }
                    LocalMarkdownFiles.RejectLinkedPath(directory);
                    var path = Path.Combine(directory, "PROMPT.md");
                    LocalMarkdownFiles.RejectLinkedPath(path);
                    var text = await LocalMarkdownFiles.ReadBoundedAsync(path, _maxFileBytes, cancellationToken);
                    var template = Parse(text);
                    if (templates.ContainsKey(template.Definition.Name))
                    {
                        throw new InvalidDataException($"Duplicate Prompt name: {template.Definition.Name}.");
                    }
                    if (!string.Equals(Path.GetFileName(directory), template.Definition.Name, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Prompt directory must match its name: {template.Definition.Name}.");
                    }
                    templates.Add(template.Definition.Name, template);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _templates, templates.ToFrozenDictionary(StringComparer.Ordinal));
            _logger.LogInformation("Prompt catalog initialized with {PromptCount} entries", templates.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Prompt catalog initialization cancelled");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Prompt catalog initialization failed");
            throw;
        }
        finally
        {
            _initialization.Release();
        }
    }

    public Task<IReadOnlyList<PromptDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PromptDefinition> definitions = Array.AsReadOnly(GetTemplates().Values
            .Select(template => template.Definition)
            .OrderBy(definition => definition.Name, StringComparer.Ordinal).ToArray());
        return Task.FromResult(definitions);
    }

    public Task<RenderedPrompt> RenderAsync(
        string promptName, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!NamePattern().IsMatch(promptName))
        {
            throw new ArgumentException("Invalid Prompt name.", nameof(promptName));
        }
        if (!GetTemplates().TryGetValue(promptName, out var template))
        {
            throw new KeyNotFoundException($"Prompt is not available: {promptName}.");
        }
        var values = new Dictionary<string, string>(arguments, StringComparer.Ordinal);
        if (values.Count != template.Definition.Arguments.Count ||
            values.Any(pair => pair.Value is null) ||
            template.Definition.Arguments.Any(argument => !values.ContainsKey(argument.Name)))
        {
            throw new ArgumentException("Prompt arguments must exactly match all declared parameters with non-null values.",
                nameof(arguments));
        }
        StringBuilder result = new();
        var offset = 0;
        foreach (Match match in PlaceholderPattern().Matches(template.Body))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(template.Body.AsSpan(offset, match.Index - offset));
            Append(values[match.Groups[1].Value].AsSpan());
            offset = match.Index + match.Length;
        }
        Append(template.Body.AsSpan(offset));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RenderedPrompt(promptName, result.ToString()));

        void Append(ReadOnlySpan<char> text)
        {
            if (text.Length > _maxOutputCharacters - result.Length)
            {
                throw new InvalidOperationException("Rendered Prompt exceeds the configured character limit.");
            }
            result.Append(text);
        }
    }

    private FrozenDictionary<string, Template> GetTemplates() =>
        Volatile.Read(ref _templates) ??
        throw new InvalidOperationException("Prompt catalog has not been initialized.");

    private static Template Parse(string text)
    {
        var (mapping, body) = LocalMarkdownFiles.Parse(text);
        Dictionary<string, YamlNode> fields = new(StringComparer.Ordinal);
        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode { Value: "name" or "description" or "arguments" } scalar ||
                !fields.TryAdd(scalar.Value, value))
            {
                throw new InvalidDataException("Prompt metadata only supports unique name, description and arguments fields.");
            }
        }
        var name = RequiredText(fields, "name");
        var description = RequiredText(fields, "description");
        if (!NamePattern().IsMatch(name) || string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidDataException("Prompt requires a valid name and nonblank template.");
        }
        List<PromptArgumentDefinition> arguments = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        if (fields.TryGetValue("arguments", out var node))
        {
            if (node is not YamlSequenceNode sequence)
            {
                throw new InvalidDataException("Prompt arguments must be a YAML sequence.");
            }
            foreach (var argument in sequence.Children)
            {
                if (argument is not YamlMappingNode argumentMapping)
                {
                    throw new InvalidDataException("Each Prompt argument must be a mapping.");
                }
                Dictionary<string, YamlNode> argumentFields = new(StringComparer.Ordinal);
                foreach (var (key, value) in argumentMapping.Children)
                {
                    if (key is not YamlScalarNode { Value: "name" or "description" } scalar ||
                        !argumentFields.TryAdd(scalar.Value, value))
                    {
                        throw new InvalidDataException("Prompt arguments only support unique name and description fields.");
                    }
                }
                var argumentName = RequiredText(argumentFields, "name");
                if (!ArgumentPattern().IsMatch(argumentName) || !names.Add(argumentName))
                {
                    throw new InvalidDataException("Prompt argument names must be valid and unique.");
                }
                arguments.Add(new PromptArgumentDefinition(argumentName, RequiredText(argumentFields, "description")));
            }
        }
        var remaining = PlaceholderPattern().Replace(body, "");
        var used = PlaceholderPattern().Matches(body).Select(match => match.Groups[1].Value);
        if (remaining.Contains("{{", StringComparison.Ordinal) || remaining.Contains("}}", StringComparison.Ordinal) ||
            !names.SetEquals(used))
        {
            throw new InvalidDataException("Prompt placeholders must be well-formed and exactly match declared arguments.");
        }
        return new Template(new PromptDefinition(name, description, arguments.AsReadOnly()), body);
    }

    private static string RequiredText(Dictionary<string, YamlNode> fields, string name)
    {
        if (!fields.TryGetValue(name, out var node) ||
            node is not YamlScalarNode { Value: { } value } || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Prompt metadata requires nonblank text for {name}.");
        }
        return value;
    }

    private sealed record Template(PromptDefinition Definition, string Body);

    [GeneratedRegex(@"\A(?=.{1,64}\z)[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentPattern();

    [GeneratedRegex(@"\{\{([a-z][a-z0-9_]{0,63})\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
