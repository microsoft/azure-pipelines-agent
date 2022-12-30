// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class RepositoryUtil
    {
        public static readonly string IsPrimaryRepository = "system.isprimaryrepository";
        public static readonly string IsTriggeringRepository = "system.istriggeringrepository";
        public static readonly string DefaultPrimaryRepositoryName = "self";
        public static readonly string GitStandardBranchPrefix = "refs/heads/";

        public static string TrimStandardBranchPrefix(string branchName)
        {
            if (!string.IsNullOrEmpty(branchName) && branchName.StartsWith(GitStandardBranchPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return branchName.Substring(GitStandardBranchPrefix.Length);
            }

            return branchName;
        }

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
            return string.Equals(repoAlias, DefaultPrimaryRepositoryName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// This method returns the repo from the list that is considered the primary repository.
        /// If the list only contains 1 repo, then that is the primary repository.
        /// If the list contains more than one, then we look for the repository marked as the primary repo.
        /// It returns null, if no primary repository can be found.
        /// </summary>
        public static RepositoryResource GetPrimaryRepository(IList<RepositoryResource> repositories)
        {
            return GetWellKnownRepository(repositories, RepositoryUtil.IsPrimaryRepository);
        }

        /// <summary>
        /// This method returns the repo from the list that is considered the triggering repository.
        /// If the list only contains 1 repo, then that is the triggering repository.
        /// If the list contains more than one, then we look for the repository marked as the triggering repo.
        /// It returns null, if no triggering repository can be found.
        /// </summary>
        public static RepositoryResource GetTriggeringRepository(IList<RepositoryResource> repositories)
        {
            return GetWellKnownRepository(repositories, RepositoryUtil.IsTriggeringRepository);
        }

        private static RepositoryResource GetWellKnownRepository(IList<RepositoryResource> repositories, string repositoryFlagName)
        {
            if (repositories == null || !repositories.Any())
            {
                return null;
            }

            if (repositories.Count == 1)
            {
                return repositories.First();
            }

            // Look for any repository marked with the expected flag name
            var repo = repositories.Where(r => r.Properties.Get<bool>(repositoryFlagName, false)).FirstOrDefault();
            if (repo != null)
            {
                return repo;
            }
            else
            {
                // return the "self" repo or null
                return GetRepository(repositories, DefaultPrimaryRepositoryName);
            }
        }

        /// <summary>
        /// This method returns the repository from the list that has a 'Path' that the localPath is parented to.
        /// If the localPath is not part of any of the repo paths, null is returned.
        /// </summary>
        public static RepositoryResource GetRepositoryForLocalPath(IList<RepositoryResource> repositories, string localPath)
        {
            if (repositories == null || !repositories.Any() || String.IsNullOrEmpty(localPath))
            {
                return null;
            }

            if (repositories.Count == 1)
            {
                return repositories.First();
            }
            else
            {
                foreach (var repo in repositories)
                {
                    var repoPath = repo.Properties.Get<string>(RepositoryPropertyNames.Path)?.TrimEnd(Path.DirectorySeparatorChar);

                    if (!string.IsNullOrEmpty(repoPath) &&
                        (localPath.Equals(repoPath, IOUtil.FilePathStringComparison)) ||
                         localPath.StartsWith(repoPath + Path.DirectorySeparatorChar, IOUtil.FilePathStringComparison))
                    {
                        return repo;
                    }
                }
            }

            return null;
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
        /// We can fairly easily determine a cloud provider like GitHub, Azure DevOps, or BitBucket.
        /// Other providers are not easily guessed, So we return Azure Repos (aka Git)
        /// </summary>
        public static string GuessRepositoryType(string repositoryUrl)
        {
            if (string.IsNullOrEmpty(repositoryUrl))
            {
                return string.Empty;
            }

            if (repositoryUrl.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RepositoryTypes.GitHub;
            }
            else if (repositoryUrl.IndexOf(".visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0
                  || repositoryUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (repositoryUrl.IndexOf("/_git/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return RepositoryTypes.Git;
                }

                return RepositoryTypes.Tfvc;
            }
            else if (repositoryUrl.IndexOf("bitbucket.org", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RepositoryTypes.Bitbucket;
            }

            // Don't assume anything
            return string.Empty;
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