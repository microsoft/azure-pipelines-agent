// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.PipelineArtifact;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Plugin
{
    public sealed class PipelineArtifactPluginV2L0
    {
        // Exposes the protected helper so the triggering-build resolution can be unit tested in isolation.
        private sealed class TestablePipelineArtifactPluginV2 : PipelineArtifactTaskPluginBaseV2
        {
            public override Guid Id => Guid.Empty;

            protected override Task ProcessCommandInternalAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
                => Task.CompletedTask;

            public int Resolve(AgentTaskPluginExecutionContext context, string pipelineDefinition)
                => ResolveTriggeringPipelineId(context, pipelineDefinition);
        }

        private static AgentTaskPluginExecutionContext CreateContext()
        {
            return new AgentTaskPluginExecutionContext();
        }

        // Regression test for the operator-precedence bug: with the fix the '.definitionId'/'.buildId'
        // suffixes are appended, so the release triggering build is correctly resolved instead of
        // silently falling back to the latest successful build.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveTriggeringPipelineId_ReleaseHost_SelectsTriggeringBuild()
        {
            var context = CreateContext();
            context.Variables["system.hostType"] = new VariableValue("Release");
            context.Variables["release.triggeringartifact.alias"] = new VariableValue("drop");
            context.Variables["release.artifacts.drop.definitionId"] = new VariableValue("42");
            context.Variables["release.artifacts.drop.buildId"] = new VariableValue("1001");

            int pipelineId = new TestablePipelineArtifactPluginV2().Resolve(context, "42");

            Assert.Equal(1001, pipelineId);
        }

        // When the triggering artifact belongs to a different definition than the configured pipeline,
        // no triggering build applies and callers must fall back to the configured runVersion.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveTriggeringPipelineId_ReleaseHost_DefinitionMismatch_ReturnsZero()
        {
            var context = CreateContext();
            context.Variables["system.hostType"] = new VariableValue("Release");
            context.Variables["release.triggeringartifact.alias"] = new VariableValue("drop");
            context.Variables["release.artifacts.drop.definitionId"] = new VariableValue("99");
            context.Variables["release.artifacts.drop.buildId"] = new VariableValue("1001");

            int pipelineId = new TestablePipelineArtifactPluginV2().Resolve(context, "42");

            Assert.Equal(0, pipelineId);
        }

        // A missing triggering-artifact alias must not throw and must fall back (return 0).
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveTriggeringPipelineId_ReleaseHost_MissingAlias_ReturnsZero()
        {
            var context = CreateContext();
            context.Variables["system.hostType"] = new VariableValue("Release");

            int pipelineId = new TestablePipelineArtifactPluginV2().Resolve(context, "42");

            Assert.Equal(0, pipelineId);
        }

        // The build-host path uses the build.triggeredBy.* variables and must keep working.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveTriggeringPipelineId_BuildHost_SelectsTriggeringBuild()
        {
            var context = CreateContext();
            context.Variables["system.hostType"] = new VariableValue("Build");
            context.Variables["build.triggeredBy.definitionId"] = new VariableValue("42");
            context.Variables["build.triggeredBy.buildId"] = new VariableValue("1001");

            int pipelineId = new TestablePipelineArtifactPluginV2().Resolve(context, "42");

            Assert.Equal(1001, pipelineId);
        }
    }
}
