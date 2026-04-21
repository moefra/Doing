namespace Hakaze.Build.IO;

public sealed class DPath : IEquatable<DPath>
{
    public DPath(string path)
        : this(path, normalizeToFullPath: true)
    {
    }

    public DPath(FileSystemInfo info)
        : this(info?.FullName ?? throw new ArgumentNullException(nameof(info)))
    {
    }

    private DPath(string path, bool normalizeToFullPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        Value = normalizeToFullPath
            ? NormalizeFullPath(path)
            : NormalizePath(path);
    }

    public string Value { get; }

    public DPath Parent
    {
        get
        {
            var root = Path.GetPathRoot(Value);
            if (!string.IsNullOrEmpty(root) && string.Equals(Value, root, StringComparison.Ordinal))
            {
                return this;
            }

            var parent = Path.GetDirectoryName(Value);
            if (string.IsNullOrEmpty(parent))
            {
                return this;
            }

            return new DPath(parent, normalizeToFullPath: Path.IsPathRooted(parent));
        }
    }

    public string FileName => Path.GetFileName(Value);

    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(Value);

    public string Extension => Path.GetExtension(Value);

    public DPath Root
    {
        get
        {
            var root = Path.GetPathRoot(Value);
            return string.IsNullOrEmpty(root)
                ? this
                : new DPath(root, normalizeToFullPath: true);
        }
    }

    public bool IsPathRooted => Path.IsPathRooted(Value);

    public bool HasExtension => Path.HasExtension(Value);

    public bool Exists => File.Exists(Value) || Directory.Exists(Value);

    public bool IsFile => File.Exists(Value);

    public bool IsDirectory => Directory.Exists(Value);

    public static implicit operator DPath(string path) => new(path);

    public static implicit operator string(DPath path) => path.Value;

    public static DPath operator /(DPath left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Combine(right);
    }

    public static DPath operator /(DPath left, DPath right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.Combine(right);
    }

    public static bool operator ==(DPath? left, DPath? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(DPath? left, DPath? right) => !(left == right);

    public DPath ChangeExtension(string? extension) => new(Path.ChangeExtension(Value, extension) ?? Value);

    public DPath GetRelativePathTo(DPath destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        // Preserve the relative result instead of resolving it against the current working directory.
        return new DPath(Path.GetRelativePath(Value, destination.Value), normalizeToFullPath: false);
    }

    public DPath Combine(string right)
    {
        ArgumentNullException.ThrowIfNull(right);
        return new DPath(Path.Combine(Value, right));
    }

    public DPath Combine(DPath right)
    {
        ArgumentNullException.ThrowIfNull(right);
        return Combine(right.Value);
    }

    public bool Equals(DPath? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DPath other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    private static string NormalizeFullPath(string path) => NormalizePath(Path.GetFullPath(path));

    private static string NormalizePath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.Ordinal))
        {
            return root;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
