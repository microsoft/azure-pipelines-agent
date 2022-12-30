// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Microsoft.VisualStudio.Services.Agent
{
    public static class ProcessExtensions
    {
        public static string GetEnvironmentVariable(this Process process, IHostContext hostContext, string variable)
        {
            ArgUtil.NotNull(process, nameof(process));
            ArgUtil.NotNull(hostContext, nameof(hostContext));
            ArgUtil.NotNull(variable, nameof(variable));

            switch (PlatformUtil.HostOS)
            {
                case PlatformUtil.OS.Linux:
                    return GetEnvironmentVariableLinux(process, hostContext, variable);
                case PlatformUtil.OS.OSX:
                    return GetEnvironmentVariableUsingPs(process, hostContext, variable);
                case PlatformUtil.OS.Windows:
                    return WindowsEnvVarHelper.GetEnvironmentVariable(process, hostContext, variable);
            }

            throw new NotImplementedException($"Cannot look up environment variables on {PlatformUtil.HostOS}");
        }

        private static string GetEnvironmentVariableLinux(Process process, IHostContext hostContext, string variable)
        {
            var trace = hostContext.GetTrace(nameof(ProcessExtensions));

            if (!Directory.Exists("/proc"))
            {
                return GetEnvironmentVariableUsingPs(process, hostContext, variable);
            }

            Dictionary<string, string> env = new Dictionary<string, string>();
            string envFile = $"/proc/{process.Id}/environ";
            trace.Info($"Read env from {envFile}");
            string envContent = File.ReadAllText(envFile);
            if (!string.IsNullOrEmpty(envContent))
            {
                // on linux, environment variables are seprated by '\0'
                var envList = envContent.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                foreach (var envStr in envList)
                {
                    // split on the first '='
                    var keyValuePair = envStr.Split('=', 2);
                    if (keyValuePair.Length == 2)
                    {
                        env[keyValuePair[0]] = keyValuePair[1];
                        trace.Verbose($"PID:{process.Id} ({keyValuePair[0]}={keyValuePair[1]})");
                    }
                }
            }

            if (env.TryGetValue(variable, out string envVariable))
            {
                return envVariable;
            }
            else
            {
                return null;
            }
        }

        private static string GetEnvironmentVariableUsingPs(Process process, IHostContext hostContext, string variable)
        {
            // On OSX, there is no /proc folder for us to read environment for given process,
            // So we have call `ps e -p <pid> -o command` to print out env to STDOUT,
            // However, the output env are not format in a parseable way, it's just a string that concatenate all envs with space,
            // It doesn't escape '=' or ' ', so we can't parse the output into a dictionary of all envs.
            // So we only look for the env you request, in the format of variable=value. (it won't work if you variable contains = or space)
            var trace = hostContext.GetTrace(nameof(ProcessExtensions));
            trace.Info($"Read env from output of `ps e -p {process.Id} -o command`");

            Dictionary<string, string> env = new Dictionary<string, string>();
            List<string> psOut = new List<string>();
            object outputLock = new object();
            using (var p = hostContext.CreateService<IProcessInvoker>())
            {
                p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                {
                    if (!string.IsNullOrEmpty(stdout.Data))
                    {
                        lock (outputLock)
                        {
                            psOut.Add(stdout.Data);
                        }
                    }
                };

                p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                {
                    if (!string.IsNullOrEmpty(stderr.Data))
                    {
                        lock (outputLock)
                        {
                            trace.Error(stderr.Data);
                        }
                    }
                };

                int exitCode = p.ExecuteAsync(workingDirectory: hostContext.GetDirectory(WellKnownDirectory.Root),
                                                fileName: "ps",
                                                arguments: $"e -p {process.Id} -o command",
                                                environment: null,
                                                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                if (exitCode == 0)
                {
                    trace.Info($"Successfully dump environment variables for {process.Id}");
                    if (psOut.Count > 0)
                    {
                        string psOutputString = string.Join(" ", psOut);
                        trace.Verbose($"ps output: '{psOutputString}'");

                        int varStartIndex = psOutputString.IndexOf(variable, StringComparison.Ordinal);
                        if (varStartIndex >= 0)
                        {
                            string rightPart = psOutputString.Substring(varStartIndex + variable.Length + 1);
                            if (rightPart.IndexOf(' ') > 0)
                            {
                                string value = rightPart.Substring(0, rightPart.IndexOf(' '));
                                env[variable] = value;
                            }
                            else
                            {
                                env[variable] = rightPart;
                            }

                            trace.Verbose($"PID:{process.Id} ({variable}={env[variable]})");
                        }
                    }
                }
            }

            if (env.TryGetValue(variable, out string envVariable))
            {
                return envVariable;
            }
            else
            {
                return null;
            }
        }
    }
}
