using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Expressions;
using Microsoft.TeamFoundation.DistributedTask.ServiceEndpoints;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public interface IJobExtension : IExtension
    {
        HostTypes HostType { get; }
        Task<List<IStep>> InitializeJob(IExecutionContext jobContext, Pipelines.AgentJobRequestMessage message);
        string GetRootedPath(IExecutionContext context, string path);
        void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath);
    }

    public sealed class JobInitializeResult
    {
        private List<IStep> _preJobSteps = new List<IStep>();
        private List<IStep> _jobSteps = new List<IStep>();
        private List<IStep> _postJobSteps = new List<IStep>();

        public List<IStep> PreJobSteps => _preJobSteps;
        public List<IStep> JobSteps => _jobSteps;
        public List<IStep> PostJobStep => _postJobSteps;
    }

    public abstract class JobExtension : AgentService, IJobExtension
    {
        public abstract HostTypes HostType { get; }

        public abstract Type ExtensionType { get; }

        // Anything job extension want to do before building the steps list. This will be deprecated when GetSource move to a task.
        public abstract void InitializeJobExtension(IExecutionContext context);

        // Anything job extension want to add to pre-job steps list. This will be deprecated when GetSource move to a task.
        public abstract IStep GetExtensionPreJobStep(IExecutionContext context);

        // Anything job extension want to add to post-job steps list. This will be deprecated when GetSource move to a task.
        public abstract IStep GetExtensionPostJobStep(IExecutionContext context);

        public abstract string GetRootedPath(IExecutionContext context, string path);

        public abstract void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath);

        // download all required tasks.
        // make sure all task's condition inputs are valid.
        // build up a list of steps for jobrunner.
        public async Task<List<IStep>> InitializeJob(IExecutionContext jobContext, Pipelines.AgentJobRequestMessage message)
        {
            Trace.Entering();
            ArgUtil.NotNull(jobContext, nameof(jobContext));
            ArgUtil.NotNull(message, nameof(message));

            // create a new timeline record node for 'Initialize job'
            IExecutionContext context = jobContext.CreateChild(Guid.NewGuid(), StringUtil.Loc("InitializeJob"), nameof(JobExtension));

            using (var register = jobContext.CancellationToken.Register(() => { context.CancelToken(); }))
            {
                try
                {
                    context.Start();
                    context.Section(StringUtil.Loc("StepStarting", StringUtil.Loc("InitializeJob")));

                    // Give job extension a chance to initialize
                    Trace.Info($"Run initial step from extension {this.GetType().Name}.");
                    InitializeJobExtension(context);

                    // Download tasks if not already in the cache
                    Trace.Info("Downloading task definitions.");
                    var taskManager = HostContext.GetService<ITaskManager>();
                    await taskManager.DownloadAsync(context, message.Steps);

                    // Container preview image env
                    string imageName = context.Variables.Get("_PREVIEW_VSTS_DOCKER_IMAGE");
                    if (string.IsNullOrEmpty(imageName))
                    {
                        imageName = Environment.GetEnvironmentVariable("_PREVIEW_VSTS_DOCKER_IMAGE");
                    }

                    // The preview variable only take affect when none of step has container declared (compat for hosted linux pool)
                    if (!string.IsNullOrEmpty(imageName) && jobContext.Containers.Count == 0)
                    {
                        foreach (var step in message.Steps)
                        {
                            step.Container = "vsts_preview_container";
                        }

                        var dockerContainer = new Pipelines.ContainerReference()
                        {
                            Name = "vsts_preview_container"
                        };
                        dockerContainer.Data["image"] = imageName;
                        jobContext.Containers.Add(new ContainerInfo(dockerContainer));
                    }

                    // build the top level steps list.
                    var stepsBuilder = HostContext.CreateService<IStepsBuilder>();
#if OS_WINDOWS
                    // This is for internal testing and is not publicly supported. This will be removed from the agent at a later time.
                    var prepareScript = Environment.GetEnvironmentVariable("VSTS_AGENT_INIT_INTERNAL_TEMP_HACK");
                    if (!string.IsNullOrEmpty(prepareScript))
                    {
                        var prepareStep = new ManagementScriptStep(
                            scriptPath: prepareScript,
                            condition: ExpressionManager.Succeeded,
                            displayName: "Agent Initialization");

                        Trace.Verbose($"Adding agent init script step.");
                        prepareStep.Initialize(HostContext);
                        ServiceEndpoint systemConnection = context.Endpoints.Single(x => string.Equals(x.Name, ServiceEndpoints.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
                        prepareStep.AccessToken = systemConnection.Authorization.Parameters["AccessToken"];
                        (stepsBuilder as StepsBuilder).AddPreStep(prepareStep);
                    }

                    // Add script post steps.
                    // This is for internal testing and is not publicly supported. This will be removed from the agent at a later time.
                    var finallyScript = Environment.GetEnvironmentVariable("VSTS_AGENT_CLEANUP_INTERNAL_TEMP_HACK");
                    if (!string.IsNullOrEmpty(finallyScript))
                    {
                        var finallyStep = new ManagementScriptStep(
                            scriptPath: finallyScript,
                            condition: ExpressionManager.Always,
                            displayName: "Agent Cleanup");

                        Trace.Verbose($"Adding agent cleanup script step.");
                        finallyStep.Initialize(HostContext);
                        ServiceEndpoint systemConnection = context.Endpoints.Single(x => string.Equals(x.Name, ServiceEndpoints.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
                        finallyStep.AccessToken = systemConnection.Authorization.Parameters["AccessToken"];
                        (stepsBuilder as StepsBuilder).AddPostStep(finallyStep);
                    }
#endif

                    // build the job top level steps
                    stepsBuilder.Build(context, message.Steps);

                    // Add pre-job step from Extension
                    Trace.Info("Adding pre-job step from extension.");
                    var extensionPreJobStep = GetExtensionPreJobStep(context);
                    if (extensionPreJobStep != null)
                    {
                        (stepsBuilder as StepsBuilder).AddPreStep(extensionPreJobStep);
                    }

                    // Add post-job step from Extension
                    Trace.Info("Adding post-job step from extension.");
                    var extensionPostJobStep = GetExtensionPostJobStep(context);
                    if (extensionPostJobStep != null)
                    {
                        (stepsBuilder as StepsBuilder).AddPostStep(extensionPostJobStep);
                    }

                    // create task execution context for all job steps
                    foreach (var step in stepsBuilder.Result)
                    {
                        ArgUtil.NotNull(step, step.DisplayName);
                        step.InitializeStep(jobContext);
                    }

                    return stepsBuilder.Result;
                }
                catch (OperationCanceledException ex) when (jobContext.CancellationToken.IsCancellationRequested)
                {
                    // Log the exception and cancel the JobExtension Initialization.
                    Trace.Error($"Caught cancellation exception from JobExtension Initialization: {ex}");
                    context.Error(ex);
                    context.Result = TaskResult.Canceled;
                    throw;
                }
                catch (Exception ex)
                {
                    // Log the error and fail the JobExtension Initialization.
                    Trace.Error($"Caught exception from JobExtension Initialization: {ex}");
                    context.Error(ex);
                    context.Result = TaskResult.Failed;
                    throw;
                }
                finally
                {
                    context.Section(StringUtil.Loc("StepFinishing", StringUtil.Loc("InitializeJob")));
                    context.Complete();
                }
            }
        }
    }
}