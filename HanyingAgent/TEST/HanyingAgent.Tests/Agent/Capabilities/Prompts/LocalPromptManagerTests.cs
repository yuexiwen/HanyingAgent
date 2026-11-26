using HanyingAgent.Contracts.Agent.Capabilities.Prompts.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Prompts.Operations;
using HanyingAgent.Host;
using HanyingAgent.Runtime.Agent.Capabilities.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HanyingAgent.Tests.Agent.Capabilities.Prompts;

public sealed class LocalPromptManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hanying-prompts-{Guid.NewGuid():N}");

    public LocalPromptManagerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task DiscoveryAndRender_PreserveContentAndDoNotReinterpretArgumentValues()
    {
        Write("zebra", "Fixed text.");
        Write("translate", "{{language}}: {{text}}\nAgain: {{text}}", TranslationArguments);
        var manager = Manager();
        await manager.InitializeAsync(default);
        var definitions = await manager.ListDefinitionsAsync(default);
        Assert.Equal(["translate", "zebra"], definitions.Select(definition => definition.Name));
        Assert.Equal(["language", "text"], definitions[0].Arguments.Select(argument => argument.Name));
        Assert.Throws<NotSupportedException>(() => ((IList<PromptDefinition>)definitions).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PromptArgumentDefinition>)definitions[0].Arguments).Clear());
        var result = await manager.RenderAsync("translate", new Dictionary<string, string>
        {
            ["language"] = "English",
            ["text"] = "\u4e2d\u6587 {{language}} $1\nNext"
        }, default);
        Assert.Equal("translate", result.Name);
        Assert.Equal("English: \u4e2d\u6587 {{language}} $1\nNext\nAgain: \u4e2d\u6587 {{language}} $1\nNext",
            result.Content);
        Assert.Equal("Fixed text.", (await manager.RenderAsync("zebra", new Dictionary<string, string>(), default)).Content);
    }

    [Fact]
    public async Task MissingUnknownNullAndWrongCaseArguments_AreRejected()
    {
        Write("translate", "{{language}} {{text}}", TranslationArguments);
        var manager = Manager();
        await manager.InitializeAsync(default);
        foreach (var values in new Dictionary<string, string>[]
        {
            new() { ["language"] = "en" },
            new() { ["language"] = "en", ["text"] = "hello", ["extra"] = "x" },
            new() { ["language"] = "en", ["text"] = null! },
            new(StringComparer.OrdinalIgnoreCase) { ["language"] = "en", ["Text"] = "hello" }
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => manager.RenderAsync("translate", values, default));
        }
        var empty = await manager.RenderAsync("translate", new Dictionary<string, string>
        {
            ["language"] = "", ["text"] = ""
        }, default);
        Assert.Equal(" ", empty.Content);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            manager.RenderAsync("missing", new Dictionary<string, string>(), default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.RenderAsync("..\\translate", new Dictionary<string, string>(), default));
    }

    [Theory]
    [InlineData("{{unknown}}")]
    [InlineData("{{ language }} {{text}}")]
    [InlineData("{{language}} {{text")]
    [InlineData("{{language}} {{text}} }}")]
    [InlineData("{{language}}")]
    [InlineData("   ")]
    public async Task InvalidPlaceholdersAndUnusedArguments_FailBeforePublishingCatalog(string body)
    {
        Write("valid", "Valid");
        Write("translate", body, TranslationArguments);
        var manager = Manager();
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.InitializeAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ListDefinitionsAsync(default));
    }

    [Theory]
    [InlineData("arguments: not-a-list")]
    [InlineData("arguments:\n  - name: text\n    description: First\n  - name: text\n    description: Duplicate")]
    [InlineData("arguments:\n  - name: Text\n    description: Wrong case")]
    [InlineData("arguments:\n  - name: text\n    description: Test\n    required: true")]
    [InlineData("arguments:\n  - name: text")]
    [InlineData("extra: value")]
    [InlineData("name: translate")]
    public async Task UnsupportedOrMalformedMetadata_IsRejected(string metadata)
    {
        Write("translate", "{{text}}", metadata);
        await Assert.ThrowsAnyAsync<Exception>(() => Manager().InitializeAsync(default));
    }

    [Fact]
    public async Task Snapshot_IsUnchangedByFileEditsAndConcurrentRenders()
    {
        Write("translate", "{{text}}", TextArgument);
        var manager = Manager();
        await Task.WhenAll(manager.InitializeAsync(default), manager.InitializeAsync(default));
        Write("translate", "Edited template");
        await manager.InitializeAsync(default);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(index =>
            Task.Run(() => manager.RenderAsync("translate", new Dictionary<string, string>
            {
                ["text"] = index.ToString()
            }, default))));
        Assert.Equal(["0", "1", "2", "3", "4"], results.Select(result => result.Content));
    }

    [Fact]
    public async Task OutputLimit_CountsRepeatedSubstitutionsAndUtf16Characters()
    {
        Write("translate", "{{text}}-{{text}}", TextArgument);
        var manager = Manager(maxOutputCharacters: 5);
        await manager.InitializeAsync(default);
        var values = new Dictionary<string, string> { ["text"] = "\U0001F600" };
        Assert.Equal("\U0001F600-\U0001F600", (await manager.RenderAsync("translate", values, default)).Content);
        values["text"] = "abc";
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RenderAsync("translate", values, default));
        values["text"] = new string('x', 100000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RenderAsync("translate", values, default));
    }

    [Fact]
    public async Task FileAndCountLimits_AreEnforcedAtExactBoundary()
    {
        Write("translate", "{{text}}", TextArgument);
        var length = (int)new FileInfo(Path.Combine(_root, "translate", "PROMPT.md")).Length;
        await Manager(maxFileBytes: length, maxPromptCount: 1).InitializeAsync(default);
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager(maxFileBytes: length - 1).InitializeAsync(default));
        Write("second", "Second");
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager(maxPromptCount: 1).InitializeAsync(default));
    }

    [Fact]
    public async Task EmptyCatalogAndCancellation_AreExplicit()
    {
        var manager = new LocalPromptManager(null, 65536, 100, 65536, NullLogger<LocalPromptManager>.Instance);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InitializeAsync(cancelled.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ListDefinitionsAsync(default));
        await manager.InitializeAsync(default);
        Assert.Empty(await manager.ListDefinitionsAsync(default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ListDefinitionsAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.RenderAsync("translate", new Dictionary<string, string>(), cancelled.Token));
    }

    [Fact]
    public async Task MissingFileAndDirectoryMismatch_AreRejected()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "wrong")).FullName;
        await Assert.ThrowsAsync<FileNotFoundException>(() => Manager().InitializeAsync(default));
        await File.WriteAllTextAsync(Path.Combine(directory, "PROMPT.md"),
            "---\nname: translate\ndescription: Test\n---\nBody");
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager().InitializeAsync(default));
    }

    [Fact]
    public async Task Host_InitializesRelativeDirectoryAndRendersThroughContract()
    {
        Write("translate", "{{text}}", TextArgument);
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Prompts:Directory"] = Path.GetRelativePath(builder.Environment.ContentRootPath, _root)
        });
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        var manager = host.Services.GetRequiredService<IPromptManager>();
        var result = await manager.RenderAsync("translate", new Dictionary<string, string> { ["text"] = "Hello" },
            timeout.Token);
        Assert.Equal("Hello", result.Content);
        await host.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Host_RejectsInvalidTemplateAtStartup()
    {
        Write("translate", "{{unknown}}");
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Prompts:Directory"] = _root });
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidDataException>(() => host.StartAsync(timeout.Token));
    }

    private LocalPromptManager Manager(int maxFileBytes = 65536, int maxPromptCount = 100, int maxOutputCharacters = 65536) =>
        new(_root, maxFileBytes, maxPromptCount, maxOutputCharacters, NullLogger<LocalPromptManager>.Instance);

    private void Write(string name, string body, string metadata = "arguments: []")
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "PROMPT.md"),
            $"---\nname: {name}\ndescription: Test prompt\n{metadata}\n---\n{body}");
    }

    private const string TextArgument = "arguments:\n  - name: text\n    description: Input text";
    private const string TranslationArguments =
        "arguments:\n  - name: language\n    description: Output language\n  - name: text\n    description: Input text";

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
