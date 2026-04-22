// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using IOPath = global::System.IO.Path;

namespace Doing.IO.Tests;

public class FileSystemTests
{
    [Test]
    public async Task IsPathCaseSensitive_ForExistingDirectory_MatchesObservedBehavior()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            bool expected = ObserveDirectoryCaseSensitivity(tempRoot);

            bool actual = FileSystem.IsPathCaseSensitive(tempRoot);

            await Assert.That(actual).IsEqualTo(expected);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task IsPathCaseSensitive_ForExistingFile_UsesContainingDirectoryBehavior()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string filePath = IOPath.Combine(tempRoot,"sample.txt");
            File.WriteAllText(filePath,"payload");

            bool expected = ObserveDirectoryCaseSensitivity(tempRoot);

            bool actual = FileSystem.IsPathCaseSensitive(filePath);

            await Assert.That(actual).IsEqualTo(expected);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task IsPathCaseSensitive_ForMissingDescendant_UsesNearestExistingAncestor()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string missingPath = IOPath.Combine(tempRoot,"alpha","beta","gamma.txt");
            bool expected = ObserveDirectoryCaseSensitivity(tempRoot);

            bool actual = FileSystem.IsPathCaseSensitive(missingPath);

            await Assert.That(actual).IsEqualTo(expected);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task IsPathCaseSensitive_ForRelativePath_UsesCurrentDirectoryResolution()
    {
        string tempRoot = CreateTempDirectory();
        string originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempRoot;
            bool expected = ObserveDirectoryCaseSensitivity(tempRoot);

            bool actual = FileSystem.IsPathCaseSensitive(IOPath.Combine("alpha","beta.txt"));

            await Assert.That(actual).IsEqualTo(expected);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task IsPathCaseSensitive_NullPath_ThrowsArgumentNullException()
    {
        Exception? exception = CaptureException(() => FileSystem.IsPathCaseSensitive(null!));

        await Assert.That(exception).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task IsPathCaseSensitive_UnresolvableWindowsDrive_ThrowsDirectoryNotFoundException()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? missingRoot = GetMissingWindowsRoot();

        if (missingRoot is null)
        {
            return;
        }

        Exception? exception = CaptureException(() => FileSystem.IsPathCaseSensitive(IOPath.Combine(missingRoot,"alpha","beta.txt")));

        await Assert.That(exception).IsTypeOf<DirectoryNotFoundException>();
    }

    private static bool ObserveDirectoryCaseSensitivity(string directoryPath)
    {
        string probeName = $"TEST_CASE_PROBE_{Guid.NewGuid():N}.tmp";
        string probePath = IOPath.Combine(directoryPath,probeName);
        string alternateProbePath = IOPath.Combine(directoryPath,probeName.ToLowerInvariant());

        try
        {
            using (File.Create(probePath))
            {
            }

            return !File.Exists(alternateProbePath);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        string path = IOPath.Combine(IOPath.GetTempPath(),$"doing-file-system-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path,true);
        }
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string? GetMissingWindowsRoot()
    {
        HashSet<char> usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        for (char driveLetter = 'Z'; driveLetter >= 'A'; driveLetter--)
        {
            if (!usedLetters.Contains(driveLetter))
            {
                return $"{driveLetter}:{IOPath.DirectorySeparatorChar}";
            }
        }

        return null;
    }
}
