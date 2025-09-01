// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (PlatformUtil.UseLegacyHttpHandler)
            {
                AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", false);
            }

            using (HostContext context = new HostContext(HostType.Worker))
            {
                Tracing trace = context.GetTrace(nameof(Program));
                trace.Info($"Worker process entry point initiated [HostType:Worker, Arguments:{string.Join(" ", args ?? new string[0])}]");
                var result = MainAsync(context, args).GetAwaiter().GetResult();
                trace.Info($"Worker process entry point completed [ExitCode:{result}]");
                return result;
            }
        }

        private static async Task<int> MainAsync(IHostContext context, string[] args)
        {
            //ITerminal registers a CTRL-C handler, which keeps the Agent.Worker process running
            //and lets the Agent.Listener handle gracefully the exit.
            var term = context.GetService<ITerminal>();
            Tracing trace = context.GetTrace(nameof(Program));
            try
            {
                trace.Info($"Worker process initialization starting - setting up runtime environment. Version: {BuildConstants.AgentPackage.Version}, Commit: {BuildConstants.Source.CommitHash}, Culture: {CultureInfo.CurrentCulture.Name}, UI Culture: {CultureInfo.CurrentUICulture.Name}");
                context.WritePerfCounter("WorkerProcessStarted");

                // Validate args.
                ArgUtil.NotNull(args, nameof(args));
                ArgUtil.Equal(3, args.Length, nameof(args.Length));
                ArgUtil.NotNullOrEmpty(args[0], $"{nameof(args)}[0]");
                ArgUtil.Equal("spawnclient", args[0].ToLowerInvariant(), $"{nameof(args)}[0]");
                ArgUtil.NotNullOrEmpty(args[1], $"{nameof(args)}[1]");
                ArgUtil.NotNullOrEmpty(args[2], $"{nameof(args)}[2]");
                trace.Info($"Command validation successful [Mode:{args[0]}, PipeIn:{args[1]}, PipeOut:{args[2]}]");
                var worker = context.GetService<IWorker>();

                // Run the worker.
                return await worker.RunAsync(
                    pipeIn: args[1],
                    pipeOut: args[2]);
            }
            catch (AggregateException ex)
            {
                ExceptionsUtil.HandleAggregateException((AggregateException)ex, (message) => trace.Error(message));
            }
            catch (Exception ex)
            {
                // Populate any exception that cause worker failure back to agent.
                Console.WriteLine(ex.ToString());
                try
                {
                    trace.Error(StringUtil.SafeLog("Worker process execution failed with unhandled exception - {0}", ex.Message));
                }
                catch (Exception e)
                {
                    // make sure we don't crash the app on trace error.
                    // since IOException will throw when we run out of disk space.
                    Console.WriteLine(e.ToString());
                }
            }
            trace.Info("Worker process exiting with error code - job execution failed");
            return 1;
        }
    }
}
