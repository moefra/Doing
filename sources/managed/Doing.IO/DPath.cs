// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tomlyn;
using YamlDotNet.Serialization;

namespace Doing.IO;

public sealed record class DPath
{
    private static readonly StringComparer PathComparer =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer YamlSerializer = new SerializerBuilder().Build();

    public DPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = NormalizePath(value);
    }

    public string Value { get; }

    public string Name => Path.GetFileName(Value);

    public string Stem => Path.GetFileNameWithoutExtension(Name);

    public string Extension => Path.GetExtension(Value);

    public DPath? Parent
    {
        get
        {
            string? parent = Path.GetDirectoryName(Value);
            return string.IsNullOrEmpty(parent) ? null : new DPath(parent);
        }
    }

    public bool IsAbsolute => Path.IsPathFullyQualified(Value);

    public bool Exists => File.Exists(Value) || Directory.Exists(Value);

    public bool IsFile => File.Exists(Value);

    public bool IsDirectory => Directory.Exists(Value);

    public static implicit operator DPath(string value) => new(value);

    public static implicit operator string(DPath dPath) => dPath.Value;

    public DPath Combine(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments.Length == 0
            ? this
            : new DPath(Path.Combine([Value,..segments]));
    }

    public DPath Combine(params DPath[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments.Length == 0
            ? this
            : new DPath(Path.Combine([Value,..segments.Select(segment => segment.Value)]));
    }

    public DPath ChangeExtension(string? extension)
    {
        return new DPath(Path.ChangeExtension(Value,extension) ?? string.Empty);
    }

    public DPath RelativeTo(DPath baseDPath)
    {
        ArgumentNullException.ThrowIfNull(baseDPath);

        return new DPath(Path.GetRelativePath(baseDPath.Value,Value));
    }

    public DPath CreateDirectory()
    {
        string targetDirectory = LooksLikeFilePath(Value)
            ? Parent?.Value ?? "."
            : Value;

        Directory.CreateDirectory(targetDirectory);
        return this;
    }

    public async Task<DPath> TouchAsync()
    {
        EnsureParentDirectory();
        await File.Create(Value).DisposeAsync();
        return this;
    }

    public DPath EnsureParentDirectory()
    {
        _ = Parent?.CreateDirectory();
        return this;
    }

    public string ReadText(Encoding? encoding = null)
    {
        return File.ReadAllText(Value,encoding ?? Encoding.UTF8);
    }

    public Task<string> ReadTextAsync(Encoding? encoding = null,CancellationToken cancellationToken = default)
    {
        return File.ReadAllTextAsync(Value,encoding ?? Encoding.UTF8,cancellationToken);
    }

    public DPath WriteText(string content,Encoding? encoding = null)
    {
        EnsureParentDirectory();
        File.WriteAllText(Value,content,encoding ?? Encoding.UTF8);
        return this;
    }

    public async Task<DPath> WriteTextAsync(string content,Encoding? encoding = null,CancellationToken cancellationToken = default)
    {
        EnsureParentDirectory();
        await File.WriteAllTextAsync(Value,content,encoding ?? Encoding.UTF8,cancellationToken);
        return this;
    }

    public byte[] ReadBytes()
    {
        return File.ReadAllBytes(Value);
    }

    public Task<byte[]> ReadBytesAsync(CancellationToken cancellationToken = default)
    {
        return File.ReadAllBytesAsync(Value,cancellationToken);
    }

    public DPath WriteBytes(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        EnsureParentDirectory();
        File.WriteAllBytes(Value,content);
        return this;
    }

    public async Task<DPath> WriteBytesAsync(byte[] content,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        EnsureParentDirectory();
        await File.WriteAllBytesAsync(Value,content,cancellationToken);
        return this;
    }

    public void Delete(bool recursive = false)
    {
        if (File.Exists(Value))
        {
            File.Delete(Value);
            return;
        }

        Directory.Delete(Value,recursive);
    }

    public DPath CopyTo(DPath destination,bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (IsDirectory)
        {
            CopyDirectory(Value,destination.Value,overwrite);
            return destination;
        }

        destination.EnsureParentDirectory();
        File.Copy(Value,destination.Value,overwrite);
        return destination;
    }

    public DPath MoveTo(DPath destination,bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (IsDirectory)
        {
            destination.EnsureParentDirectory();

            if (destination.Exists)
            {
                if (!overwrite)
                {
                    throw new IOException($"destination already exists: {destination.Value}");
                }

                destination.Delete(true);
            }

            Directory.Move(Value,destination.Value);
            return destination;
        }

        destination.EnsureParentDirectory();
        File.Move(Value,destination.Value,overwrite);
        return destination;
    }

    public T ReadJsonFile<T>(JsonSerializerOptions? options = null)
    {
        string content = ReadText();
        return JsonSerializer.Deserialize<T>(content,options ?? DefaultJsonSerializerOptions())!;
    }

    public async Task<T> ReadJsonFileAsync<T>(JsonSerializerOptions? options = null,CancellationToken cancellationToken = default)
    {
        string content = await ReadTextAsync(cancellationToken: cancellationToken);
        return JsonSerializer.Deserialize<T>(content,options ?? DefaultJsonSerializerOptions())!;
    }

    public DPath WriteJsonFile<T>(T value,JsonSerializerOptions? options = null,bool indented = true)
    {
        string content = JsonSerializer.Serialize(value,CreateJsonSerializerOptions(options,indented));
        return WriteText(content);
    }

    public Task<DPath> WriteJsonFileAsync<T>(T value,JsonSerializerOptions? options = null,bool indented = true,CancellationToken cancellationToken = default)
    {
        string content = JsonSerializer.Serialize(value,CreateJsonSerializerOptions(options,indented));
        return WriteTextAsync(content,cancellationToken: cancellationToken);
    }

    public T ReadYamlFile<T>()
    {
        return YamlDeserializer.Deserialize<T>(ReadText())!;
    }

    public async Task<T> ReadYamlFileAsync<T>(CancellationToken cancellationToken = default)
    {
        string content = await ReadTextAsync(cancellationToken: cancellationToken);
        return YamlDeserializer.Deserialize<T>(content)!;
    }

    public DPath WriteYamlFile<T>(T value)
    {
        return WriteText(YamlSerializer.Serialize(value));
    }

    public Task<DPath> WriteYamlFileAsync<T>(T value,CancellationToken cancellationToken = default)
    {
        return WriteTextAsync(YamlSerializer.Serialize(value),cancellationToken: cancellationToken);
    }

    public T ReadTomlFile<T>()
    {
        return TomlSerializer.Deserialize<T>(ReadText())!;
    }

    public async Task<T> ReadTomlFileAsync<T>(CancellationToken cancellationToken = default)
    {
        string content = await ReadTextAsync(cancellationToken: cancellationToken);
        return TomlSerializer.Deserialize<T>(content)!;
    }

    public DPath WriteTomlFile<T>(T value)
    {
        return WriteText(TomlSerializer.Serialize(value));
    }

    public Task<DPath> WriteTomlFileAsync<T>(T value,CancellationToken cancellationToken = default)
    {
        return WriteTextAsync(TomlSerializer.Serialize(value),cancellationToken: cancellationToken);
    }

    public bool Equals(DPath? other)
    {
        return other is not null && PathComparer.Equals(Value,other.Value);
    }

    public override int GetHashCode()
    {
        return PathComparer.GetHashCode(Value);
    }

    public override string ToString() => Value;

    private static JsonSerializerOptions CreateJsonSerializerOptions(JsonSerializerOptions? options,bool indented)
    {
        JsonSerializerOptions serializerOptions = options is null
            ? DefaultJsonSerializerOptions()
            : new JsonSerializerOptions(options);

        serializerOptions.WriteIndented = indented;
        return serializerOptions;
    }

    private static JsonSerializerOptions DefaultJsonSerializerOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.General);
    }

    private static string NormalizePath(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        char separator = Path.DirectorySeparatorChar;
        string unified = value
            .Replace('\\',separator)
            .Replace('/',separator);

        string root = Path.GetPathRoot(unified) ?? string.Empty;
        string remainder = unified[root.Length..];
        List<string> normalizedSegments = [];

        foreach (string rawSegment in remainder.Split(separator,StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawSegment == ".")
            {
                continue;
            }

            if (rawSegment == "..")
            {
                if (normalizedSegments.Count > 0 && normalizedSegments[^1] != "..")
                {
                    normalizedSegments.RemoveAt(normalizedSegments.Count - 1);
                    continue;
                }

                if (root.Length == 0)
                {
                    normalizedSegments.Add(rawSegment);
                }

                continue;
            }

            normalizedSegments.Add(rawSegment);
        }

        string normalizedRemainder = string.Join(separator,normalizedSegments);

        if (root.Length > 0)
        {
            if (normalizedRemainder.Length == 0)
            {
                return root;
            }

            string normalizedRoot = root.EndsWith(separator)
                ? root[..^1]
                : root;

            if (normalizedRoot.Length == 0)
            {
                return $"{separator}{normalizedRemainder}";
            }

            return $"{normalizedRoot}{separator}{normalizedRemainder}";
        }

        return normalizedRemainder.Length == 0 ? "." : normalizedRemainder;
    }

    private static bool LooksLikeFilePath(string value)
    {
        if (value == ".")
        {
            return false;
        }

        return !string.IsNullOrEmpty(Path.GetExtension(value));
    }

    private static void CopyDirectory(string source,string destination,bool overwrite)
    {
        if (Directory.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException($"destination already exists: {destination}");
            }

            Directory.Delete(destination,true);
        }

        Directory.CreateDirectory(destination);

        foreach (string directory in Directory.GetDirectories(source,"*",SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(source,directory);
            Directory.CreateDirectory(Path.Combine(destination,relativePath));
        }

        foreach (string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(source,file);
            string destinationFile = Path.Combine(destination,relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file,destinationFile,overwrite: true);
        }
    }
}
