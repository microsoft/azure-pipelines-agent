// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Agent.Sdk;

namespace Microsoft.TeamFoundation.DistributedTask.Logging
{
    /// <summary>
    /// Handles shell metacharacter expansion for secret masking
    /// </summary>
    public static class ShellExpansionMasker
    {
        // Shell metacharacters that can cause expansion
        private static readonly Regex ShellMetacharRegex = new Regex(@"\$\$|\$RANDOM|\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
        
        /// <summary>
        /// Detects if a secret value contains shell metacharacters that could be expanded
        /// </summary>
        /// <param name="secretValue">The secret value to check</param>
        /// <returns>True if the secret contains expandable shell metacharacters</returns>
        public static bool ContainsShellMetacharacters(string secretValue)
        {
            if (string.IsNullOrEmpty(secretValue))
                return false;
                
            return ShellMetacharRegex.IsMatch(secretValue);
        }
        
        /// <summary>
        /// Generates possible expanded variations of a secret that contains shell metacharacters
        /// </summary>
        /// <param name="secretValue">The original secret value</param>
        /// <param name="trace">Trace writer for logging</param>
        /// <returns>List of possible expanded values</returns>
        public static List<string> GetPossibleExpansions(string secretValue, ITraceWriter trace)
        {
            var expansions = new List<string>();
            
            if (string.IsNullOrEmpty(secretValue))
                return expansions;
            
            try
            {
                // Handle $$ (process ID expansion)
                if (secretValue.Contains("$$"))
                {
                    trace?.Verbose($"SHELL EXPANSION: Detected $$ in secret, generating process ID variations");
                    
                    // Generate variations with different possible process IDs
                    // Process IDs are typically 1-7 digits on most systems
                    var currentProcessId = Process.GetCurrentProcess().Id.ToString();
                    
                    // Add current process ID expansion
                    string currentExpansion = secretValue.Replace("$$", currentProcessId);
                    expansions.Add(currentExpansion);
                    trace?.Verbose($"SHELL EXPANSION: Added current PID expansion: {MaskForLogging(currentExpansion)}");
                    
                    // Add common process ID patterns (for broader coverage)
                    // Most shells use similar PID ranges during execution
                    var commonPidPatterns = new[]
                    {
                        // Common ranges for process IDs during pipeline execution
                        "1", "123", "1234", "12345", "123456", "1234567",
                        // Some systems start higher
                        "10000", "20000", "30000", "40000", "50000",
                        // Random-ish patterns that might occur
                        "54321", "98765", "13579", "24680"
                    };
                    
                    foreach (var pidPattern in commonPidPatterns)
                    {
                        if (pidPattern != currentProcessId) // Don't duplicate current PID
                        {
                            string expansion = secretValue.Replace("$$", pidPattern);
                            expansions.Add(expansion);
                        }
                    }
                    
                    trace?.Verbose($"SHELL EXPANSION: Generated {expansions.Count} $$ expansions");
                }
                
                // Handle $RANDOM (bash random number expansion)
                if (secretValue.Contains("$RANDOM"))
                {
                    trace?.Verbose($"SHELL EXPANSION: Detected $RANDOM in secret, generating random number variations");
                    
                    // $RANDOM in bash generates 0-32767
                    var randomPatterns = new[]
                    {
                        "0", "1", "123", "1234", "12345", 
                        "32767", "16384", "8192", "4096", "2048",
                        "9999", "5555", "7777", "3333", "1111"
                    };
                    
                    foreach (var randomPattern in randomPatterns)
                    {
                        string expansion = secretValue.Replace("$RANDOM", randomPattern);
                        expansions.Add(expansion);
                    }
                    
                    trace?.Verbose($"SHELL EXPANSION: Generated {randomPatterns.Length} $RANDOM expansions");
                }
                
                // Handle other common variable expansions
                var matches = ShellMetacharRegex.Matches(secretValue);
                foreach (Match match in matches)
                {
                    string metachar = match.Value;
                    if (metachar != "$$" && metachar != "$RANDOM")
                    {
                        trace?.Verbose($"SHELL EXPANSION: Detected other metacharacter: {metachar}");
                        // For other variables, we can't predict the expansion easily
                        // but we log it for awareness
                    }
                }
            }
            catch (Exception ex)
            {
                trace?.Info($"SHELL EXPANSION: Error generating expansions: {ex.Message}");
            }
            
            return expansions;
        }
        
        /// <summary>
        /// Masks a secret value for safe logging
        /// </summary>
        private static string MaskForLogging(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 6)
                return "***";
                
            // Show first 2 and last 2 characters for debugging while keeping it mostly secret
            return $"{value.Substring(0, 2)}***{value.Substring(value.Length - 2)}";
        }
    }
}