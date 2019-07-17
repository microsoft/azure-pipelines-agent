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

        private static readonly char[] GlobChars = new [] { '*', '?', '[', ']' };

        private static bool IsPathyChar(char c)
        {
            if (GlobChars.Contains(c)) return true;
            if (c == Path.DirectorySeparatorChar) return true;
            if (c == Path.AltDirectorySeparatorChar) return true;
            if (c == Path.VolumeSeparatorChar) return true;
            return !Path.GetInvalidFileNameChars().Contains(c);
        }

        internal static bool IsPathy(string keySegment)
        {
            if (keySegment.First() == '\'' && keySegment.Last() == '\'') return false;
            if (keySegment.First() == '"' && keySegment.Last() == '"') return false;
            if (keySegment.Any(c => !IsPathyChar(c))) return false;
            if (!keySegment.Contains(".")) return false;
            if (keySegment.Last() == '.') return false;
            return true;
        }

        internal static Func<string,bool> CreateMinimatchFilter(AgentTaskPluginExecutionContext context, string rule, bool invert)
        {
            Func<string,bool> filter = Minimatcher.CreateFilter(rule, minimatchOptions);
            Func<string,bool> tracedFilter = (path) => {
                bool result = invert ^ filter(path);
                context.Verbose($"Path `{path}` is {(result ? "included" : "excluded")} because of pattern `{(invert ? "!" : "")}{rule}`.");
                return result;
            };

            return tracedFilter;
        }

        internal static string MakePathAbsolute(string workingDirectory, string path)
        {
            // Normalize to some extent, let minimatch worry about casing
            if (workingDirectory == null)
            {
                return Path.GetFullPath(path);
            }
            else
            {
                return Path.GetFullPath(path, workingDirectory);
            }
        }

        internal static Func<string,bool> CreateFilter(
            AgentTaskPluginExecutionContext context,
            string workingDirectory,
            string includeRule,
            IEnumerable<string> excludeRules)
        {
            Func<string,bool> includeFilter = CreateMinimatchFilter(context, includeRule, invert: false);
            Func<string,bool>[] excludeFilters = excludeRules.Select(excludeRule => 
                CreateMinimatchFilter(context, excludeRule, invert: true)).ToArray();
            Func<string,bool> filter = (path) => includeFilter(path) && excludeFilters.All(f => f(path));
            return filter;
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
        internal static void DetermineFileEnumerationFromGlob(
            string includeGlobPathAbsolute,
            out string enumerateRootPath,
            out string enumeratePattern,
            out SearchOption enumerateDepth)
        {
            int firstGlob = includeGlobPathAbsolute.IndexOfAny(GlobChars);
            bool hasRecursive = includeGlobPathAbsolute.Contains("**", StringComparison.Ordinal);

            // no globbing
            if (firstGlob < 0)
            {
                enumerateRootPath = Path.GetDirectoryName(includeGlobPathAbsolute);
                enumeratePattern = Path.GetFileName(includeGlobPathAbsolute);
                enumerateDepth = SearchOption.TopDirectoryOnly;
            }
            else
            {
                int rootDirLength = includeGlobPathAbsolute.Substring(0,firstGlob).LastIndexOfAny( new [] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar});
                enumerateRootPath = includeGlobPathAbsolute.Substring(0,rootDirLength);
                enumeratePattern = "*";
                enumerateDepth = hasRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            }
        }

        public static Fingerprint EvaluateKeyToFingerprint(
            AgentTaskPluginExecutionContext context,
            IEnumerable<string> keySegments,
            bool addWildcard)
        {
            var sha256 = new SHA256Managed();

            string workingDirectoryValue = context.Variables.GetValueOrDefault(
                "system.defaultworkingdirectory" // Constants.Variables.System.DefaultWorkingDirectory
                )?.Value;

            var resolvedSegments = new List<string>();

            foreach (string keySegment in keySegments)
            {
                if (keySegment.Equals("*", StringComparison.Ordinal))
                {
                    throw new ArgumentException("`*` is a reserved key segment. For path glob, use `./*`.");
                }
                else if (keySegment.Equals(Fingerprint.Wildcard, StringComparison.Ordinal))
                {
                    throw new ArgumentException("`**` is a reserved key segment. For path glob, use `./**`.");
                }
                else if (IsPathy(keySegment))
                {
                    context.Verbose($"Interpretting `{keySegment}` as a path.");

                    var segment = new StringBuilder();
                    bool foundFile = false;

                    string[] pathRules = keySegment.Split(new []{','}, StringSplitOptions.RemoveEmptyEntries);
                    string rootRule = pathRules.First();
                    if(rootRule[1] == '!')
                    {
                        throw new ArgumentException("Path glob must start with an include glob.");
                    }

                    string workingDirectory = null;
                    if (!Path.IsPathFullyQualified(rootRule))
                    {
                        workingDirectory = workingDirectoryValue;
                    }

                    string absoluteRootRule = MakePathAbsolute(workingDirectory, rootRule);
                    context.Verbose($"Expanded include rule is `{absoluteRootRule}`.");
                    IEnumerable<string> absoluteExcludeRules = pathRules.Skip(1).Select(r => MakePathAbsolute(workingDirectory, r.Substring(1)));
                    Func<string,bool> filter = CreateFilter(context, workingDirectory, absoluteRootRule, absoluteExcludeRules);

                    DetermineFileEnumerationFromGlob(
                        absoluteRootRule,
                        out string enumerateRootPath,
                        out string enumeratePattern,
                        out SearchOption enumerateDepth);

                    context.Verbose($"Enumerating starting at root `{enumerateRootPath}` with pattern `{enumeratePattern}`.");
                    IEnumerable<string> files = Directory.EnumerateFiles(enumerateRootPath, enumeratePattern, enumerateDepth);
                    files = files.Where(f => filter(f)).Distinct();

                    foreach(string path in files)
                    {
                        foundFile = true;

                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            byte[] hash = sha256.ComputeHash(fs);
                            string displayPath = workingDirectory == null ? path : Path.GetRelativePath(enumerateRootPath, path);
                            segment.Append($"\nSHA256({displayPath})=[{fs.Length}]{hash.ToHex()}");
                        }
                    }
                    
                    if (!foundFile)
                    {
                        throw new FileNotFoundException("No files found.");
                    }

                    string fileHashString = segment.ToString();
                    string fileHashStringHash = SummarizeString(fileHashString);
                    context.Output($"File hashes summarized as `{fileHashStringHash}` from BASE64(SHA256(`{fileHashString}`))");
                    resolvedSegments.Add(fileHashStringHash);
                } 
                else
                {
                    context.Verbose($"Interpretting `{keySegment}` as a string.");
                    resolvedSegments.Add($"{keySegment}");
                }
            }

            if (addWildcard)
            {
                resolvedSegments.Add(Fingerprint.Wildcard);
            }

            return new Fingerprint() { Segments = resolvedSegments.ToArray() };
        }

        internal static string SummarizeString(string input)
        {
            var sha256 = new SHA256Managed();
            byte[] fileHashStringBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(fileHashStringBytes);
        }
    }
}