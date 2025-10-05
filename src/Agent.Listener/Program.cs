// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using CommandLine;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    public static class Program
    {
        private static Tracing trace;
        
        public static int Main(string[] args)
        {
            if (PlatformUtil.UseLegacyHttpHandler)
            {
                AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", false);
            }

            // Top-level fatal guard: catch anything thrown before/around HostContext init
            try
            {
                using (HostContext context = new HostContext(HostType.Agent))
                {
                    try
                    {
                        return MainAsync(context, args).GetAwaiter().GetResult();
                    }
                    catch (AggregateException aggEx)
                    {
                        var trace = context.GetTrace("AgentProcess");
                        var flattenedExceptions = aggEx.Flatten().InnerExceptions;
                        trace.Error($"Agent v{BuildConstants.AgentPackage.Version} failed with {flattenedExceptions.Count} aggregated error(s)");
                        
                        bool hasNonRetryable = false;
                        foreach (var innerEx in flattenedExceptions)
                        {
                            trace.Error(innerEx);
                            if (innerEx is NonRetryableException)
                            {
                                hasNonRetryable = true;
                            }
                        }
                        
                        return hasNonRetryable ? Constants.Agent.ReturnCode.TerminatedError : Constants.Agent.ReturnCode.RetryableError;
                    }
                    catch (NonRetryableException nrEx)
                    {
                        var trace = context.GetTrace("AgentProcess");
                        trace.Error(nrEx);
                        return Constants.Agent.ReturnCode.TerminatedError;
                    }
                    catch (OperationCanceledException) when (context.AgentShutdownToken.IsCancellationRequested)
                    {
                        var trace = context.GetTrace("AgentProcess");
                        trace.Info("Agent shutdown requested");
                        return Constants.Agent.ReturnCode.Success;
                    }
                    catch (Exception ex)
                    {
                        var trace = context.GetTrace("AgentProcess");
                        trace.Error(ex);
                        return Constants.Agent.ReturnCode.RetryableError;
                    }
                }
            }
            catch (TypeInitializationException tiEx)
            {
                Console.Error.WriteLine($"FATAL: Agent v{BuildConstants.AgentPackage.Version} initialization failed");
                Console.Error.WriteLine(tiEx.ToString());
                return Constants.Agent.ReturnCode.TerminatedError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL: Agent v{BuildConstants.AgentPackage.Version} startup failed");
                Console.Error.WriteLine(ex.ToString());
                return Constants.Agent.ReturnCode.RetryableError;
            }
        }

        // Return code definition: (this will be used by service host to determine whether it will re-launch agent.listener)
        // 0: Agent exit
        // 1: Terminate failure
        // 2: Retriable failure
        // 3: Exit for self update
        private static async Task<int> MainAsync(IHostContext context, string[] args)
        {
            trace = context.GetTrace("AgentProcess");
            trace.Entering();
            trace.Info($"Agent package {BuildConstants.AgentPackage.PackageName}.");
            trace.Info($"Running on {PlatformUtil.HostOS} ({PlatformUtil.HostArchitecture}).");
            trace.Info($"RuntimeInformation: {RuntimeInformation.OSDescription}.");
            context.WritePerfCounter("AgentProcessStarted");
            var terminal = context.GetService<ITerminal>();

            // TODO: check that the right supporting tools are available for this platform
            // (replaces the check for build platform vs runtime platform)

            try
            {
                trace.Info($"Version: {BuildConstants.AgentPackage.Version}");
                trace.Info($"Commit: {BuildConstants.Source.CommitHash}");
                trace.Info($"Culture: {CultureInfo.CurrentCulture.Name}");
                trace.Info($"UI Culture: {CultureInfo.CurrentUICulture.Name}");
                // Validate directory permissions.
                string agentDirectory = context.GetDirectory(WellKnownDirectory.Root);
                trace.Info($"Validating directory permissions for: '{agentDirectory}'");
                try
                {
                    IOUtil.ValidateExecutePermission(agentDirectory);
                }
                catch (Exception ex)
                {
                    terminal.WriteError(StringUtil.Loc("ErrorOccurred", ex.Message));
                    trace.Error(ex);
                    return Constants.Agent.ReturnCode.TerminatedError;
                }

                if (PlatformUtil.UseLegacyHttpHandler)
                {
                    trace.Warning($"You are using the legacy HTTP handler because you set ${AgentKnobs.LegacyHttpVariableName}.");
                    trace.Warning($"This feature will go away with .NET 6.0, and we recommend you stop using it.");
                    trace.Warning($"It won't be available soon.");
                }

                if (PlatformUtil.RunningOnWindows)
                {
                    trace.Verbose("Configuring Windows-specific settings and validating prerequisites");
                    
                    // Validate PowerShell 3.0 or higher is installed.
                    var powerShellExeUtil = context.GetService<IPowerShellExeUtil>();
                    try
                    {
                        powerShellExeUtil.GetPath();
                        trace.Info("PowerShell validation successful - compatible version found");
                    }
                    catch (Exception ex)
                    {
                        terminal.WriteError(StringUtil.Loc("ErrorOccurred", ex.Message));
                        trace.Error(ex);
                        return Constants.Agent.ReturnCode.TerminatedError;
                    }

                    // Validate .NET Framework 4.5 or higher is installed.
                    if (!NetFrameworkUtil.Test(new Version(4, 5), trace))
                    {
                        terminal.WriteError(StringUtil.Loc("MinimumNetFramework"));
                        trace.Error(".NET Framework version below recommended minimum - functionality may be limited");
                        // warn only, like configurationmanager.cs does. this enables windows edition with just .netcore to work
                    }

                    // Upgrade process priority to avoid Listener starvation
                    using (Process p = Process.GetCurrentProcess())
                    {
                        try
                        {
                            p.PriorityClass = ProcessPriorityClass.AboveNormal;
                        }
                        catch (Exception e)
                        {
                            trace.Warning("Unable to change Windows process priority");
                            trace.Warning(e.Message);
                        }
                    }
                }

                // Add environment variables from .env file
                string envFile = Path.Combine(context.GetDirectory(WellKnownDirectory.Root), ".env");
                if (File.Exists(envFile))
                {
                    var envContents = File.ReadAllLines(envFile);
                    foreach (var env in envContents)
                    {
                        if (!string.IsNullOrEmpty(env) && env.IndexOf('=') > 0)
                        {
                            string envKey = env.Substring(0, env.IndexOf('='));
                            string envValue = env.Substring(env.IndexOf('=') + 1);
                            Environment.SetEnvironmentVariable(envKey, envValue);
                        }
                    }
                    trace.Info($"Successfully loaded {envContents.Length} environment variables from .env file");
                }

                // Parse the command line args.
                var command = new CommandSettings(context, args, new SystemEnvironment());
                trace.Info("Command line arguments parsed successfully - ready for command execution");

                // Print any Parse Errros
                if (command.ParseErrors?.Any() == true)
                {
                    List<string> errorStr = new List<string>();

                    foreach (var error in command.ParseErrors)
                    {
                        if (error is TokenError tokenError)
                        {
                            errorStr.Add(tokenError.Token);
                        }
                        else
                        {
                            // Unknown type of error dump to log
                            terminal.WriteError(StringUtil.Loc("ErrorOccurred", error.Tag));
                        }
                    }

                    terminal.WriteError(
                        StringUtil.Loc("UnrecognizedCmdArgs",
                        string.Join(", ", errorStr)));
                }

                // Defer to the Agent class to execute the command.
                IAgent agent = context.GetService<IAgent>();
                try
                {
                    trace.Verbose("Delegating command execution to Agent service");
                    return await agent.ExecuteCommand(command);
                }
                catch (OperationCanceledException) when (context.AgentShutdownToken.IsCancellationRequested)
                {
                    trace.Info("Agent execution cancelled - graceful shutdown requested");
                    return Constants.Agent.ReturnCode.Success;
                }
                catch (NonRetryableException nrEx)
                {
                    terminal.WriteError(StringUtil.Loc("ErrorOccurred", nrEx.Message));
                    trace.Error(nrEx);
                    return Constants.Agent.ReturnCode.TerminatedError;
                }

            }
            catch (AggregateException aggEx)
            {
                var flattenedExceptions = aggEx.Flatten().InnerExceptions;
                terminal.WriteError(StringUtil.Loc("ErrorOccurred", flattenedExceptions.FirstOrDefault()?.Message ?? aggEx.Message));
                trace.Error($"Agent v{BuildConstants.AgentPackage.Version} failed with {flattenedExceptions.Count} aggregated error(s)");
                
                bool hasNonRetryable = false;
                foreach (var innerEx in flattenedExceptions)
                {
                    trace.Error(innerEx);
                    if (innerEx is NonRetryableException)
                    {
                        hasNonRetryable = true;
                    }
                }
                
                return hasNonRetryable ? Constants.Agent.ReturnCode.TerminatedError : Constants.Agent.ReturnCode.RetryableError;
            }
            catch (TypeInitializationException tiEx)
            {
                terminal.WriteError(StringUtil.Loc("ErrorOccurred", tiEx.Message));
                trace.Error(tiEx);
                return Constants.Agent.ReturnCode.TerminatedError;
            }
            catch (Exception ex)
            {
                terminal.WriteError(StringUtil.Loc("ErrorOccurred", ex.Message));
                trace.Error(ex);
                return Constants.Agent.ReturnCode.RetryableError;
            }
        }
    }
}
