// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Doing.IO;

/// <summary>
/// Helpful utilities for file system operation
/// </summary>
public static class FileSystem
{
    /// <summary>
    /// Check if a path is case-sensitive
    /// </summary>
    /// <param name="path">the path to check</param>
    /// <returns>true if the name of files and directories under the path is case-sensitive</returns>
    public static bool IsPathCaseSensitive(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string fullPath = Path.GetFullPath(path);
        string directoryPath = ResolveExistingDirectory(fullPath);

        return TryProbeDirectoryCaseSensitivity(directoryPath,out bool isCaseSensitive)
            ? isCaseSensitive
            : GetFallbackCaseSensitivity(directoryPath);
    }

    private static string ResolveExistingDirectory(string fullPath)
    {
        DirectoryInfo? directory = File.Exists(fullPath)
            ? new FileInfo(fullPath).Directory
            : new DirectoryInfo(fullPath);

        while (directory is not null)
        {
            if (directory.Exists)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate an existing directory for path '{fullPath}'.");
    }

    private static bool TryProbeDirectoryCaseSensitivity(string directoryPath,out bool isCaseSensitive)
    {
        string probeName = $"DOING_CASE_PROBE_{Guid.NewGuid():N}.tmp";
        string probePath = Path.Combine(directoryPath,probeName);
        string alternateProbePath = Path.Combine(directoryPath,probeName.ToLowerInvariant());

        try
        {
            using (File.Create(probePath))
            {
            }

            isCaseSensitive = !File.Exists(alternateProbePath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            TryDeleteFile(probePath);
        }

        isCaseSensitive = false;
        return false;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static bool GetFallbackCaseSensitivity(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsCaseSensitivity(directoryPath);
        }

        return OperatingSystem.IsMacOS() ? false : true;
    }

    private static bool GetWindowsCaseSensitivity(string directoryPath)
    {
        if (TryGetWindowsDirectoryCaseSensitivity(directoryPath,out bool isCaseSensitive))
        {
            return isCaseSensitive;
        }

        return GetWindowsVolumeCaseSensitivity(directoryPath);
    }

    private static bool TryGetWindowsDirectoryCaseSensitivity(string directoryPath,out bool isCaseSensitive)
    {
        if (NativeMethods.GetFileInformationByName(
                directoryPath,
                FILE_INFO_BY_NAME_CLASS.FileCaseSensitiveByNameInfo,
                out FILE_CASE_SENSITIVE_INFORMATION caseSensitiveInformation,
                (uint)Marshal.SizeOf<FILE_CASE_SENSITIVE_INFORMATION>()))
        {
            isCaseSensitive = (caseSensitiveInformation.Flags & FILE_CS_FLAG_CASE_SENSITIVE_DIR) != 0;
            return true;
        }

        isCaseSensitive = false;
        return false;
    }

    private static bool GetWindowsVolumeCaseSensitivity(string directoryPath)
    {
        string? rootPath = Path.GetPathRoot(directoryPath);

        if (string.IsNullOrEmpty(rootPath))
        {
            throw new DirectoryNotFoundException($"Could not determine the volume root for path '{directoryPath}'.");
        }

        if (!NativeMethods.GetVolumeInformation(
                rootPath,
                null,
                0,
                nint.Zero,
                nint.Zero,
                out uint fileSystemFlags,
                null,
                0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return (fileSystemFlags & FILE_CASE_SENSITIVE_SEARCH) != 0;
    }

    private const uint FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x0000_0001;
    private const uint FILE_CASE_SENSITIVE_SEARCH = 0x0000_0001;

    private enum FILE_INFO_BY_NAME_CLASS
    {
        FileStatByNameInfo = 0,
        FileStatLxByNameInfo = 1,
        FileCaseSensitiveByNameInfo = 2,
        FileStatBasicByNameInfo = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_CASE_SENSITIVE_INFORMATION
    {
        public uint Flags;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll",EntryPoint = "GetFileInformationByName",CharSet = CharSet.Unicode,SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByName(
            string fileName,
            FILE_INFO_BY_NAME_CLASS fileInformationClass,
            out FILE_CASE_SENSITIVE_INFORMATION fileInfoBuffer,
            uint fileInfoBufferSize);

        [DllImport("kernel32.dll",EntryPoint = "GetVolumeInformationW",CharSet = CharSet.Unicode,SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumeInformation(
            string rootPathName,
            char[]? volumeNameBuffer,
            uint volumeNameSize,
            nint volumeSerialNumber,
            nint maximumComponentLength,
            out uint fileSystemFlags,
            char[]? fileSystemNameBuffer,
            uint fileSystemNameSize);
    }
}
