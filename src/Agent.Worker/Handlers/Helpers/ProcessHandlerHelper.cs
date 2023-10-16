// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Common;

namespace Agent.Worker.Handlers.Helpers
{
    public static class ProcessHandlerHelper
    {
        private const char _EscapingSymbol = '^';
        private const string _EnvPrefix = "%";
        private const string _EnvPostfix = "%";

        public static (string, CmdTelemetry) ExpandCmdEnv(string inputArgs, Dictionary<string, string> environment)
        {
            ArgUtil.NotNull(inputArgs, nameof(inputArgs));
            ArgUtil.NotNull(environment, nameof(environment));

            string result = inputArgs;
            int startIndex = 0;
            var telemetry = new CmdTelemetry();

            while (true)
            {
                int prefixIndex = result.IndexOf(_EnvPrefix, startIndex);
                if (prefixIndex < 0)
                {
                    break;
                }

                telemetry.FoundPrefixes++;

                if (prefixIndex > 0 && result[prefixIndex - 1] == _EscapingSymbol)
                {
                    telemetry.EscapingSymbolBeforeVar++;
                }

                int envStartIndex = prefixIndex + _EnvPrefix.Length;
                int envEndIndex = FindEnclosingIndex(result, prefixIndex);
                if (envEndIndex == 0)
                {
                    telemetry.NotClosedEnvSyntaxPosition = prefixIndex;
                    break;
                }

                string envName = result[envStartIndex..envEndIndex];
                if (envName.StartsWith(_EscapingSymbol))
                {
                    telemetry.VariablesStartsFromES++;
                }

                var head = result[..prefixIndex];
                if (envName.Contains(_EscapingSymbol, StringComparison.Ordinal))
                {
                    telemetry.VariablesWithESInside++;
                }

                // Since Windows have case-insensetive environment, and Process handler is windows-specific, we should allign this behavior.
                var windowsEnvironment = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);

                // In case we don't have such variable, we just leave it as is
                if (!windowsEnvironment.TryGetValue(envName, out string envValue) || string.IsNullOrEmpty(envValue))
                {
                    telemetry.NotExistingEnv++;
                    startIndex = prefixIndex + 1;
                    continue;
                }

                var tail = result[(envEndIndex + _EnvPostfix.Length)..];

                result = head + envValue + tail;
                startIndex = prefixIndex + envValue.Length;

                telemetry.VariablesExpanded++;

                continue;
            }

            return (result, telemetry);
        }

        private static int FindEnclosingIndex(string input, int targetIndex)
        {
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '%' && i > targetIndex)
                {
                    return i;
                }
            }

            return 0;
        }

        public static (bool, Dictionary<string, object>) ValidateInputArguments(
            string inputArgs,
            Dictionary<string, string> environment,
            IExecutionContext context)
        {
            var enableValidation = AgentKnobs.ProcessHandlerSecureArguments.GetValue(context).AsBoolean();
            context.Debug($"Enable args validation: '{enableValidation}'");
            var enableAudit = AgentKnobs.ProcessHandlerSecureArgumentsAudit.GetValue(context).AsBoolean();
            context.Debug($"Enable args validation audit: '{enableAudit}'");
            var enableTelemetry = AgentKnobs.ProcessHandlerTelemetry.GetValue(context).AsBoolean();
            context.Debug($"Enable telemetry: '{enableTelemetry}'");

            if (enableValidation || enableAudit || enableTelemetry)
            {
                context.Debug("Starting args env expansion");
                var (expandedArgs, envExpandTelemetry) = ExpandCmdEnv(inputArgs, environment);
                context.Debug($"Expanded args={expandedArgs}");

                context.Debug("Starting args sanitization");
                var (sanitizedArgs, sanitizeTelemetry) = CmdArgsSanitizer.SanitizeArguments(expandedArgs);

                Dictionary<string, object> telemetry = null;
                if (sanitizedArgs != inputArgs)
                {
                    if (enableTelemetry)
                    {
                        telemetry = envExpandTelemetry.ToDictionary();
                        if (sanitizeTelemetry != null)
                        {
                            telemetry.AddRange(sanitizeTelemetry.ToDictionary());
                        }
                    }
                    if (sanitizedArgs != expandedArgs)
                    {
                        if (enableAudit && !enableValidation)
                        {
                            context.Warning(StringUtil.Loc("ProcessHandlerInvalidScriptArgs"));
                        }
                        if (enableValidation)
                        {
                            return (false, telemetry);
                        }

                        return (true, telemetry);
                    }
                }

                return (true, null);
            }
            else
            {
                context.Debug("Args sanitization skipped.");
                return (true, null);
            }
        }
    }

    public class CmdTelemetry
    {
        public int FoundPrefixes { get; set; } = 0;
        public int VariablesExpanded { get; set; } = 0;
        public int EscapingSymbolBeforeVar { get; set; } = 0;
        public int VariablesStartsFromES { get; set; } = 0;
        public int VariablesWithESInside { get; set; } = 0;
        public int QuotesNotEnclosed { get; set; } = 0;
        public int NotClosedEnvSyntaxPosition { get; set; } = 0;
        public int NotExistingEnv { get; set; } = 0;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["foundPrefixes"] = FoundPrefixes,
                ["variablesExpanded"] = VariablesExpanded,
                ["escapedVariables"] = EscapingSymbolBeforeVar,
                ["variablesStartsFromES"] = VariablesStartsFromES,
                ["bariablesWithESInside"] = VariablesWithESInside,
                ["quotesNotEnclosed"] = QuotesNotEnclosed,
                ["notClosedBraceSyntaxPosition"] = NotClosedEnvSyntaxPosition,
                ["notExistingEnv"] = NotExistingEnv
            };
        }
    };
}
