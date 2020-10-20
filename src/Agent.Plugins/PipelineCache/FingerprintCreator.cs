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

[assembly: InternalsVisibleTo("Test")]

namespace Agent.Plugins.PipelineCache
{
    public enum FingerprintType
    {
        Key,
        Path
    }

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

        internal static bool IsPathySegment(string segment)
        {
            if (segment.First() == ForceStringLiteral && segment.Last() == ForceStringLiteral) return false;
            if (segment.Any(c => !IsPathyChar(c))) return false;
            if (!segment.Contains(".") &&
                !segment.Contains(Path.DirectorySeparatorChar) &&
                !segment.Contains(Path.AltDirectorySeparatorChar)) return false;
            if (segment.Last() == '.') return false;
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
            FilePattern = 2,
            Directory = 3,
            DirectoryPattern = 4
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

        private static void LogSegment(
            AgentTaskPluginExecutionContext context,
            IEnumerable<string> segments,
            string segment,
            KeySegmentType type,
            Object details)
        {
            string FormatForDisplay(string value, int displayLength)
            {
                if (value.Length > displayLength)
                {
                    value = value.Substring(0, displayLength - 3) + "...";
                }

                return value.PadRight(displayLength);
            };

            string formattedSegment = FormatForDisplay(segment, Math.Min(segments.Select(s => s.Length).Max(), 50));
            
            void LogPatternSegment<T>(object value, string title, Func<T, string> getText, Func<T,string> getSuffix = null)
            {
                var matches = (value as T[]) ?? Array.Empty<T>();
                context.Output($" - {formattedSegment} [{title} pattern; matches: {matches.Length}]");
                if (matches.Any())
                {
                    int displayLength = Math.Min(matches.Select(d => getText(d).Length).Max(), 70);
                    foreach (var match in matches)
                    {
                        context.Output($"   - {FormatForDisplay(getText(match), displayLength)}{(getSuffix != null ? getSuffix(match) : "")}");
                    }
                }
            };

            switch (type)
            {
                case KeySegmentType.String:
                    context.Output($" - {formattedSegment} [string]");
                    break;
                case KeySegmentType.Directory:
                    context.Output($" - {formattedSegment} [directory]");
                    break;
                case KeySegmentType.DirectoryPattern:
                    LogPatternSegment<string>(
                        details, 
                        "directory", 
                        s => s
                    );
                    break;
                case KeySegmentType.FilePath:
                    var files = (details as MatchedFile[]) ?? new MatchedFile[0];
                    string fileHash = files.Length > 0 ? files[0].Hash : null;
                    context.Output($" - {formattedSegment} [file] {(!string.IsNullOrWhiteSpace(fileHash) ? $"--> {fileHash}" : "(not found)")}");
                    break;
                case KeySegmentType.FilePattern:
                    LogPatternSegment<MatchedFile>(
                        details, 
                        "file", 
                        match => match.DisplayPath, 
                        match => $" --> {match.Hash}"
                    );
                    break;
            }
        }

        public static Fingerprint EvaluateToFingerprint(
            AgentTaskPluginExecutionContext context,
            string filePathRoot,
            IEnumerable<string> segments,
            FingerprintType fingerprintType)
        {
            // Quickly validate all segments
            foreach (string segment in segments)
            {
                CheckSegment(segment, fingerprintType.ToString());
            }

            string defaultWorkingDirectory = context.Variables.GetValueOrDefault(
                "system.defaultworkingdirectory" // Constants.Variables.System.DefaultWorkingDirectory
            )?.Value ?? filePathRoot;

            var resolvedSegments = new List<string>();
            var exceptions = new List<Exception>();
            var hasPatternSegments = false;

            foreach (string segment in segments)
            {
                if (!IsPathySegment(segment))
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
                    var matchedDirectories = new SortedDictionary<string, string>(StringComparer.Ordinal);

                    foreach (var kvp in enumerations)
                    {
                        Enumeration enumerate = kvp.Key;
                        List<string> absoluteIncludeGlobs = kvp.Value;
                        context.Verbose($"Enumerating starting at root `{enumerate.RootPath}` with pattern `{enumerate.Pattern}` and depth `{enumerate.Depth}`.");
                        IEnumerable<string> files = fingerprintType == FingerprintType.Key
                            ? Directory.EnumerateFiles(enumerate.RootPath, enumerate.Pattern, enumerate.Depth)
                            : Directory.EnumerateDirectories(enumerate.RootPath, enumerate.Pattern, enumerate.Depth);

                        Func<string, bool> filter = CreateFilter(context, absoluteIncludeGlobs, absoluteExcludeRules);
                        files = files.Where(f => filter(f)).Distinct();

                        foreach (string path in files)
                        {
                            // Path.GetRelativePath returns 'The relative path, or path if the paths don't share the same root.'
                            string displayPath = filePathRoot == null ? path : Path.GetRelativePath(filePathRoot, path);

                            if (fingerprintType == FingerprintType.Key)
                            {
                                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    matchedFiles.Add(path, new MatchedFile(displayPath, fs));
                                }
                            }
                            else
                            {
                                matchedDirectories.Add(path, displayPath);
                            }
                        }
                    }

                    var patternSegment = segment.IndexOfAny(GlobChars) >= 0 || matchedFiles.Count > 1;
                    hasPatternSegments |= patternSegment;

                    var displaySegment = segment;

                    if (context.Container != null)
                    {
                        displaySegment = context.Container.TranslateToContainerPath(displaySegment);
                    }

                    KeySegmentType segmentType;
                    object details;
                    if (fingerprintType == FingerprintType.Key)
                    {
                        segmentType = patternSegment ? KeySegmentType.FilePattern : KeySegmentType.FilePath;
                        details = matchedFiles.Values.ToArray();
                        resolvedSegments.Add(MatchedFile.GenerateHash(matchedFiles.Values));

                        if (!matchedFiles.Any())
                        {
                            var message = patternSegment ? $"No matching files found for pattern: {displaySegment}" : $"File not found: {displaySegment}";
                            exceptions.Add(new FileNotFoundException(message));
                        }
                    }
                    else
                    {
                        segmentType = patternSegment ? KeySegmentType.DirectoryPattern : KeySegmentType.Directory;
                        details = matchedDirectories.Values.ToArray();
                        resolvedSegments.AddRange(matchedDirectories.Values);

                        if (!matchedDirectories.Any())
                        {
                            var message = patternSegment ? $"No matching directories found for pattern: {displaySegment}" : $"Directory not found: {displaySegment}";
                            exceptions.Add(new DirectoryNotFoundException(message));
                        }
                    }

                    LogSegment(
                        context,
                        segments,
                        displaySegment,
                        segmentType,
                        details
                    );
                }
            }

            if (fingerprintType == FingerprintType.Path)
            {
                // If there are segments or contains a glob pattern, all resolved segments must be rooted within filePathRoot (e.g. Pipeline.Workspace)
                // This limitation is mainly due to 7z not extracting backtraced paths
                if (resolvedSegments.Count() > 1 || hasPatternSegments)
                {
                    foreach (var backtracedPath in resolvedSegments.Where(x => x.StartsWith("..")))
                    {
                        exceptions.Add(new ArgumentException($"Resolved path is not within `Pipeline.Workspace`: {backtracedPath}"));
                    }
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
