// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;
using Minimatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("Test")]

namespace Agent.Plugins.PipelineCache
{
    public static class FingerprintCreator
    {
        private static readonly bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool isCaseSensitive = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        // https://github.com/Microsoft/azure-pipelines-task-lib/blob/master/node/docs/findingfiles.md#matchoptions
        private static readonly Options minimatchOptions = new Options
        {
            Dot = true,
            NoBrace = true,
            NoCase = !isCaseSensitive,
            AllowWindowsPaths = isWindows,
        };

        private static readonly char[] GlobChars = new[] { '*', '?', '[', ']' };

        private const char ForceStringLiteral = '"';

        private static bool IsPathyChar(char c)
        {
            if (GlobChars.Contains(c)) return true;
            if (c == Path.DirectorySeparatorChar) return true;
            if (c == Path.AltDirectorySeparatorChar) return true;
            if (c == Path.VolumeSeparatorChar) return true;
            return !Path.GetInvalidFileNameChars().Contains(c);
        }

        internal static bool IsPathyKeySegment(string keySegment)
        {
            if (keySegment.First() == ForceStringLiteral && keySegment.Last() == ForceStringLiteral) return false;
            if (keySegment.Any(c => !IsPathyChar(c))) return false;
            if (!keySegment.Contains(".") &&
                !keySegment.Contains(Path.DirectorySeparatorChar) &&
                !keySegment.Contains(Path.AltDirectorySeparatorChar)) return false;
            if (keySegment.Last() == '.') return false;
            return true;
        }

        internal static Func<string, bool> CreateMinimatchFilter(AgentTaskPluginExecutionContext context, string rule, bool invert)
        {
            Func<string, bool> filter = Minimatcher.CreateFilter(rule, minimatchOptions);
            Func<string, bool> tracedFilter = (path) =>
            {
                bool filterResult = filter(path);
                context.Verbose($"Path `{path}` is {(filterResult ? "" : "not ")}{(invert ? "excluded" : "included")} because of pattern `{(invert ? "!" : "")}{rule}`.");
                return invert ^ filterResult;
            };

            return tracedFilter;
        }

        internal static string MakePathCanonical(string defaultWorkingDirectory, string path)
        {
            // Normalize to some extent, let minimatch worry about casing
            if (Path.IsPathFullyQualified(path))
            {
                return Path.GetFullPath(path);
            }
            else
            {
                return Path.GetFullPath(path, defaultWorkingDirectory);
            }
        }

        internal static Func<string, bool> CreateFilter(
            AgentTaskPluginExecutionContext context,
            IEnumerable<string> includeRules,
            IEnumerable<string> excludeRules)
        {
            Func<string, bool>[] includeFilters = includeRules.Select(includeRule =>
                 CreateMinimatchFilter(context, includeRule, invert: false)).ToArray();
            Func<string, bool>[] excludeFilters = excludeRules.Select(excludeRule =>
                 CreateMinimatchFilter(context, excludeRule, invert: true)).ToArray();
            Func<string, bool> filter = (path) => includeFilters.Any(f => f(path)) && excludeFilters.All(f => f(path));
            return filter;
        }

        internal struct Enumeration
        {
            public string RootPath;
            public string Pattern;
            public SearchOption Depth;
        }

        internal class MatchedFile
        {
            private static readonly SHA256Managed s_sha256 = new SHA256Managed();

            public MatchedFile(string displayPath, long fileLength, string hash)
            {
                this.DisplayPath = displayPath;
                this.FileLength = fileLength;
                this.Hash = hash;
            }

            public MatchedFile(string displayPath, FileStream fs) :
                this(displayPath, fs.Length, s_sha256.ComputeHash(fs).ToHex())
            {
            }

            public string DisplayPath;
            public long FileLength;
            public string Hash;

            public string GetHash()
            {
                return MatchedFile.GenerateHash(new[] { this });
            }

            public static string GenerateHash(IEnumerable<MatchedFile> matches)
            {
                string s = matches.Aggregate(new StringBuilder(),
                        (sb, file) => sb.Append($"\nSHA256({file.DisplayPath})=[{file.FileLength}]{file.Hash}"),
                        sb => sb.ToString());

                return Convert.ToBase64String(s_sha256.ComputeHash(Encoding.UTF8.GetBytes(s)));
            }
        }

        internal enum KeySegmentType
        {
            String = 0,
            FilePath = 1,
            FilePattern = 2
        }

        // Given a globby path, figure out where to start enumerating.
        // Room for optimization here e.g. 
        // includeGlobPath = /dir/*foo* 
        // should map to 
        // enumerateRootPath = /dir/
        // enumeratePattern = *foo*
        // enumerateDepth = SearchOption.TopDirectoryOnly
        //
        // It's ok to come up with a file-enumeration that includes too much as the glob filter
        // will filter out the extra, but it's not ok to include too little in the enumeration.
        internal static Enumeration DetermineFileEnumerationFromGlob(string includeGlobPathAbsolute)
        {
            int firstGlob = includeGlobPathAbsolute.IndexOfAny(GlobChars);
            bool hasRecursive = includeGlobPathAbsolute.Contains("**", StringComparison.Ordinal);

            // no globbing
            if (firstGlob < 0)
            {
                return new Enumeration()
                {
                    RootPath = Path.GetDirectoryName(includeGlobPathAbsolute),
                    Pattern = Path.GetFileName(includeGlobPathAbsolute),
                    Depth = SearchOption.TopDirectoryOnly
                };
            }
            else
            {
                int rootDirLength = includeGlobPathAbsolute.Substring(0, firstGlob).LastIndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                return new Enumeration()
                {
                    RootPath = includeGlobPathAbsolute.Substring(0, rootDirLength),
                    Pattern = "*",
                    Depth = hasRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
                };
            }
        }

        internal static void CheckSegment(string segment, string segmentType)
        {
            if (segment.Equals("*", StringComparison.Ordinal))
            {
                throw new ArgumentException($"`*` is a reserved {segmentType} segment. For path glob, use `./*`.");
            }
            else if (segment.Equals(Fingerprint.Wildcard, StringComparison.Ordinal))
            {
                throw new ArgumentException($"`**` is a reserved {segmentType} segment. For path glob, use `./**`.");
            }
            else if (segment.First() == '\'')
            {
                throw new ArgumentException($"A {segmentType} segment cannot start with a single-quote character`.");
            }
            else if (segment.First() == '`')
            {
                throw new ArgumentException($"A {segmentType} segment cannot start with a backtick character`.");
            }
        }

        internal static void LogSegment(
            AgentTaskPluginExecutionContext context,
            string[] segments,
            string segment,
            KeySegmentType type,
            Object details)
        {
            Func<string, int, string> FormatForDisplay = (value, displayLength) =>
            {
                if (value.Length > displayLength)
                {
                    value = value.Substring(0, displayLength - 3) + "...";
                }

                return value.PadRight(displayLength);
            };

            string formattedSegment = FormatForDisplay(segment, Math.Min(segments.Select(s => s.Length).Max(), 50));

            if (type == KeySegmentType.String)
            {
                context.Output($" - {formattedSegment} [string]");
            }
            else
            {
                var matches = (details as MatchedFile[]) ?? new MatchedFile[0];

                if (type == KeySegmentType.FilePath)
                {
                    string fileHash = matches.Length > 0 ? matches[0].Hash : null;
                    context.Output($" - {formattedSegment} [file] {(!string.IsNullOrWhiteSpace(fileHash) ? $"--> {fileHash}" : "(not found)")}");
                }
                else if (type == KeySegmentType.FilePattern)
                {
                    context.Output($" - {formattedSegment} [file pattern; matches: {matches.Length}]");
                    if (matches.Any())
                    {
                        int filePathDisplayLength = Math.Min(matches.Select(mf => mf.DisplayPath.Length).Max(), 70);
                        foreach (var match in matches)
                        {
                            context.Output($"   - {FormatForDisplay(match.DisplayPath, filePathDisplayLength)} --> {match.Hash}");
                        }
                    }
                }
            }
        }

        public static Fingerprint EvaluateKeyToFingerprint(
            AgentTaskPluginExecutionContext context,
            string filePathRoot,
            IEnumerable<string> keySegments)
        {
            // Avoid multiple enumerations
            var segments = keySegments.ToArray();

            // Quickly validate all segments
            foreach (string segment in segments)
            {
                CheckSegment(segment, "key");
            }

            string defaultWorkingDirectory = context.Variables.GetValueOrDefault(
                "system.defaultworkingdirectory" // Constants.Variables.System.DefaultWorkingDirectory
            )?.Value;

            var resolvedSegments = new List<string>();
            var exceptions = new List<Exception>();

            foreach (string keySegment in segments)
            {
                if (!IsPathyKeySegment(keySegment))
                {
                    LogSegment(context, segments, keySegment, KeySegmentType.String, null);
                    resolvedSegments.Add(keySegment);
                }
                else
                {
                    string[] pathRules = keySegment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    string[] includeRules = pathRules.Where(p => !p.StartsWith('!')).ToArray();

                    if (!includeRules.Any())
                    {
                        throw new ArgumentException("No include rules specified.");
                    }

                    var enumerations = new Dictionary<Enumeration, List<string>>();
                    foreach (string includeRule in includeRules)
                    {
                        string absoluteRootRule = MakePathCanonical(defaultWorkingDirectory, includeRule);
                        context.Verbose($"Expanded include rule is `{absoluteRootRule}`.");
                        Enumeration enumeration = DetermineFileEnumerationFromGlob(absoluteRootRule);
                        List<string> globs;
                        if (!enumerations.TryGetValue(enumeration, out globs))
                        {
                            enumerations[enumeration] = globs = new List<string>();
                        }
                        globs.Add(absoluteRootRule);
                    }

                    string[] excludeRules = pathRules.Where(p => p.StartsWith('!')).ToArray();
                    string[] absoluteExcludeRules = excludeRules.Select(excludeRule =>
                    {
                        excludeRule = excludeRule.Substring(1);
                        return MakePathCanonical(defaultWorkingDirectory, excludeRule);
                    }).ToArray();

                    var matchedFiles = new SortedDictionary<string, MatchedFile>(StringComparer.Ordinal);

                    foreach (var kvp in enumerations)
                    {
                        Enumeration enumerate = kvp.Key;
                        List<string> absoluteIncludeGlobs = kvp.Value;
                        context.Verbose($"Enumerating starting at root `{enumerate.RootPath}` with pattern `{enumerate.Pattern}` and depth `{enumerate.Depth}`.");
                        IEnumerable<string> files = Directory.EnumerateFiles(enumerate.RootPath, enumerate.Pattern, enumerate.Depth);
                        Func<string, bool> filter = CreateFilter(context, absoluteIncludeGlobs, absoluteExcludeRules);
                        files = files.Where(f => filter(f)).Distinct();

                        foreach (string path in files)
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                // Path.GetRelativePath returns 'The relative path, or path if the paths don't share the same root.'
                                string displayPath = filePathRoot == null ? path : Path.GetRelativePath(filePathRoot, path);
                                matchedFiles.Add(path, new MatchedFile(displayPath, fs));
                            }
                        }
                    }

                    var patternSegment = keySegment.IndexOfAny(GlobChars) >= 0 || matchedFiles.Count() > 1;

                    var displayKeySegment = keySegment;

                    if (context.Container != null)
                    {
                        displayKeySegment = context.Container.TranslateToContainerPath(displayKeySegment);
                    }

                    LogSegment(
                        context,
                        segments,
                        displayKeySegment,
                        patternSegment ? KeySegmentType.FilePattern : KeySegmentType.FilePath,
                        matchedFiles.Values.ToArray()
                    );

                    if (!matchedFiles.Any())
                    {
                        if (patternSegment)
                        {
                            exceptions.Add(new FileNotFoundException($"No matching files found for pattern: {displayKeySegment}"));
                        }
                        else
                        {
                            exceptions.Add(new FileNotFoundException($"File not found: {displayKeySegment}"));
                        }
                    }

                    resolvedSegments.Add(MatchedFile.GenerateHash(matchedFiles.Values));
                }
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }

            return new Fingerprint() { Segments = resolvedSegments.ToArray() };
        }

        public static Fingerprint EvaluatePathToFingerprint(
            AgentTaskPluginExecutionContext context,
            string workingDirectory,
            IEnumerable<string> pathSegments)
        {
            // Avoid multiple enumerations
            var segments = pathSegments.ToArray();

            // Quickly validate all segments
            foreach (string pathSegment in pathSegments)
            {
                CheckSegment(pathSegment, "path");
            }

            //string defaultWorkingDirectory = context.Variables.GetValueOrDefault(
            //    "system.defaultworkingdirectory" // Constants.Variables.System.DefaultWorkingDirectory
            //)?.Value;

            var resolvedSegments = new List<string>();
            var exceptions = new List<Exception>();

            foreach (string segment in segments)
            {
                if (!IsPathyKeySegment(segment))
                {
                    LogSegment(context, segments, segment, KeySegmentType.String, null);
                    resolvedSegments.Add(segment);
                }
                else
                {
                    string[] pathRules = segment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    string[] includeRules = pathRules.Where(p => !p.StartsWith('!')).ToArray();

                    if (!includeRules.Any())
                    {
                        throw new ArgumentException("No include rules specified.");
                    }

                    var enumerations = new Dictionary<Enumeration, List<string>>();
                    foreach (string includeRule in includeRules)
                    {
                        string absoluteRootRule = MakePathCanonical(workingDirectory, includeRule);
                        context.Verbose($"Expanded include rule is `{absoluteRootRule}`.");
                        Enumeration enumeration = DetermineFileEnumerationFromGlob(absoluteRootRule);
                        List<string> globs;
                        if (!enumerations.TryGetValue(enumeration, out globs))
                        {
                            enumerations[enumeration] = globs = new List<string>();
                        }
                        globs.Add(absoluteRootRule);
                    }

                    string[] excludeRules = pathRules.Where(p => p.StartsWith('!')).ToArray();
                    string[] absoluteExcludeRules = excludeRules.Select(excludeRule =>
                    {
                        excludeRule = excludeRule.Substring(1);
                        return MakePathCanonical(workingDirectory, excludeRule);
                    }).ToArray();

                    var matchedDirectories = new SortedDictionary<string, string>(StringComparer.Ordinal);

                    foreach (var kvp in enumerations)
                    {
                        Enumeration enumerate = kvp.Key;
                        List<string> absoluteIncludeGlobs = kvp.Value;
                        context.Verbose($"Enumerating starting at root `{enumerate.RootPath}` with pattern `{enumerate.Pattern}` and depth `{enumerate.Depth}`.");
                        IEnumerable<string> directories = Directory.EnumerateDirectories(enumerate.RootPath, enumerate.Pattern, enumerate.Depth);
                        Func<string, bool> filter = CreateFilter(context, absoluteIncludeGlobs, absoluteExcludeRules);
                        directories = directories.Where(f => filter(f)).Distinct();

                        foreach (string path in directories)
                        {
                            // Path.GetRelativePath returns 'The relative path, or path if the paths don't share the same root.'
                            string displayPath = Path.GetRelativePath(workingDirectory, path);
                            matchedDirectories.Add(path, displayPath);
                        }
                    }

                    var patternSegment = segment.IndexOfAny(GlobChars) >= 0 || matchedDirectories.Count() > 1;

                    var displayPathSegment = segment;

                    if (context.Container != null)
                    {
                        displayPathSegment = context.Container.TranslateToContainerPath(displayPathSegment);
                    }

                    LogSegment(
                        context,
                        segments,
                        displayPathSegment,
                        patternSegment ? KeySegmentType.FilePattern : KeySegmentType.FilePath,
                        matchedDirectories.Values.ToArray()
                    );

                    if (!matchedDirectories.Any())
                    {
                        if (patternSegment)
                        {
                            exceptions.Add(new DirectoryNotFoundException($"No matching directories found for pattern: {displayPathSegment}"));
                        }
                        else
                        {
                            exceptions.Add(new DirectoryNotFoundException($"Directory not found: {displayPathSegment}"));
                        }
                    }

                    resolvedSegments.AddRange(matchedDirectories.Values);
                }
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }

            return new Fingerprint() { Segments = resolvedSegments.ToArray() };
        }
    }
}
