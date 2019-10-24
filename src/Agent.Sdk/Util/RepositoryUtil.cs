// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class RepositoryUtil
    {
        public static readonly string PrimaryRepositoryName = "self";

        /// <summary>
        /// Returns true if the dictionary contains the 'HasMultipleCheckouts' key and the value is set to 'true'.
        /// </summary>
        public static bool HasMultipleCheckouts(Dictionary<string, string> jobSettings)
        {
            if (jobSettings != null && jobSettings.TryGetValue(WellKnownJobSettings.HasMultipleCheckouts, out string hasMultipleCheckoutsText))
            {
                return bool.TryParse(hasMultipleCheckoutsText, out bool hasMultipleCheckouts) && hasMultipleCheckouts;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the string matches the primary repository name (self)
        /// </summary>
        public static bool IsPrimaryRepositoryName(string repoAlias)
        {
            return string.Equals(repoAlias, PrimaryRepositoryName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// This method returns the repo from the list that is considered the primary repository.
        /// If the list only contains 1 repo, then that is the primary repository.
        /// If the list contains more than one, then we look for the repository with the primary repo name (self).
        /// It returns null, if no primary repository can be found.
        /// </summary>
        public static RepositoryResource GetPrimaryRepository(IList<RepositoryResource> repositories)
        {
            if (repositories == null || !repositories.Any())
            {
                return null;
            }

            if (repositories.Count == 1)
            {
                return repositories.First();
            }
            else
            {
                return GetRepository(repositories, PrimaryRepositoryName);
            }
        }

        /// <summary>
        /// This method returns the repo matching the repo alias passed.
        /// It returns null if that repo can't be found.
        /// </summary>
        public static RepositoryResource GetRepository(IList<RepositoryResource> repositories, string repoAlias)
        {
            if (repositories == null)
            {
                return null;
            }

            return repositories.FirstOrDefault(r => string.Equals(r.Alias, repoAlias, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the folder name that would be created by calling 'git.exe clone'.
        /// This is just the relative folder name not a full path.
        /// The repo name is used if provided, then repo url, and finally repo alias.
        /// </summary>
        public static string GetCloneDirectory(RepositoryResource repository)
        {
            ArgUtil.NotNull(repository, nameof(repository));

            string repoName =
                repository.Properties.Get<string>(RepositoryPropertyNames.Name) ??
                repository.Url?.AbsoluteUri ??
                repository.Alias;

            return GetCloneDirectory(repoName);
        }

        /// <summary>
        /// Returns the folder name that would be created by calling 'git.exe clone'.
        /// This is just the relative folder name not a full path.
        /// This can take a repo full name, partial name, or url as input.
        /// </summary>
        public static string GetCloneDirectory(string repoName)
        {
            // The logic here was inspired by what git.exe does
            // see https://github.com/git/git/blob/4c86140027f4a0d2caaa3ab4bd8bfc5ce3c11c8a/builtin/clone.c#L213

            ArgUtil.NotNullOrEmpty(repoName, nameof(repoName));
            const string schemeSeparator = "://";

            // skip any kind of scheme
            int startPosition = repoName.IndexOf(schemeSeparator);
            if (startPosition < 0)
            {
                // There is no scheme
                startPosition = 0;
            }
            else
            {
                startPosition += schemeSeparator.Length;
            }

            // skip any auth info (ends with @)
            int endPosition = repoName.Length - 1;
            startPosition = repoName.SkipLastIndexOf('@', startPosition, endPosition, out _);

            // trim any slashes or ".git" extension
            endPosition = TrimSlashesAndExtension(repoName, endPosition);

            // skip everything before the last path segment (ends with /)
            startPosition = repoName.SkipLastIndexOf('/', startPosition, endPosition, out bool slashFound);
            if (!slashFound)
            {
                // No slashes means we only have a host name, remove any trailing port number
                endPosition = TrimPortNumber(repoName, endPosition, startPosition);
            }

            // Colons can also be path separators, so skip past the last colon
            startPosition = repoName.SkipLastIndexOf(':', startPosition, endPosition, out _);

            return repoName.Substring(startPosition, endPosition - startPosition + 1);
        }

        private static int TrimPortNumber(string buffer, int endIndex, int startIndex)
        {
            int lastColon = buffer.FinalIndexOf(':', startIndex, endIndex);
            // Trim the rest of the string after the colon if it is empty or is all digits
            if (lastColon >= 0 && (lastColon == endIndex || buffer.SubstringIsNumber(lastColon + 1, endIndex)))
            {
                return lastColon - 1;
            }

            return endIndex;
        }

        private static int TrimSlashesAndExtension(string buffer, int endIndex)
        {
            if (buffer == null || endIndex < 0 || endIndex >= buffer.Length)
            {
                return endIndex;
            }

            // skip ending slashes or whitespace
            while (endIndex > 0 && (buffer[endIndex] == '/' || char.IsWhiteSpace(buffer[endIndex])))
            {
                endIndex--;
            }

            const string gitExtension = ".git";
            int possibleExtensionStart = endIndex - gitExtension.Length + 1;
            if (possibleExtensionStart >= 0 && gitExtension.Equals(buffer.Substring(possibleExtensionStart, gitExtension.Length), StringComparison.OrdinalIgnoreCase))
            {
                // We found the .git extension
                endIndex -= gitExtension.Length;
            }

            // skip ending slashes or whitespace
            while (endIndex > 0 && (buffer[endIndex] == '/' || char.IsWhiteSpace(buffer[endIndex])))
            {
                endIndex--;
            }

            return endIndex;
        }

        private static int SkipLastIndexOf(this string buffer, char charToSearchFor, int startIndex, int endIndex, out bool charFound)
        {
            int index = buffer.FinalIndexOf(charToSearchFor, startIndex, endIndex);
            if (index >= 0 && index < endIndex)
            {
                // Start after the char we found
                charFound = true;
                return index + 1;
            }

            charFound = false;
            return startIndex;
        }

        private static int FinalIndexOf(this string buffer, char charToSearchFor, int startIndex, int endIndex)
        {
            if (buffer == null || startIndex < 0 || endIndex < 0 || startIndex >= buffer.Length || endIndex >= buffer.Length)
            {
                return -1;
            }

            return buffer.LastIndexOf(charToSearchFor, endIndex, endIndex - startIndex + 1);
        }

        private static bool SubstringIsNumber(this string buffer, int startIndex, int endIndex)
        {
            if (buffer == null || startIndex < 0 || endIndex < 0 || startIndex >= buffer.Length || endIndex >= buffer.Length || startIndex > endIndex)
            {
                return false;
            }

            for (int i = startIndex; i <= endIndex; i++)
            {
                if (!char.IsDigit(buffer[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}