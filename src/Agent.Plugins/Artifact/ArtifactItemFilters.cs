// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.WebApi;
using Minimatch;

namespace Agent.Plugins
{
    class ArtifactItemFilters
    {
        private readonly VssConnection connection;
        private readonly IAppTraceSource tracer;

        public ArtifactItemFilters(VssConnection connection, IAppTraceSource tracer)
        {
            this.tracer = tracer;
            this.connection = connection;
        }

        /// <summary>
        /// Collects hashtable with items in accordance with patterns
        /// </summary>
        /// <param name="paths">List of relative paths for items detected in artifact. The relative paths start from name of artifact.</param>
        /// <param name="minimatchPatterns">Array of patterns used to filter items in artifact</param>
        /// <param name="customMinimatchOptions">Download parameters from minimatcherFuncs</param>
        /// <returns></returns>
        public dynamic GetMapToFilterItems(List<string> paths, string[] minimatchPatterns, Options customMinimatchOptions)
        {
            // Hashtable to keep track of matches.
            Hashtable map = new Hashtable();

            foreach (string minimatchPattern in minimatchPatterns)
            {
                tracer.Info($"Pattern: {minimatchPattern}");

                // Trim and skip empty.
                string currentPattern = minimatchPattern.Trim();
                if (String.IsNullOrEmpty(currentPattern))
                {
                    tracer.Info($"Skipping empty pattern.");
                    continue;
                }

                // Clone match options.
                Options matchOptions = CloneMiniMatchOptions(customMinimatchOptions);

                // Skip comments.
                if (!matchOptions.NoComment && currentPattern.StartsWith('#'))
                {
                    tracer.Info($"Skipping comment.");
                    continue;
                }

                // Set NoComment. Brace expansion could result in a leading '#'.
                matchOptions.NoComment = true;

                // Determine whether pattern is include or exclude.
                int negateCount = 0;
                if (!matchOptions.NoNegate)
                {
                    while (negateCount < currentPattern.Length && currentPattern[negateCount] == '!')
                    {
                        negateCount++;
                    }

                    currentPattern = currentPattern.Substring(negateCount);
                    if (negateCount > 0)
                    {
                        tracer.Info($"Trimmed leading '!'. Pattern: '{currentPattern}'");
                    }
                }

                bool isIncludePattern = negateCount == 0 ||
                    (negateCount % 2 == 0 && !matchOptions.FlipNegate) ||
                    (negateCount % 2 == 1 && matchOptions.FlipNegate);

                // Set NoNegate. Brace expansion could result in a leading '!'.
                matchOptions.NoNegate = true;
                matchOptions.FlipNegate = false;

                // Trim and skip empty.
                currentPattern = currentPattern.Trim();
                if (String.IsNullOrEmpty(currentPattern))
                {
                    tracer.Info($"Skipping empty pattern.");
                    continue;
                }

                // Expand braces - required to accurately interpret findPath.
                string[] expandedPatterns;
                string preExpandedPattern = currentPattern;
                if (matchOptions.NoBrace)
                {
                    expandedPatterns = new string[] { currentPattern };
                }
                else
                {
                    expandedPatterns = ExpandBraces(currentPattern, matchOptions);
                }

                // Set NoBrace.
                matchOptions.NoBrace = true;

                foreach (string expandedPattern in expandedPatterns)
                {
                    if (expandedPattern != preExpandedPattern)
                    {
                        tracer.Info($"Pattern: {expandedPattern}");
                    }

                    // Trim and skip empty.
                    currentPattern = expandedPattern.Trim();
                    if (String.IsNullOrEmpty(currentPattern))
                    {
                        tracer.Info($"Skipping empty pattern.");
                        continue;
                    }

                    string[] currentPatterns = new string[] { currentPattern };
                    IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs(
                        currentPatterns,
                        tracer,
                        matchOptions
                    );

                    UpdatePatternsMap(isIncludePattern, paths, minimatcherFuncs, ref map);
                }
            }

            return map;
        }

        /// <summary>
        /// Expands braces in patterns if they are exist
        /// </summary>
        /// <param name="pattern">String of pattern</param>
        /// <param name="matchOptions">Download parameters from minimatcherFuncs</param>
        /// <returns></returns>
        private string[] ExpandBraces(string pattern, Options matchOptions)
        {
            // Convert slashes on Windows before calling braceExpand(). Unfortunately this means braces cannot
            // be escaped on Windows, this limitation is consistent with current limitations of minimatch (3.0.3).
            tracer.Info($"Expanding braces.");
            string convertedPattern = pattern.Replace("\\", "/");
            return Minimatcher.BraceExpand(convertedPattern, matchOptions).ToArray();
        }

        /// <summary>
        /// Adds or removes items from map in accordance with patterns
        /// </summary>
        /// <param name="isIncludePattern">Defines if specific pattern possitive or negative</param>
        /// <param name="paths">List of relative paths for items detected in artifact. The relative paths start from name of artifact.</param>
        /// <param name="minimatcherFuncs">Functions of MinimatchHelper</param>
        /// <param name="map">Map for items in artifact collected in accordance with patterns. The map is hashtable, key is string path to item in artifact, value is bool true for all items. Item with path from the hashtable is considered as required to be in list after filtering.</param>
        private void UpdatePatternsMap(bool isIncludePattern, List<string> paths, IEnumerable<Func<string, bool>> minimatcherFuncs, ref Hashtable map)
        {
            string patternType = isIncludePattern ? "include" : "exclude";
            tracer.Info($"Applying {patternType} pattern against original list.");

            List<string> matchResults = this.FilterItemsByPatterns(paths, minimatcherFuncs);
            int matchCount = 0;

            foreach (string matchResult in matchResults)
            {
                matchCount++;
                if (isIncludePattern)
                {
                    // Union the results.
                    map[matchResult] = Boolean.TrueString;
                }
                else
                {
                    // Subtract the results.
                    map.Remove(matchResult);
                }
            }

            tracer.Info($"{matchCount} matches");
        }

        /// <summary>
        /// Returns list of FileContainerItem items required to be downloaded. Used by FileContainerProvider.
        /// </summary>
        /// <param name="items">List of items detected in artifact</param>
        /// <param name="map">Map for items in artifact collected in accordance with patterns. The map is hashtable, key is string path to item in artifact, value is bool true for all items. Item with path from the hashtable is considered as required to be in list after filtering.</param>
        /// <returns></returns>
        public List<FileContainerItem> ApplyPatternsMapToContainerItems(List<FileContainerItem> items, Hashtable map)
        {
            List<FileContainerItem> resultItems = new List<FileContainerItem>();
            foreach (FileContainerItem item in items)
            {
                if (Convert.ToBoolean(map[item.Path]))
                {
                    resultItems.Add(item);
                }
            }

            return resultItems;
        }

        /// <summary>
        /// Returns list of FileInfo items required to be downloaded. Used by FileShareProvider.
        /// </summary>
        /// <param name="files">List of files detected in artifact</param>
        /// <param name="map">Map for files in artifact collected in accordance with patterns. The map is hashtable, key is string path to file in artifact, value is bool true for all files. File with path from the hashtable is considered as required to be in list after filtering.</param>
        /// <param name="sourcePath">Full path to artifact placed on file share</param>
        /// <returns></returns>
        public List<FileInfo> ApplyPatternsMapToFileShareItems(IEnumerable<FileInfo> files, Hashtable map, string sourcePath)
        {
            char[] trimChars = new[] { '\\', '/' };

            List<FileInfo> resultItems = new List<FileInfo>();
            foreach (FileInfo file in files)
            {
                var artifactName = new DirectoryInfo(sourcePath).Name;
                string pathInArtifact = RemoveSourceDirFromPath(file, sourcePath);

                if (Convert.ToBoolean(map[Path.Combine(artifactName, pathInArtifact)]))
                {
                    resultItems.Add(file);
                }
            }

            return resultItems;
        }

        /// Collects list of items filtered by minimather in accordance with pattern
        /// </summary>
        /// <param name="paths">List of relative paths for items detected in artifact. The relative paths start from name of artifact.</param>
        /// <param name="minimatchFuncs">Functions of MinimatchHelper</param>
        /// <returns></returns>
        private List<string> FilterItemsByPatterns(List<string> paths, IEnumerable<Func<string, bool>> minimatchFuncs)
        {
            List<string> filteredItems = new List<string>();
            foreach (string path in paths)
            {
                if (minimatchFuncs.Any(match => match(path)))
                {
                    filteredItems.Add(path);
                }
            }

            return filteredItems;
        }

        /// <summary>
        /// Trims source path from full path of file. Result is path to file in artifact.
        /// E.g. file is \\FileShare\TestArtifact\TestFolder\TestFile.txt, sourcePath is \\FileShare\TestArtifact, result is TestFolder\TestFile.txt.
        /// </summary>
        /// <param name="file">FileInfo object with info about file in artifact</param>
        /// <param name="sourcePath">String path to artifact on file share.</param>
        /// <returns></returns>
        public string RemoveSourceDirFromPath(FileInfo file, string sourcePath)
        {
            char[] trimChars = new[] { '\\', '/' };
            return file.ToString().Remove(0, sourcePath.Length).TrimStart(trimChars);
        }

        /// Creates copy of provided minimatcher options
        /// </summary>
        /// <param name="currentMiniMatchOptions">Existed minimatcher options for copying</param>
        /// <returns></returns>
        private Options CloneMiniMatchOptions(Options currentMiniMatchOptions)
        {
            Options clonedMiniMatchOptions = new Options()
            {
                Dot = currentMiniMatchOptions.Dot,
                FlipNegate = currentMiniMatchOptions.FlipNegate,
                MatchBase = currentMiniMatchOptions.MatchBase,
                NoBrace = currentMiniMatchOptions.NoBrace,
                NoCase = currentMiniMatchOptions.NoCase,
                NoComment = currentMiniMatchOptions.NoComment,
                NoExt = currentMiniMatchOptions.NoExt,
                NoGlobStar = currentMiniMatchOptions.NoGlobStar,
                NoNegate = currentMiniMatchOptions.NoNegate,
                NoNull = currentMiniMatchOptions.NoNull,
                IgnoreCase = currentMiniMatchOptions.IgnoreCase,
                AllowWindowsPaths = PlatformUtil.RunningOnWindows
            };
            return clonedMiniMatchOptions;
        }
    }
}