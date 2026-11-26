using System.Text;
using HanyingAgent.Contracts.Agent.Capabilities.Skills.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Skills.Operations;
using HanyingAgent.Host;
using HanyingAgent.Runtime.Agent.Capabilities.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HanyingAgent.Tests.Agent.Capabilities.Skills;

public sealed class LocalSkillManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hanying-skills-{Guid.NewGuid():N}");

    public LocalSkillManagerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task DiscoveryAndLoad_ReturnSortedMetadataAndUnchangedInstructions()
    {
        Write("zebra", "Last", "Do the last task.\r\n");
        Write("analysis", "Analyze documents", "Read.\nVerify.\n");
        var manager = Manager();
        await manager.InitializeAsync(default);
        var definitions = await manager.ListDefinitionsAsync(default);
        Assert.Equal(["analysis", "zebra"], definitions.Select(definition => definition.Name));
        var skill = await manager.LoadAsync("analysis", default);
        Assert.Equal("Analyze documents", skill.Definition.Description);
        Assert.Equal("Read.\nVerify.\n", skill.Instructions);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SkillDefinition>)definitions).Clear());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => manager.LoadAsync("missing", default));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.LoadAsync("..\\analysis", default));
    }

    [Fact]
    public async Task Snapshot_IsStableAfterModificationAndRepeatedInitialization()
    {
        Write("analysis", "Original", "Original body.");
        var manager = Manager();
        await Task.WhenAll(manager.InitializeAsync(default), manager.InitializeAsync(default));
        Write("analysis", "Modified", "Modified body.");
        Write("new-skill", "New", "New body.");
        await manager.InitializeAsync(default);
        Assert.Single(await manager.ListDefinitionsAsync(default));
        Assert.Equal("Original body.", (await manager.LoadAsync("analysis", default)).Instructions);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => manager.LoadAsync("new-skill", default));
    }

    [Theory]
    [InlineData("name: analysis\nname: analysis\ndescription: Test", "Body")]
    [InlineData("name: analysis\ndescription: Test\nextra: value", "Body")]
    [InlineData("name: analysis", "Body")]
    [InlineData("name: Analysis\ndescription: Test", "Body")]
    [InlineData("name: bad--name\ndescription: Test", "Body")]
    [InlineData("name: analysis\ndescription: ''", "Body")]
    [InlineData("name: analysis\ndescription: [one, two]", "Body")]
    [InlineData("name: [", "Body")]
    [InlineData("name: analysis\ndescription: Test", " \n")]
    public async Task InvalidDocument_FailsWithoutPublishingPartialCatalog(string header, string body)
    {
        Write("valid", "Valid", "Valid body.");
        var directory = Directory.CreateDirectory(Path.Combine(_root, "analysis")).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), $"---\n{header}\n---\n{body}");
        var manager = Manager();
        await Assert.ThrowsAnyAsync<Exception>(() => manager.InitializeAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ListDefinitionsAsync(default));
    }

    [Theory]
    [InlineData("No front matter")]
    [InlineData("---\nname: analysis\ndescription: Test")]
    public async Task MissingFrontMatterDelimiters_AreRejected(string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "analysis")).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), text);
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager().InitializeAsync(default));
    }

    [Fact]
    public async Task DirectoryNameMismatchAndDuplicateName_AreRejected()
    {
        Write("analysis", "First", "Body");
        var directory = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        var path = Path.Combine(directory, "SKILL.md");
        await File.WriteAllTextAsync(path, "---\nname: analysis\ndescription: Other\n---\nBody");
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager().InitializeAsync(default));
        File.Delete(Path.Combine(_root, "analysis", "SKILL.md"));
        Directory.Delete(Path.Combine(_root, "analysis"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager().InitializeAsync(default));
    }

    [Fact]
    public async Task ByteAndCountLimits_AreEnforcedAtExactBoundary()
    {
        Write("analysis", "Test", "Document: \u4e2d\u6587");
        var length = (int)new FileInfo(Path.Combine(_root, "analysis", "SKILL.md")).Length;
        await Manager(maxFileBytes: length).InitializeAsync(default);
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager(maxFileBytes: length - 1).InitializeAsync(default));
        await Manager(maxSkillCount: 1).InitializeAsync(default);
        Write("second", "Second", "Body");
        await Assert.ThrowsAsync<InvalidDataException>(() => Manager(maxSkillCount: 1).InitializeAsync(default));
    }

    [Fact]
    public async Task MissingSourcesAndMalformedUtf8_FailExplicitly()
    {
        var missing = new LocalSkillManager(Path.Combine(_root, "missing"), 65536, 100,
            NullLogger<LocalSkillManager>.Instance);
        await Assert.ThrowsAnyAsync<IOException>(() => missing.InitializeAsync(default));
        var directory = Directory.CreateDirectory(Path.Combine(_root, "analysis")).FullName;
        await Assert.ThrowsAsync<FileNotFoundException>(() => Manager().InitializeAsync(default));
        await File.WriteAllBytesAsync(Path.Combine(directory, "SKILL.md"), [0xff, 0xff]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => Manager().InitializeAsync(default));
    }

    [Fact]
    public async Task UnconfiguredSourceIsEmpty_AndCancellationIsPropagated()
    {
        var manager = new LocalSkillManager(null, 65536, 100, NullLogger<LocalSkillManager>.Instance);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InitializeAsync(cancelled.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ListDefinitionsAsync(default));
        await manager.InitializeAsync(default);
        Assert.Empty(await manager.ListDefinitionsAsync(default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ListDefinitionsAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.LoadAsync("analysis", cancelled.Token));
    }

    [Fact]
    public async Task DirectoryJunction_IsRejectedWithoutReadingItsContents()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var catalog = Directory.CreateDirectory(Path.Combine(_root, "catalog")).FullName;
        var junction = Path.Combine(catalog, "linked");
        var startInfo = new System.Diagnostics.ProcessStartInfo(
            Environment.GetEnvironmentVariable("ComSpec") ?? throw new InvalidOperationException("ComSpec is missing."))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{junction}\" \"{target}\""
        };
        using var process = System.Diagnostics.Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start junction fixture command.");
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
        try
        {
            var manager = new LocalSkillManager(catalog, 65536, 100, NullLogger<LocalSkillManager>.Instance);
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.InitializeAsync(default));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task Host_InitializesCatalogAndResolvesRelativePathFromContentRoot()
    {
        Write("analysis", "Test", "Instructions");
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Skills:Directory"] = Path.GetRelativePath(builder.Environment.ContentRootPath, _root)
        });
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        var skills = host.Services.GetRequiredService<ISkillManager>();
        Assert.Equal("Instructions", (await skills.LoadAsync("analysis", timeout.Token)).Instructions);
        await host.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Host_RejectsInvalidCatalogAtStartup()
    {
        Write("analysis", "Test", "");
        var builder = ApplicationHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Skills:Directory"] = _root });
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidDataException>(() => host.StartAsync(timeout.Token));
    }

    private LocalSkillManager Manager(int maxFileBytes = 65536, int maxSkillCount = 100) =>
        new(_root, maxFileBytes, maxSkillCount, NullLogger<LocalSkillManager>.Instance);

    private void Write(string name, string description, string body)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: {description}\n---\n{body}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
