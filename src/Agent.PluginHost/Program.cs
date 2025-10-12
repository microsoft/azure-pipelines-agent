// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.PluginHost
{
    public static class Program
    {
        private static CancellationTokenSource tokenSource = new CancellationTokenSource();
        private static string executingAssemblyLocation = string.Empty;

        public static int Main(string[] args)
        {
            if (PlatformUtil.UseLegacyHttpHandler)
            {
                AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", false);
            }

            Console.CancelKeyPress += Console_CancelKeyPress;

            // Set encoding to UTF8, process invoker will use UTF8 write to STDIN
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            // Top-level fatal guard: catch anything thrown before/during plugin host execution
            try
            {
                return MainAsync(args);
            }
            catch (Exception ex)
            {
                // If plugin host execution fails, write detailed error to stderr
                Console.Error.WriteLine($"[FATAL PluginHost] Plugin host failed: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= Console_CancelKeyPress;
            }
        }

        private static int MainAsync(string[] args)
        {
            try
            {
                ArgUtil.NotNull(args, nameof(args));
                if (args.Length != 2)
                {
                    Console.Error.WriteLine($"[PLUGIN HOST ERROR] Invalid argument count. Expected 2 arguments, got {args.Length}");
                    Console.Error.WriteLine("[PLUGIN HOST ERROR] Usage: Agent.PluginHost.exe <pluginType> <assemblyQualifiedName>");
                    Console.Error.WriteLine("[PLUGIN HOST ERROR] Plugin types: task, command, log");
                    ArgUtil.Equal(2, args.Length, nameof(args.Length));
                }

                string pluginType = args[0];
                if (string.IsNullOrEmpty(pluginType))
                {
                    Console.Error.WriteLine("[PLUGIN HOST ERROR] Plugin type cannot be null or empty");
                    throw new ArgumentNullException(nameof(pluginType), "Plugin type cannot be null or empty");
                }

                if (string.Equals("task", pluginType, StringComparison.OrdinalIgnoreCase))
                {
                    string assemblyQualifiedName = args[1];
                    ArgUtil.NotNullOrEmpty(assemblyQualifiedName, nameof(assemblyQualifiedName));

                    string serializedContext = Console.ReadLine();
                    ArgUtil.NotNullOrEmpty(serializedContext, nameof(serializedContext));

                    AgentTaskPluginExecutionContext executionContext = StringUtil.ConvertFromJson<AgentTaskPluginExecutionContext>(serializedContext);
                    ArgUtil.NotNull(executionContext, nameof(executionContext));

                    VariableValue culture;
                    ArgUtil.NotNull(executionContext.Variables, nameof(executionContext.Variables));
                    if (executionContext.Variables.TryGetValue("system.culture", out culture) &&
                        !string.IsNullOrEmpty(culture?.Value))
                    {
                        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(culture.Value);
                        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(culture.Value);
                    }

                    AssemblyLoadContext.Default.Resolving += ResolveAssembly;
                    try
                    {
                        Type type = Type.GetType(assemblyQualifiedName, throwOnError: true);
                        var taskPlugin = Activator.CreateInstance(type) as IAgentTaskPlugin;
                        ArgUtil.NotNull(taskPlugin, nameof(taskPlugin));
                        taskPlugin.RunAsync(executionContext, tokenSource.Token).GetAwaiter().GetResult();
                    }
                    catch (SocketException ex)
                    {
                        ExceptionsUtil.HandleSocketException(ex, executionContext.VssConnection.Uri.ToString(), executionContext.Error);
                    }
                    catch (AggregateException ex)
                    {
                        ExceptionsUtil.HandleAggregateException(ex, executionContext.Error);
                    }
                    catch (Exception ex)
                    {
                        executionContext.Error($"[TASK PLUGIN ERROR] {ex.GetType().Name}: {ex.Message}");
                        executionContext.Debug($"[TASK PLUGIN ERROR] Stack trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            executionContext.Debug($"[TASK PLUGIN ERROR] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        }
                    }
                    finally
                    {
                        AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
                    }

                    return 0;
                }
                else if (string.Equals("command", pluginType, StringComparison.OrdinalIgnoreCase))
                {
                    string assemblyQualifiedName = args[1];
                    ArgUtil.NotNullOrEmpty(assemblyQualifiedName, nameof(assemblyQualifiedName));

                    string serializedContext = Console.ReadLine();
                    ArgUtil.NotNullOrEmpty(serializedContext, nameof(serializedContext));

                    AgentCommandPluginExecutionContext executionContext = StringUtil.ConvertFromJson<AgentCommandPluginExecutionContext>(serializedContext);
                    ArgUtil.NotNull(executionContext, nameof(executionContext));

                    AssemblyLoadContext.Default.Resolving += ResolveAssembly;
                    try
                    {
                        Type type = Type.GetType(assemblyQualifiedName, throwOnError: true);
                        var commandPlugin = Activator.CreateInstance(type) as IAgentCommandPlugin;
                        ArgUtil.NotNull(commandPlugin, nameof(commandPlugin));
                        commandPlugin.ProcessCommandAsync(executionContext, tokenSource.Token).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        // any exception throw from plugin will fail the command.
                        executionContext.Error($"[COMMAND PLUGIN ERROR] Command plugin failed: {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            executionContext.Error($"[COMMAND PLUGIN ERROR] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        }
                        executionContext.Error($"[COMMAND PLUGIN ERROR] Full details: {ex.ToString()}");
                    }
                    finally
                    {
                        AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
                    }

                    return 0;
                }
                else if (string.Equals("log", pluginType, StringComparison.OrdinalIgnoreCase))
                {
                    // read commandline arg to get the instance id
                    var instanceId = args[1];
                    ArgUtil.NotNullOrEmpty(instanceId, nameof(instanceId));

                    // read STDIN, the first line will be the HostContext for the log plugin host
                    string serializedContext = Console.ReadLine();
                    ArgUtil.NotNullOrEmpty(serializedContext, nameof(serializedContext));
                    AgentLogPluginHostContext hostContext = StringUtil.ConvertFromJson<AgentLogPluginHostContext>(serializedContext);
                    ArgUtil.NotNull(hostContext, nameof(hostContext));

                    // create plugin object base on plugin assembly names from the HostContext
                    List<IAgentLogPlugin> logPlugins = new List<IAgentLogPlugin>();
                    AssemblyLoadContext.Default.Resolving += ResolveAssembly;
                    try
                    {
                        foreach (var pluginAssembly in hostContext.PluginAssemblies)
                        {
                            try
                            {
                                Type type = Type.GetType(pluginAssembly, throwOnError: true);
                                var logPlugin = Activator.CreateInstance(type) as IAgentLogPlugin;
                                ArgUtil.NotNull(logPlugin, nameof(logPlugin));
                                logPlugins.Add(logPlugin);
                            }
                            catch (Exception ex)
                            {
                                // any exception throw from plugin will get trace and ignore, error from plugins will not fail the job.
                                Console.WriteLine($"[PLUGIN ERROR] Unable to load plugin '{pluginAssembly}': {ex.GetType().Name}: {ex.Message}");
                                Console.WriteLine($"[PLUGIN ERROR] Plugin loading failed - continuing without this plugin");
                            }
                        }
                    }
                    finally
                    {
                        AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
                    }

                    // start the log plugin host
                    var logPluginHost = new AgentLogPluginHost(hostContext, logPlugins);
                    Task hostTask = logPluginHost.Run();
                    while (true)
                    {
                        var consoleInput = Console.ReadLine();
                        if (string.Equals(consoleInput, $"##vso[logplugin.finish]{instanceId}", StringComparison.OrdinalIgnoreCase))
                        {
                            // singal all plugins, the job has finished.
                            // plugin need to start their finalize process.
                            logPluginHost.Finish();
                            break;
                        }
                        else
                        {
                            // the format is TimelineRecordId(GUID):Output(String)
                            logPluginHost.EnqueueOutput(consoleInput);
                        }
                    }

                    // wait for the host to finish.
                    hostTask.GetAwaiter().GetResult();

                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"[PLUGIN HOST ERROR] Unknown plugin type: '{pluginType}'. Supported types: task, command, log");
                    throw new ArgumentOutOfRangeException(nameof(pluginType), pluginType, $"Unknown plugin type: '{pluginType}'. Supported types: task, command, log");
                }
            }
            catch (Exception ex)
            {
                // Enhanced infrastructure failure reporting with context
                Console.Error.WriteLine($"[FATAL PluginHost] Infrastructure failure in plugin host: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static Assembly ResolveAssembly(AssemblyLoadContext context, AssemblyName assembly)
        {
            string assemblyFilename = assembly.Name + ".dll";
            if (string.IsNullOrEmpty(executingAssemblyLocation))
            {
                executingAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            return context.LoadFromAssemblyPath(Path.Combine(executingAssemblyLocation, assemblyFilename));
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            tokenSource.Cancel();
        }
    }
}
