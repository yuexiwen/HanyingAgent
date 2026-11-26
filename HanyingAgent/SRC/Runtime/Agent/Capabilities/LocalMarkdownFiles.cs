using System.Text;
using YamlDotNet.RepresentationModel;

namespace HanyingAgent.Runtime.Agent.Capabilities;

internal static class LocalMarkdownFiles
{
    public static async Task<string> ReadBoundedAsync(
        string path, int maxFileBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        if (stream.Length > maxFileBytes)
        {
            throw new InvalidDataException("Markdown file exceeds the configured byte limit.");
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(4096, maxFileBytes + 1)];
        while (true)
        {
            var read = await stream.ReadAsync(
                chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxFileBytes - buffer.Length + 1)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maxFileBytes)
            {
                throw new InvalidDataException("Markdown file exceeds the configured byte limit.");
            }
            buffer.Write(chunk, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length)
            .TrimStart('\uFEFF');
    }

    public static (YamlMappingNode Metadata, string Body) Parse(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != "---")
        {
            throw new InvalidDataException("Markdown document must start with YAML front matter.");
        }
        StringBuilder header = new();
        string? line;
        while ((line = reader.ReadLine()) is not null && line != "---")
        {
            header.AppendLine(line);
        }
        if (line is null)
        {
            throw new InvalidDataException("Markdown front matter is not terminated.");
        }
        var yaml = new YamlStream();
        yaml.Load(new StringReader(header.ToString()));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            throw new InvalidDataException("Markdown front matter must contain one YAML mapping.");
        }
        return (mapping, reader.ReadToEnd());
    }

    public static void RejectLinkedPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Document paths must not contain symbolic links or directory junctions.");
            }
        }
    }
}
