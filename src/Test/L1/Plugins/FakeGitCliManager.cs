// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Agent.Plugins.Repository;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Common;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeGitCliManager : GitCliManager
    {
        public FakeGitCliManager(Dictionary<string, string> envs = null) : base(envs)
        { }

        public override async Task LoadGitExecutionInfo(AgentTaskPluginExecutionContext context, bool useBuiltInGit)
        {
            // There is no built-in git for OSX/Linux
            await Task.Delay(1);
            gitPath = "path/to/git";
            gitVersion = await GitVersion(context);
            gitLfsPath = "path/to/gitlfs";
            gitLfsVersion = await GitLfsVersion(context);

            // Set the user agent.
            string gitHttpUserAgentEnv = $"git/{gitVersion.ToString()} (vsts-agent-git/{context.Variables.GetValueOrDefault("agent.version")?.Value ?? "unknown"})";
            context.Debug($"Set git useragent to: {gitHttpUserAgentEnv}.");
            gitEnv["GIT_HTTP_USER_AGENT"] = gitHttpUserAgentEnv;
        }

        public override async Task<Version> GitVersion(AgentTaskPluginExecutionContext context)
        {
            // Return very high version so no min version conflicts.
            await Task.Delay(1);
            return new Version(2, 99999);
        }

        // git lfs version
        public override async Task<Version> GitLfsVersion(AgentTaskPluginExecutionContext context)
        {
            // Return very high version so no min version conflicts.
            await Task.Delay(1);
            return new Version(2, 99999);
        }

        protected override async Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
        {
            await Task.Delay(1);
            return 0;
        }

        protected override async Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> output)
        {
            await Task.Delay(1);
            return 0;
        }

        protected override async Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
        {
            await Task.Delay(1);
            return 0;
        }
    }
}