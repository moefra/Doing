using Hakaze.Build.IO;

namespace Hakaze.Build.IO.Tests;

public class DPathTests
{
    [Test]
    public void StringConstructor_AndImplicitStringConversion_RoundTripNormalizedAbsolutePath()
    {
        DPath path = Path.Combine("alpha", "beta");
        string actual = path;
        var expected = NormalizeAbsolute(Path.Combine("alpha", "beta"));

        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    [Test]
    public void FileSystemInfoConstructor_UsesFullName()
    {
        var directoryPath = CreateUniqueTempPath();
        Directory.CreateDirectory(directoryPath);

        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            var path = new DPath(directoryInfo);
            var expected = NormalizeAbsolute(directoryInfo.FullName);

            if (path.Value != expected)
            {
                throw new InvalidOperationException($"Expected '{expected}', got '{path.Value}'.");
            }
        }
        finally
        {
            DeleteIfExists(directoryPath);
        }
    }

    [Test]
    public void RelativePathConstructor_NormalizesToAbsolutePath()
    {
        var path = new DPath(".");
        var expected = NormalizeAbsolute(".");

        if (path.Value != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{path.Value}'.");
        }
    }

    [Test]
    public void NonRootPath_RemovesTrailingDirectorySeparator()
    {
        var pathWithoutSeparator = CreateUniqueTempPath();
        var pathWithSeparator = pathWithoutSeparator + Path.DirectorySeparatorChar;
        var path = new DPath(pathWithSeparator);
        var expected = NormalizeAbsolute(pathWithoutSeparator);

        if (path.Value != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{path.Value}'.");
        }
    }

    [Test]
    public void RootPath_PreservesRootAndReturnsItselfAsParent()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))
                   ?? throw new InvalidOperationException("Expected a valid filesystem root.");
        var path = new DPath(root);

        if (path.Value != root)
        {
            throw new InvalidOperationException($"Expected root '{root}', got '{path.Value}'.");
        }

        if (path.Parent != path)
        {
            throw new InvalidOperationException("Expected root parent to return itself.");
        }
    }

    [Test]
    public void SlashOperator_ChainsPathSegments()
    {
        DPath path = Path.Combine("alpha", "beta");
        var actual = path / "foo" / "bar";
        var expected = NormalizeAbsolute(Path.Combine(path.Value, "foo", "bar"));

        if (actual.Value != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual.Value}'.");
        }
    }

    [Test]
    public void SlashOperator_UsesPathCombineSemanticsForAbsoluteRightSide()
    {
        DPath left = Path.Combine("alpha", "beta");
        DPath right = CreateUniqueTempPath();
        var actual = left / right;
        var expected = NormalizeAbsolute(Path.Combine(left.Value, right.Value));

        if (actual.Value != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual.Value}'.");
        }
    }

    [Test]
    public void PathMembers_ReturnExpectedValues()
    {
        var pathString = Path.Combine(CreateUniqueTempPath(), "nested", "file.tar.gz");
        var path = new DPath(pathString);
        var expectedParent = NormalizeAbsolute(Path.Combine(Path.GetDirectoryName(pathString) ?? string.Empty));
        var expectedRoot = Path.GetPathRoot(path.Value)
                           ?? throw new InvalidOperationException("Expected path root.");

        if (path.FileName != "file.tar.gz")
        {
            throw new InvalidOperationException($"Expected 'file.tar.gz', got '{path.FileName}'.");
        }

        if (path.FileNameWithoutExtension != "file.tar")
        {
            throw new InvalidOperationException($"Expected 'file.tar', got '{path.FileNameWithoutExtension}'.");
        }

        if (path.Extension != ".gz")
        {
            throw new InvalidOperationException($"Expected '.gz', got '{path.Extension}'.");
        }

        if (path.Parent.Value != expectedParent)
        {
            throw new InvalidOperationException($"Expected parent '{expectedParent}', got '{path.Parent.Value}'.");
        }

        if (path.Root.Value != expectedRoot)
        {
            throw new InvalidOperationException($"Expected root '{expectedRoot}', got '{path.Root.Value}'.");
        }

        if (!path.IsPathRooted)
        {
            throw new InvalidOperationException("Expected rooted path.");
        }

        if (!path.HasExtension)
        {
            throw new InvalidOperationException("Expected path to have an extension.");
        }
    }

    [Test]
    public void ChangeExtension_AndGetRelativePathTo_ReturnExpectedValues()
    {
        var basePath = CreateUniqueTempPath();
        var source = new DPath(Path.Combine(basePath, "source"));
        var destination = new DPath(Path.Combine(basePath, "target", "file.txt"));
        var changed = destination.ChangeExtension(".md");
        var relative = source.GetRelativePathTo(destination);
        var expectedChanged = NormalizeAbsolute(Path.ChangeExtension(destination.Value, ".md") ?? destination.Value);
        var expectedRelative = NormalizeRelative(Path.GetRelativePath(source.Value, destination.Value));

        if (changed.Value != expectedChanged)
        {
            throw new InvalidOperationException($"Expected '{expectedChanged}', got '{changed.Value}'.");
        }

        if (relative.Value != expectedRelative)
        {
            throw new InvalidOperationException($"Expected '{expectedRelative}', got '{relative.Value}'.");
        }
    }

    [Test]
    public void CombineOverloads_ReturnExpectedValues()
    {
        DPath left = Path.Combine("alpha", "beta");
        DPath right = "gamma";
        var combinedWithString = left.Combine("delta");
        var combinedWithPath = left.Combine(right);
        var expectedString = NormalizeAbsolute(Path.Combine(left.Value, "delta"));
        var expectedPath = NormalizeAbsolute(Path.Combine(left.Value, right.Value));

        if (combinedWithString.Value != expectedString)
        {
            throw new InvalidOperationException($"Expected '{expectedString}', got '{combinedWithString.Value}'.");
        }

        if (combinedWithPath.Value != expectedPath)
        {
            throw new InvalidOperationException($"Expected '{expectedPath}', got '{combinedWithPath.Value}'.");
        }
    }

    [Test]
    public void Exists_IsFile_AndIsDirectory_ReportFilesystemState()
    {
        var root = CreateUniqueTempPath();
        var directoryPath = Path.Combine(root, "folder");
        var filePath = Path.Combine(root, "folder", "file.txt");
        var missingPath = Path.Combine(root, "missing");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(filePath, "content");

        try
        {
            var directory = new DPath(directoryPath);
            var file = new DPath(filePath);
            var missing = new DPath(missingPath);

            if (!directory.Exists || !directory.IsDirectory || directory.IsFile)
            {
                throw new InvalidOperationException("Expected directory flags to reflect an existing directory.");
            }

            if (!file.Exists || !file.IsFile || file.IsDirectory)
            {
                throw new InvalidOperationException("Expected file flags to reflect an existing file.");
            }

            if (missing.Exists || missing.IsFile || missing.IsDirectory)
            {
                throw new InvalidOperationException("Expected missing path flags to report non-existence.");
            }
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Test]
    public void Equality_UsesNormalizedValue()
    {
        var pathString = CreateUniqueTempPath();
        var left = new DPath(pathString);
        var right = new DPath(pathString + Path.DirectorySeparatorChar);

        if (left != right)
        {
            throw new InvalidOperationException("Expected normalized paths to be equal.");
        }

        if (!left.Equals(right))
        {
            throw new InvalidOperationException("Expected Equals to use normalized values.");
        }

        if (left.GetHashCode() != right.GetHashCode())
        {
            throw new InvalidOperationException("Expected equal paths to share the same hash code.");
        }
    }

    [Test]
    public void NullStringInput_ThrowsArgumentNullException()
    {
        try
        {
            _ = new DPath((string)null!);
        }
        catch (ArgumentNullException)
        {
            return;
        }

        throw new InvalidOperationException("Expected ArgumentNullException for null string input.");
    }

    private static string CreateUniqueTempPath()
        => Path.Combine(Path.GetTempPath(), "Hakaze.Build.IO.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string NormalizeAbsolute(string path) => Normalize(Path.GetFullPath(path));

    private static string NormalizeRelative(string path) => Normalize(path);

    private static string Normalize(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.Ordinal))
        {
            return root;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
