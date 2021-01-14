using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Security.CredScan.KnowledgeBase.Ruleset;
using Microsoft.Security.CredScan.KnowledgeBase.Client.DataFormat.Json;
using Microsoft.Security.CredScan.KnowledgeBase;

namespace CredScanRegexes
{
    class CredScanPatternExtractor
    {
        public IEnumerable<(string Name, string Pattern)> GetPatterns()
        {
            string searchConfig = RulesetHelper.GetPredefinedSearchConfiguration("FullTextProvider");
            var kbf = new JsonKnowledgeBaseFactory(JsonConvert.DeserializeObject(searchConfig) as JObject);
            var kb = kbf.CreateKnowledgeBase();

            // CredScan has some patterns that should not be exported publicly
            // and there are a few patterns which over-match
            var usablePatterns = kb.Patterns.Where(p =>
                !p.Tags.Contains(PatternTag.ProviderType_ContainsSecret)
                && !skipPatternNames.Contains(p.Name));

            foreach (var pattern in usablePatterns)
            {
                if (pattern.ScannerMatchingExpression is object)
                {
                    yield return (pattern.Name, new MatchingExpression(pattern.ScannerMatchingExpression).Argument);
                }

                if (pattern.ScannerMatchingExpressions is object)
                {
                    foreach (var sme in pattern.ScannerMatchingExpressions)
                    {
                        yield return (pattern.Name, new MatchingExpression(sme).Argument);
                    }
                }
            }
        }

        public string GetCredScanVersion()
        {
            Assembly assembly = Assembly.GetAssembly(typeof(RulesetHelper));
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        }

        private static List<string> skipPatternNames = new List<string>
        {
            "PasswordContextInCode",
            "PasswordContextInXml",
        };
    }
}
