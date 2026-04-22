// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tomlyn;
using YamlDotNet.Serialization;

namespace Doing.IO;

/// <summary>
/// Represents a normalized filesystem path and provides convenience helpers for common file and directory operations.
/// </summary>
public sealed record class DPath
{
    private static readonly StringComparer PathComparer =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer YamlSerializer = new SerializerBuilder().Build();

    /// <summary>
    /// Initializes a new <see cref="DPath"/> from the supplied path string.
    /// </summary>
    /// <param name="value">The raw path value to normalize.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public DPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = NormalizePath(value);
    }

    /// <summary>
    /// Gets the normalized path string represented by this instance.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the final path segment, including its extension when present.
    /// </summary>
    public string Name => Path.GetFileName(Value);

    /// <summary>
    /// Gets the file name without its extension.
    /// </summary>
    public string Stem => Path.GetFileNameWithoutExtension(Name);

    /// <summary>
    /// Gets the file extension for the current path.
    /// </summary>
    public string Extension => Path.GetExtension(Value);

    /// <summary>
    /// Gets the parent directory, or <see langword="null"/> when the path has no parent.
    /// </summary>
    public DPath? Parent
    {
        get
        {
            string? parent = Path.GetDirectoryName(Value);
            return string.IsNullOrEmpty(parent) ? null : new DPath(parent);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the current path is fully qualified.
    /// </summary>
    public bool IsAbsolute => Path.IsPathFullyQualified(Value);

    /// <summary>
    /// Gets a value indicating whether a file or directory currently exists at this path.
    /// </summary>
    public bool Exists => File.Exists(Value) || Directory.Exists(Value);

    /// <summary>
    /// Gets a value indicating whether this path currently points to an existing file.
    /// </summary>
    public bool IsFile => File.Exists(Value);

    /// <summary>
    /// Gets a value indicating whether this path currently points to an existing directory.
    /// </summary>
    public bool IsDirectory => Directory.Exists(Value);

    /// <summary>
    /// Converts a path string into a normalized <see cref="DPath"/>.
    /// </summary>
    /// <param name="value">The raw path value to normalize.</param>
    public static implicit operator DPath(string value) => new(value);

    /// <summary>
    /// Converts a <see cref="DPath"/> into its normalized string representation.
    /// </summary>
    /// <param name="dPath">The path instance to unwrap.</param>
    public static implicit operator string(DPath dPath) => dPath.Value;

    /// <summary>
    /// Appends string path segments to the current path.
    /// </summary>
    /// <param name="segments">The path segments to combine after the current value.</param>
    /// <returns>A new <see cref="DPath"/> containing the combined path, or the current instance when no segments are supplied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments"/> is <see langword="null"/>.</exception>
    public DPath Combine(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments.Length == 0
            ? this
            : new DPath(Path.Combine([Value,..segments]));
    }

    /// <summary>
    /// Appends <see cref="DPath"/> segments to the current path.
    /// </summary>
    /// <param name="segments">The path segments to combine after the current value.</param>
    /// <returns>A new <see cref="DPath"/> containing the combined path, or the current instance when no segments are supplied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments"/> is <see langword="null"/>.</exception>
    public DPath Combine(params DPath[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments.Length == 0
            ? this
            : new DPath(Path.Combine([Value,..segments.Select(segment => segment.Value)]));
    }

    /// <summary>
    /// Returns a new path with a different file extension.
    /// </summary>
    /// <param name="extension">The new extension, with or without a leading period, or <see langword="null"/> to remove the extension.</param>
    /// <returns>A new <see cref="DPath"/> with the updated extension.</returns>
    public DPath ChangeExtension(string? extension)
    {
        return new DPath(Path.ChangeExtension(Value,extension) ?? string.Empty);
    }

    /// <summary>
    /// Computes the relative path from a base path to the current path.
    /// </summary>
    /// <param name="baseDPath">The base path used to calculate the relative path.</param>
    /// <returns>A new <see cref="DPath"/> representing the relative path from <paramref name="baseDPath"/> to this instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseDPath"/> is <see langword="null"/>.</exception>
    public DPath RelativeTo(DPath baseDPath)
    {
        ArgumentNullException.ThrowIfNull(baseDPath);

        return new DPath(Path.GetRelativePath(baseDPath.Value,Value));
    }

    /// <summary>
    /// Creates the directory represented by this path, or the parent directory when the path appears to target a file.
    /// </summary>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public DPath CreateDirectory()
    {
        string targetDirectory = LooksLikeFilePath(Value)
            ? Parent?.Value ?? "."
            : Value;

        Directory.CreateDirectory(targetDirectory);
        return this;
    }

    /// <summary>
    /// Creates an empty file at the current path after ensuring the parent directory exists.
    /// </summary>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public async Task<DPath> TouchAsync()
    {
        EnsureParentDirectory();
        await File.Create(Value).DisposeAsync();
        return this;
    }

    /// <summary>
    /// Ensures the parent directory exists when the current path has one.
    /// </summary>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public DPath EnsureParentDirectory()
    {
        _ = Parent?.CreateDirectory();
        return this;
    }

    /// <summary>
    /// Reads the entire file as text.
    /// </summary>
    /// <param name="encoding">The text encoding to use. UTF-8 is used when omitted.</param>
    /// <returns>The file contents as a string.</returns>
    public string ReadText(Encoding? encoding = null)
    {
        return File.ReadAllText(Value,encoding ?? Encoding.UTF8);
    }

    /// <summary>
    /// Asynchronously reads the entire file as text.
    /// </summary>
    /// <param name="encoding">The text encoding to use. UTF-8 is used when omitted.</param>
    /// <param name="cancellationToken">A token used to cancel the read operation.</param>
    /// <returns>A task that resolves to the file contents.</returns>
    public Task<string> ReadTextAsync(Encoding? encoding = null,CancellationToken cancellationToken = default)
    {
        return File.ReadAllTextAsync(Value,encoding ?? Encoding.UTF8,cancellationToken);
    }

    /// <summary>
    /// Writes text to the current path after ensuring the parent directory exists.
    /// </summary>
    /// <param name="content">The text content to write.</param>
    /// <param name="encoding">The text encoding to use. UTF-8 is used when omitted.</param>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public DPath WriteText(string content,Encoding? encoding = null)
    {
        EnsureParentDirectory();
        File.WriteAllText(Value,content,encoding ?? Encoding.UTF8);
        return this;
    }

    /// <summary>
    /// Asynchronously writes text to the current path after ensuring the parent directory exists.
    /// </summary>
    /// <param name="content">The text content to write.</param>
    /// <param name="encoding">The text encoding to use. UTF-8 is used when omitted.</param>
    /// <param name="cancellationToken">A token used to cancel the write operation.</param>
    /// <returns>A task that resolves to the current <see cref="DPath"/> instance.</returns>
    public async Task<DPath> WriteTextAsync(string content,Encoding? encoding = null,CancellationToken cancellationToken = default)
    {
        EnsureParentDirectory();
        await File.WriteAllTextAsync(Value,content,encoding ?? Encoding.UTF8,cancellationToken);
        return this;
    }

    /// <summary>
    /// Reads the entire file as a byte array.
    /// </summary>
    /// <returns>The file contents.</returns>
    public byte[] ReadBytes()
    {
        return File.ReadAllBytes(Value);
    }

    /// <summary>
    /// Asynchronously reads the entire file as a byte array.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the read operation.</param>
    /// <returns>A task that resolves to the file contents.</returns>
    public Task<byte[]> ReadBytesAsync(CancellationToken cancellationToken = default)
    {
        return File.ReadAllBytesAsync(Value,cancellationToken);
    }

    /// <summary>
    /// Writes binary content to the current path after ensuring the parent directory exists.
    /// </summary>
    /// <param name="content">The binary content to write.</param>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
    public DPath WriteBytes(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        EnsureParentDirectory();
        File.WriteAllBytes(Value,content);
        return this;
    }

    /// <summary>
    /// Asynchronously writes binary content to the current path after ensuring the parent directory exists.
    /// </summary>
    /// <param name="content">The binary content to write.</param>
    /// <param name="cancellationToken">A token used to cancel the write operation.</param>
    /// <returns>A task that resolves to the current <see cref="DPath"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
    public async Task<DPath> WriteBytesAsync(byte[] content,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        EnsureParentDirectory();
        await File.WriteAllBytesAsync(Value,content,cancellationToken);
        return this;
    }

    /// <summary>
    /// Deletes the file or directory at the current path.
    /// </summary>
    /// <param name="recursive"><see langword="true"/> to delete directory contents recursively when the path points to a directory.</param>
    public void Delete(bool recursive = false)
    {
        if (File.Exists(Value))
        {
            File.Delete(Value);
            return;
        }

        Directory.Delete(Value,recursive);
    }

    /// <summary>
    /// Copies the current file or directory to a destination path.
    /// </summary>
    /// <param name="destination">The destination path.</param>
    /// <param name="overwrite"><see langword="true"/> to replace an existing destination.</param>
    /// <returns>The destination path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Moves the current file or directory to a destination path.
    /// </summary>
    /// <param name="destination">The destination path.</param>
    /// <param name="overwrite"><see langword="true"/> to replace an existing destination.</param>
    /// <returns>The destination path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Reads the current file as JSON and deserializes it into the requested type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <param name="options">Optional serializer settings. Default JSON options are used when omitted.</param>
    /// <returns>The deserialized value.</returns>
    public T ReadJsonFile<T>(JsonSerializerOptions? options = null)
    {
        string content = ReadText();
        return JsonSerializer.Deserialize<T>(content,options ?? DefaultJsonSerializerOptions())!;
    }

    /// <summary>
    /// Asynchronously reads the current file as JSON and deserializes it into the requested type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <param name="options">Optional serializer settings. Default JSON options are used when omitted.</param>
    /// <param name="cancellationToken">A token used to cancel the read operation.</param>
    /// <returns>A task that resolves to the deserialized value.</returns>
    public async Task<T> ReadJsonFileAsync<T>(JsonSerializerOptions? options = null,CancellationToken cancellationToken = default)
    {
        string content = await ReadTextAsync(cancellationToken: cancellationToken);
        return JsonSerializer.Deserialize<T>(content,options ?? DefaultJsonSerializerOptions())!;
    }

    /// <summary>
    /// Serializes a value as JSON and writes it to the current path.
    /// </summary>
    /// <typeparam name="T">The type of value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">Optional serializer settings copied before writing. Default JSON options are used when omitted.</param>
    /// <param name="indented"><see langword="true"/> to format the JSON with indentation.</param>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public DPath WriteJsonFile<T>(T value,JsonSerializerOptions? options = null,bool indented = true)
    {
        string content = JsonSerializer.Serialize(value,CreateJsonSerializerOptions(options,indented));
        return WriteText(content);
    }

    /// <summary>
    /// Asynchronously serializes a value as JSON and writes it to the current path.
    /// </summary>
    /// <typeparam name="T">The type of value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">Optional serializer settings copied before writing. Default JSON options are used when omitted.</param>
    /// <param name="indented"><see langword="true"/> to format the JSON with indentation.</param>
    /// <param name="cancellationToken">A token used to cancel the write operation.</param>
    /// <returns>A task that resolves to the current <see cref="DPath"/> instance.</returns>
    public Task<DPath> WriteJsonFileAsync<T>(T value,JsonSerializerOptions? options = null,bool indented = true,CancellationToken cancellationToken = default)
    {
        string content = JsonSerializer.Serialize(value,CreateJsonSerializerOptions(options,indented));
        return WriteTextAsync(content,cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads the current file as YAML and deserializes it into the requested type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <returns>The deserialized value.</returns>
    public T ReadYamlFile<T>()
    {
        return YamlDeserializer.Deserialize<T>(ReadText())!;
    }

    /// <summary>
    /// Asynchronously reads the current file as YAML and deserializes it into the requested type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <param name="cancellationToken">A token used to cancel the read operation.</param>
    /// <returns>A task that resolves to the deserialized value.</returns>
    public async Task<T> ReadYamlFileAsync<T>(CancellationToken cancellationToken = default)
    {
        string content = await ReadTextAsync(cancellationToken: cancellationToken);
        return YamlDeserializer.Deserialize<T>(content)!;
    }

    /// <summary>
    /// Serializes a value as YAML and writes it to the current path.
    /// </summary>
    /// <typeparam name="T">The type of value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public DPath WriteYamlFile<T>(T value)
    {
        return WriteText(YamlSerializer.Serialize(value));
    }

    /// <summary>
    /// Asynchronously serializes a value as YAML and writes it to the current path.
    /// </summary>
    /// <typeparam name="T">The type of value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="cancellationToken">A token used to cancel the write operation.</param>
    /// <returns>A task that resolves to the current <see cref="DPath"/> instance.</returns>
    public Task<DPath> WriteYamlFileAsync<T>(T value,CancellationToken cancellationToken = default)
    {
        return WriteTextAsync(YamlSerializer.Serialize(value),cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads the current file as TOML and deserializes it into the requested type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <returns>The deserialized value.</returns>
    public T ReadTomlFile<T>()
    {
        return TomlSerializer.Deserialize<T>(ReadText())!;
    }

    /// <summary>
    /// Asynchronously reads the current file as TOML and deserializes it into the requested type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <param name="cancellationToken">A token used to cancel the read operation.</param>
    /// <returns>A task that resolves to the deserialized value.</returns>
    public async Task<T> ReadTomlFileAsync<T>(CancellationToken cancellationToken = default)
    {
        string content = await ReadTextAsync(cancellationToken: cancellationToken);
        return TomlSerializer.Deserialize<T>(content)!;
    }

    /// <summary>
    /// Serializes a value as TOML and writes it to the current path.
    /// </summary>
    /// <typeparam name="T">The type of value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The current <see cref="DPath"/> instance.</returns>
    public DPath WriteTomlFile<T>(T value)
    {
        return WriteText(TomlSerializer.Serialize(value));
    }

    /// <summary>
    /// Asynchronously serializes a value as TOML and writes it to the current path.
    /// </summary>
    /// <typeparam name="T">The type of value being serialized.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="cancellationToken">A token used to cancel the write operation.</param>
    /// <returns>A task that resolves to the current <see cref="DPath"/> instance.</returns>
    public Task<DPath> WriteTomlFileAsync<T>(T value,CancellationToken cancellationToken = default)
    {
        return WriteTextAsync(TomlSerializer.Serialize(value),cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Determines whether another path is equal to the current path using the platform-specific path comparer.
    /// </summary>
    /// <param name="other">The other path to compare.</param>
    /// <returns><see langword="true"/> when both paths normalize to the same value for the current platform; otherwise <see langword="false"/>.</returns>
    public bool Equals(DPath? other)
    {
        return other is not null && PathComparer.Equals(Value,other.Value);
    }

    /// <summary>
    /// Returns a hash code computed with the platform-specific path comparer.
    /// </summary>
    /// <returns>A hash code for the current path.</returns>
    public override int GetHashCode()
    {
        return PathComparer.GetHashCode(Value);
    }

    /// <summary>
    /// Returns the normalized path string.
    /// </summary>
    /// <returns>The normalized path represented by this instance.</returns>
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
