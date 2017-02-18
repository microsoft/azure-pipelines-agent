using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines;
using YamlDotNet.Serialization;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    [ServiceLocator(Default = typeof(LocalRunner))]
    public interface ILocalRunner : IAgentService
    {
        Task<int> RunAsync(CommandSettings command, CancellationToken token);
    }

    public sealed class LocalRunner : AgentService, ILocalRunner
    {
        public async Task<int> RunAsync(CommandSettings command, CancellationToken token)
        {
            Trace.Info(nameof(RunAsync));
            var terminal = HostContext.GetService<ITerminal>();
            var configStore = HostContext.GetService<IConfigurationStore>();
            AgentSettings settings = configStore.GetSettings();

            // Load the YAML file.
            string yamlFile = command.GetYaml();
            ArgUtil.File(yamlFile, nameof(yamlFile));
            var pipeline = await PipelineParser.LoadAsync(yamlFile);
            ArgUtil.NotNull(pipeline, nameof(pipeline));
            if (command.WhatIf)
            {
                // What-if mode.
                var yamlSerializer = new Serializer();
                terminal.WriteLine(yamlSerializer.Serialize(pipeline));
                return 0;
            }

            // Create job message.
            IJobDispatcher jobDispatcher = null;
            try
            {
                jobDispatcher = HostContext.CreateService<IJobDispatcher>();
                AgentJobRequestMessage newJobMessage = GetJobMessage();
                newJobMessage.Environment.Variables[Constants.Variables.Agent.RunMode] = RunMode.Local.ToString();
                jobDispatcher.Run(newJobMessage);
                await jobDispatcher.WaitAsync(token);
            }
            finally
            {
                if (jobDispatcher != null)
                {
                    await jobDispatcher.ShutdownAsync();
                }
            }

            return Constants.Agent.ReturnCode.Success;
        }

        private static AgentJobRequestMessage GetJobMessage()
        {
            const string Message = @"{
  ""tasks"": [
    {
      ""instanceId"": ""00000000-0000-0000-0000-000000000001"",
      ""displayName"": ""Run echo"",
      ""enabled"": true,
      ""continueOnError"": false,
      ""alwaysRun"": false,
      ""timeoutInMinutes"": 0,
      ""id"": ""d9bafed4-0b18-4f58-968d-86655b4d2ce9"",
      ""name"": ""CmdLine"",
      ""version"": ""1.1.2"",
      ""inputs"": {
        ""filename"": ""echo"",
        ""arguments"": ""##vso[task.setvariable variable=myfancysecret;issecret=true]mysecretvalue"",
        ""workingFolder"": """",
        ""failOnStandardError"": ""false""
      }
    },
    {
      ""instanceId"": ""00000000-0000-0000-0000-000000000002"",
      ""displayName"": ""Run echo"",
      ""enabled"": true,
      ""continueOnError"": false,
      ""alwaysRun"": false,
      ""timeoutInMinutes"": 0,
      ""id"": ""d9bafed4-0b18-4f58-968d-86655b4d2ce9"",
      ""name"": ""CmdLine"",
      ""version"": ""1.1.2"",
      ""inputs"": {
        ""filename"": ""echo"",
        ""arguments"": ""$(myfancysecret)"",
        ""workingFolder"": """",
        ""failOnStandardError"": ""false""
      }
    }
  ],
  ""requestId"": 1860,
  ""lockToken"": ""00000000-0000-0000-0000-000000000000"",
  ""lockedUntil"": ""0001-01-01T00:00:00"",
  ""messageType"": ""JobRequest"",
  ""plan"": {
    ""scopeIdentifier"": ""00000000-0000-0000-0000-000000000000"",
    ""planType"": ""Build"",
    ""version"": 8,
    ""planId"": ""00000000-0000-0000-0000-000000000000"",
    ""artifactUri"": ""vstfs:///Build/Build/1864"",
    ""artifactLocation"": null
  },
  ""timeline"": {
    ""id"": ""00000000-0000-0000-0000-000000000000"",
    ""changeId"": 1,
    ""location"": null
  },
  ""jobId"": ""00000000-0000-0000-0000-000000000000"",
  ""jobName"": ""Build"",
  ""environment"": {
    ""endpoints"": [
      {
        ""data"": {
          ""repositoryId"": ""00000000-0000-0000-0000-000000000000"",
          ""rootFolder"": null,
          ""clean"": ""false"",
          ""checkoutSubmodules"": ""False"",
          ""onpremtfsgit"": ""False"",
          ""fetchDepth"": ""0"",
          ""gitLfsSupport"": ""false"",
          ""skipSyncSource"": ""true"",
          ""cleanOptions"": ""0""
        },
        ""name"": ""gitTest"",
        ""type"": ""TfsGit"",
        ""url"": ""https://127.0.0.1/vsts-agent-local-runner/_git/gitTest"",
        ""authorization"": {
          ""parameters"": {
            ""AccessToken"": ""dummy-access-token""
          },
          ""scheme"": ""OAuth""
        },
        ""isReady"": false
      }
    ],
    ""mask"": [
      {
        ""type"": ""regex"",
        ""value"": ""dummy-access-token""
      }
    ],
    ""variables"": {
      ""system"": ""build"",
      ""system.collectionId"": ""00000000-0000-0000-0000-000000000000"",
      ""system.teamProject"": ""gitTest"",
      ""system.teamProjectId"": ""00000000-0000-0000-0000-000000000000"",
      ""system.definitionId"": ""55"",
      ""build.definitionName"": ""My Build Definition Name"",
      ""build.definitionVersion"": ""1"",
      ""build.queuedBy"": ""John Doe"",
      ""build.queuedById"": ""00000000-0000-0000-0000-000000000000"",
      ""build.requestedFor"": ""John Doe"",
      ""build.requestedForId"": ""00000000-0000-0000-0000-000000000000"",
      ""build.requestedForEmail"": ""john.doe@contoso.com"",
      ""build.sourceVersion"": ""55ba1763b74d42a758514b466b7ea931aedbc941"",
      ""build.sourceBranch"": ""refs/heads/master"",
      ""build.sourceBranchName"": ""master"",
      ""system.debug"": ""true"",
      ""system.culture"": ""en-US"",
      ""build.clean"": """",
      ""build.buildId"": ""1863"",
      ""build.buildUri"": ""vstfs:///Build/Build/1863"",
      ""build.buildNumber"": ""1863"",
      ""//build.containerId"": ""123456"",
      ""system.isScheduled"": ""False"",
      ""system.hosttype"": ""build"",
      ""system.teamFoundationCollectionUri"": ""https://127.0.0.1/vsts-agent-local-runner"",
      ""system.taskDefinitionsUri"": ""https://127.0.0.1/vsts-agent-local-runner"",
      ""AZURE_HTTP_USER_AGENT"": ""VSTS_00000000-0000-0000-0000-000000000000_build_55_1863"",
      ""MSDEPLOY_HTTP_USER_AGENT"": ""VSTS_00000000-0000-0000-0000-000000000000_build_55_1863"",
      ""system.planId"": ""00000000-0000-0000-0000-000000000000"",
      ""system.jobId"": ""00000000-0000-0000-0000-000000000000"",
      ""system.timelineId"": ""00000000-0000-0000-0000-000000000000"",
      ""build.repository.uri"": ""https://127.0.0.1/vsts-agent-local-runner/_git/gitTest"",
      ""build.sourceVersionAuthor"": ""John Doe"",
      ""build.sourceVersionMessage"": ""Updated Program.cs""
    },
    ""systemConnection"": {
      ""data"": {
        ""ServerId"": ""00000000-0000-0000-0000-000000000000"",
        ""ServerName"": ""127.0.0.1""
      },
      ""name"": ""SystemVssConnection"",
      ""url"": ""https://127.0.0.1/vsts-agent-local-runner"",
      ""authorization"": {
        ""parameters"": {
          ""AccessToken"": ""dummy-access-token""
        },
        ""scheme"": ""OAuth""
      },
      ""isReady"": false
    }
  }
}";
            return JsonUtility.FromString<AgentJobRequestMessage>(Message);
        }
    }
}