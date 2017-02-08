using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public class ScriptJobExtension : AgentService, IJobExtension
    {
        public Type ExtensionType => typeof(IJobExtension);

        public string HostType => "*";

        public IStep PrepareStep { get; private set; }
        public IStep FinallyStep { get; private set; }

        private string PrepareScript { get; set; }
        private string FinallyScript { get; set; }

        public ScriptJobExtension()
        {
            PrepareScript = Environment.GetEnvironmentVariable("agent.init");
            if (!string.IsNullOrEmpty(PrepareScript))
            {
                PrepareStep = new JobExtensionRunner(
                    runAsync: PrepareAsync,
                    alwaysRun: false,
                    continueOnError: false,
                    critical: true,
                    displayName: StringUtil.Loc("AgentInit"),
                    enabled: true,
                    @finally: false);
            }

            FinallyScript = Environment.GetEnvironmentVariable("agent.cleanup");
            if (!string.IsNullOrEmpty(FinallyScript))
            {
                FinallyStep = new JobExtensionRunner(
                    runAsync: FinallyAsync,
                    alwaysRun: false,
                    continueOnError: false,
                    critical: false,
                    displayName: StringUtil.Loc("AgentCleanup"),
                    enabled: true,
                    @finally: true);
            }
        }

        public void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath)
        {
            repoName = null;
            sourcePath = null;
        }

        public string GetRootedPath(IExecutionContext context, string path)
        {
            return null;
        }

        private Task PrepareAsync()
        {
            // Validate args.
            Trace.Entering();
            ArgUtil.NotNull(PrepareStep, nameof(PrepareStep));
            ArgUtil.NotNull(PrepareStep.ExecutionContext, nameof(PrepareStep.ExecutionContext));
            ArgUtil.NotNullOrEmpty(PrepareScript, nameof(PrepareScript));

            // Run the script
            return this.RunScriptAsync(PrepareStep.ExecutionContext, PrepareScript);
        }

        private Task FinallyAsync()
        {
            // Validate args.
            Trace.Entering();
            ArgUtil.NotNull(FinallyStep, nameof(FinallyStep));
            ArgUtil.NotNull(FinallyStep.ExecutionContext, nameof(FinallyStep.ExecutionContext));
            ArgUtil.NotNullOrEmpty(FinallyScript, nameof(FinallyScript));

            // Run the script
            return this.RunScriptAsync(FinallyStep.ExecutionContext, FinallyScript);
        }

        private async Task RunScriptAsync(IExecutionContext executionContext, string scriptPath)
        {
            // Validate script file.
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException(StringUtil.Loc("FileNotFound", scriptPath));
            }

            // Create the handler data.
            var scriptDirectory = Path.GetDirectoryName(scriptPath);
            var scriptFileName = Path.GetFileName(scriptPath);
            var handlerData = new PowerShellHandlerData()
            {
                Target = scriptFileName,
                WorkingDirectory = scriptDirectory,
            };

            // Create the handler.
            var handlerFactory = HostContext.GetService<IHandlerFactory>();
            IHandler handler = handlerFactory.Create(
                executionContext,
                handlerData,
                new Dictionary<string, string>(),
                taskDirectory: scriptDirectory,
                filePathInputRootDirectory: string.Empty);

            // Run the task.
            await handler.RunAsync();
        }
    }
}