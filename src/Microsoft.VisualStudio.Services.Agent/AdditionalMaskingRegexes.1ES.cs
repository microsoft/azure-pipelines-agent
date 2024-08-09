// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{
    public static partial class AdditionalMaskingPatterns
    {
        public static IEnumerable<PatternDescriptor> OneESPatterns => oneESPatterns;

        private static IEnumerable<PatternDescriptor> oneESPatterns =
            new List<PatternDescriptor>()
            {
                // AAD client app, most recent two versions.
                new PatternDescriptor
                {
                    Regex = @"(\b[0-9A-Za-z-_~.]{3}7Q~[0-9A-Za-z-_~.]{31}(\b|$))|(\b[0-9A-Za-z-_~.]{3}8Q~[0-9A-Za-z-_~.]{34}(\b|$))",
                    SniffLiterals = new HashSet<string>(new[]{ "7Q~", "8Q~"}),
                    Moniker = "SEC101/156.AadClientAppSecret",
                },

                // Prominent Azure provider 256-bit symmetric keys.
                new PatternDescriptor
                {
                    Regex = @"\b[0-9A-Za-z+/]{33}(AIoT|\+(ASb|AEh|ARm))[A-P][0-9A-Za-z+/]{5}=(\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "AIoT", "+ASb", "+AEh", "+ARm" }),
                    Moniker = "SEC102/101.Unclassified32ByteBase64String",
                },

                // Prominent Azure provider 512-bit symmetric keys.
                new PatternDescriptor
                {
                    Regex = @"(\b|$)[0-9A-Za-z+/]{76}(APIM|ACDb|\+(ABa|AMC|ASt))[0-9A-Za-z+/]{5}[AQgw]==((\b|$)|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "APIM", "ACDb", "+ABa", "+AMC", "+ASt", }),
                    Moniker = "SEC102/102.Unclassified64ByteBase64String",
                },
                       
                // Azure Function key.
                new PatternDescriptor
                {
                    Regex = @"\b[0-9A-Za-z_\-]{44}AzFu[0-9A-Za-z\-_]{5}[AQgw]==(\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "AzFu" }),
                    Moniker = "SEC101/158.AzureFunctionIdentifiableKey",
                },

                  // Azure Search keys.
                new PatternDescriptor
                {
                    Regex = @"\b[0-9A-Za-z]{42}AzSe[A-D][0-9A-Za-z]{5}(\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "AzSe" }),
                    Moniker = "SEC101/167.AzureSearchIdentifiableKey",
                },
                  
                  // Azure Container Registry keys.
                new PatternDescriptor
                {
                    Regex = @"\b[0-9A-Za-z+/]{42}\+ACR[A-D][0-9A-Za-z+/]{5}(\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "+ACR" }),
                    Moniker = "SEC101/176.AzureContainerRegistryIdentifiableKey",
                },
                  
                  // Azure Cache for Redis keys.
                new PatternDescriptor
                {
                    Regex = @"\b[0-9A-Za-z]{33}AzCa[A-P][0-9A-Za-z]{5}=(\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "AzCa" }),
                    Moniker = "SEC101/154.AzureCacheForRedisIdentifiableKey"
                },
                  
                // NuGet API keys.
                new PatternDescriptor
                {
                    Regex = @"\boy2[a-p][0-9a-z]{15}[aq][0-9a-z]{11}[eu][bdfhjlnprtvxz357][a-p][0-9a-z]{11}[aeimquy4](\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "oy2" }),
                    Moniker = "SEC101/031.NuGetApiKey"
                },

                // NPM author keys.
                new PatternDescriptor
                {
                    Regex = @"\bnpm_[0-9A-Za-z]{36}(\b|$)",
                    SniffLiterals = new HashSet<string>(new[]{ "npm_" }),
                    Moniker = "SEC101/050.NpmAuthorKey"
                },
            };
    }
}