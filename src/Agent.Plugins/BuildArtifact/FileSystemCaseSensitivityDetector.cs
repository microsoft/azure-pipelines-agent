// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Agent.Plugins.BuildArtifacts
{
    /// <summary>
    /// Describes the case behavior observed for a destination filesystem.
    /// </summary>
    internal enum FileSystemCaseSensitivity
    {
        Indeterminate,
        CaseSensitive,
        CaseInsensitive
    }

    /// <summary>
    /// Detects filesystem case behavior without modifying the destination or its ancestors.
    /// </summary>
    internal static class FileSystemCaseSensitivityDetector
    {
        internal static FileSystemCaseSensitivity Detect(string targetPath)
        {
            return Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                Directory.EnumerateFileSystemEntries);
        }

        internal static FileSystemCaseSensitivity Detect(
            string targetPath,
            Func<string, bool> directoryExists,
            Func<string, bool> fileSystemEntryExists,
            Func<string, IEnumerable<string>> enumerateFileSystemEntries)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return FileSystemCaseSensitivity.Indeterminate;
            }

            ArgumentNullException.ThrowIfNull(directoryExists);
            ArgumentNullException.ThrowIfNull(fileSystemEntryExists);
            ArgumentNullException.ThrowIfNull(enumerateFileSystemEntries);

            try
            {
                DirectoryInfo existingDirectory = FindDeepestExistingDirectory(targetPath, directoryExists);
                if (existingDirectory == null)
                {
                    return FileSystemCaseSensitivity.Indeterminate;
                }

                string probeEntryName = null;
                HashSet<string> usableEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string entryPath in enumerateFileSystemEntries(existingDirectory.FullName))
                {
                    string entryName = Path.GetFileName(entryPath);
                    if (!ContainsAsciiLetter(entryName))
                    {
                        continue;
                    }

                    if (!usableEntryNames.Add(entryName))
                    {
                        return FileSystemCaseSensitivity.Indeterminate;
                    }

                    probeEntryName ??= entryName;
                }

                if (probeEntryName == null)
                {
                    return FileSystemCaseSensitivity.Indeterminate;
                }

                string alternateEntryName = ToggleFirstAsciiLetterCase(probeEntryName);
                string alternateEntryPath = Path.Combine(existingDirectory.FullName, alternateEntryName);

                return fileSystemEntryExists(alternateEntryPath)
                    ? FileSystemCaseSensitivity.CaseInsensitive
                    : FileSystemCaseSensitivity.CaseSensitive;
            }
            catch (Exception ex) when (
                ex is ArgumentException
                || ex is IOException
                || ex is NotSupportedException
                || ex is SecurityException
                || ex is UnauthorizedAccessException)
            {
                // Matching remains case-sensitive when a read-only probe cannot determine behavior.
            }

            return FileSystemCaseSensitivity.Indeterminate;
        }

        private static bool DirectoryExists(string path)
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            try
            {
                return File.GetAttributes(path).HasFlag(FileAttributes.Directory);
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private static bool FileSystemEntryExists(string path)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                return true;
            }

            try
            {
                File.GetAttributes(path);
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private static DirectoryInfo FindDeepestExistingDirectory(
            string targetPath,
            Func<string, bool> directoryExists)
        {
            DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(targetPath));
            while (directory != null && !directoryExists(directory.FullName))
            {
                directory = directory.Parent;
            }

            return directory;
        }

        private static bool ContainsAsciiLetter(string value)
        {
            foreach (char character in value)
            {
                if ((character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z'))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ToggleFirstAsciiLetterCase(string value)
        {
            char[] characters = value.ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                char character = characters[index];
                if (character >= 'A' && character <= 'Z')
                {
                    characters[index] = (char)(character + ('a' - 'A'));
                    break;
                }

                if (character >= 'a' && character <= 'z')
                {
                    characters[index] = (char)(character - ('a' - 'A'));
                    break;
                }
            }

            return new string(characters);
        }
    }
}
