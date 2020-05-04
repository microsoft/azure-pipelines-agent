using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Security.CredScan.KnowledgeBase.Ruleset;
using Microsoft.Security.CredScan.KnowledgeBase.Client.DataFormat.Json;
using Microsoft.Security.CredScan.KnowledgeBase;

namespace CredScanRegexes
{
    class Program
    {
        static void Main(string[] args)
        {
            var version = GetVersion();
            var regexes = ExtractRegexes();

            StringBuilder sb = new StringBuilder();
            string sp = "                ";
            foreach (var regex in regexes)
            {
                sb.Append($"{sp}// {regex.Key}\n");

                foreach (var pattern in regex.Value)
                {
                    string[] subPatterns = PreprocessPattern(pattern);
                    var escapedPatterns =
                        from p in subPatterns
                        select p.Replace("\"", "\"\"");
                    
                    if (escapedPatterns.Count() > 0)
                    {
                        sb.Append($"{sp}@\"{escapedPatterns.First()}\"");
                        foreach (var ep in escapedPatterns.Skip(1))
                        {
                            sb.Append($"\n{sp}+ @\"{ep}\"");
                        }
                        sb.Append(",\n");
                    }
                    else
                    {
                        sb.Append($"{sp}// skipped pattern: {pattern}\n");
                    }
                }

                sb.Append("\n");
            }

            Console.WriteLine(string.Format(
                Program.fileTemplate,
                sb.ToString(),
                version));
        }

        private static Dictionary<string, List<string>> ExtractRegexes()
        {
            string searchConfig = RulesetHelper.GetPredefinedSearchConfiguration("FullTextProvider");
            var kbf = new JsonKnowledgeBaseFactory(JsonConvert.DeserializeObject(searchConfig) as JObject);
            var kb = kbf.CreateKnowledgeBase();

            var result = new Dictionary<string, List<string>>();

            // CredScan has some patterns that should not be exported publicly
            var publicPatterns = kb.Patterns.Where(p => !p.Tags.Contains(PatternTag.ProviderType_ContainsSecret));

            foreach (var pattern in publicPatterns)
            {
                List<string> regexes = new List<string>();

                if (pattern.ScannerMatchingExpression is object)
                {
                    regexes.Add(new MatchingExpression(pattern.ScannerMatchingExpression).Argument);
                }

                if (pattern.ScannerMatchingExpressions is object)
                {
                    foreach (var sme in pattern.ScannerMatchingExpressions)
                    {
                        regexes.Add(new MatchingExpression(sme).Argument);
                    }
                }

                result[pattern.Name] = regexes;
            }

            return result;
        }

        private static string GetVersion()
        {
            Assembly assembly = Assembly.GetAssembly(typeof(RulesetHelper));
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        }

        private static int CountOccurrences(string needle, string haystack)
        {
            int count = 0;
            int start = haystack.IndexOf(needle);
            while (start > -1)
            {
                count++;
                haystack = haystack.Substring(start + needle.Length);
                start = haystack.IndexOf(needle);
            }
            return count;
        }

        // if a CredScan pattern has a named group, then the credential
        // is assumed to be in that group. otherwise, the entire pattern
        // is the credential. the azure pipelines agent assumes the whole
        // pattern is the credential to suppress, so we need to doctor up
        // CredScan patterns with non-matching groups.
        private static string[] PreprocessPattern(string pattern)
        {
            // multiple named groups confuses things, so skip them for now
            if (CountOccurrences("(?<", pattern) > 1)
            {
                return new string[] { };
            }

            if (pattern.IndexOf("(?<") > -1)
            {
                // finding the beginning of the named capture group is easy
                int startNamedGroup = pattern.IndexOf("(?<");

                // finding the end means looking for the matching close-paren
                int parenCount = 1;
                int endNamedGroup = startNamedGroup + 1;
                while(parenCount > 0 && endNamedGroup < pattern.Length)
                {
                    string letter = pattern.Substring(endNamedGroup, 1);
                    if (letter == "(")
                    {
                        parenCount++;
                    }
                    else if (letter == ")")
                    {
                        parenCount--;
                    }
                    endNamedGroup++;
                }
                
                List<string> result = new List<string>();
                if (startNamedGroup > 0)
                {
                    // nonmatching lookbehind
                    result.Add($"(?<={pattern.Substring(0, startNamedGroup)})");
                }

                // the matching group
                result.Add($"{pattern.Substring(startNamedGroup, endNamedGroup-startNamedGroup)}");

                if (endNamedGroup < pattern.Length)
                {
                    // nonmatching lookahead
                    result.Add($"(?={pattern.Substring(endNamedGroup)})");
                }

                return result.ToArray();
            }

            return new string[] { pattern };
        }

        private static string fileTemplate = @"// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// THIS FILE IS GENERATED CODE.
// DO NOT EDIT.
// YOUR EDITS MAY BE LOST.
//
// Generated by tools/CredScanRegexes/CredScanRegexes.csproj
// from CredScan version {1}

using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{{
    internal static partial class AdditionalMaskingRegexes
    {{
        public static IEnumerable<string> CredScanPatterns => credScanPatterns;

        // Each pattern or set of patterns has a comment mentioning
        // which CredScan policy it came from. In CredScan, if a pattern
        // contains a named group, then that named group is considered the
        // sensitive part.
        // 
        // For the agent, we don't want to mask the non-sensitive parts, so
        // we wrap lookbehind and lookahead non-match groups around those
        // parts which aren't in the named group.
        // 
        // The non-matching parts are pulled out into separate string
        // literals to make them easier to manually examine.
        private static IEnumerable<string> credScanPatterns =
            new List<string>()
            {{
{0}            }};
    }}
}}";
    }
}
