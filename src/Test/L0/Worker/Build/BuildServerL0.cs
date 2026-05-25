// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Xunit;

namespace Test.L0.Worker.Build
{
    public sealed class BuildServerL0
    {
        private const string UseBuildTagsBodyApiEnvVar = "AGENT_USE_BUILD_TAGS_BODY_API";

        // Regression test for tags containing reserved URL characters such as ';' — see
        // https://github.com/microsoft/azure-pipelines-task-lib/issues/1072.
        //
        // BuildServer.AddBuildTag must call the bulk BuildHttpClient.AddBuildTagsAsync overload
        // (which transports tags in the request body) instead of the legacy single-tag
        // AddBuildTagAsync overload (which encodes the tag into the URL path and corrupts
        // characters like ';').
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task AddBuildTag_UsesBodyApi_AndPreservesSemicolonInTag()
        {
            string previous = Environment.GetEnvironmentVariable(UseBuildTagsBodyApiEnvVar);
            try
            {
                // Force-clear the env var so the BuiltInDefault ("true") wins, regardless of
                // anything the host machine may have set.
                Environment.SetEnvironmentVariable(UseBuildTagsBodyApiEnvVar, null);

                const string tag = "foo;bar";
                const int buildId = 42;
                Guid projectId = Guid.NewGuid();

                var mockClient = new Mock<BuildHttpClient>(new Uri("http://localhost"), new VssCredentials());

                IEnumerable<string> capturedTags = null;
                Guid capturedProject = Guid.Empty;
                int capturedBuildId = 0;

                mockClient
                    .Setup(x => x.AddBuildTagsAsync(
                        It.IsAny<IEnumerable<string>>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<object>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<IEnumerable<string>, Guid, int, object, CancellationToken>(
                        (tags, project, id, _, __) =>
                        {
                            capturedTags = tags?.ToArray();
                            capturedProject = project;
                            capturedBuildId = id;
                        })
                    .Returns(Task.FromResult<List<string>>(new List<string> { tag }));

                var server = new Microsoft.VisualStudio.Services.Agent.Worker.Build.BuildServer
                {
                    _buildHttpClient = mockClient.Object
                };

                var result = await server.AddBuildTag(buildId, projectId, tag);

                Assert.NotNull(capturedTags);
                Assert.Equal(new[] { tag }, capturedTags);
                Assert.Equal(projectId, capturedProject);
                Assert.Equal(buildId, capturedBuildId);
                Assert.Contains(tag, result);

                mockClient.Verify(
                    x => x.AddBuildTagsAsync(
                        It.IsAny<IEnumerable<string>>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<object>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                // Guard against accidental revert to the URL-path single-tag overload.
                mockClient.Verify(
                    x => x.AddBuildTagAsync(
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<string>(),
                        It.IsAny<object>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                Environment.SetEnvironmentVariable(UseBuildTagsBodyApiEnvVar, previous);
            }
        }

        // Kill-switch test: when AGENT_USE_BUILD_TAGS_BODY_API=false, BuildServer.AddBuildTag must
        // fall back to the legacy single-tag URL-path overload (AddBuildTagAsync) — providing an
        // operator-controlled rollback path for older on-prem Azure DevOps Server versions that do
        // not accept the body-based endpoint.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task AddBuildTag_FallsBackToLegacyApi_WhenKnobDisabled()
        {
            string previous = Environment.GetEnvironmentVariable(UseBuildTagsBodyApiEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(UseBuildTagsBodyApiEnvVar, "false");

                const string tag = "simple-tag";
                const int buildId = 7;
                Guid projectId = Guid.NewGuid();

                var mockClient = new Mock<BuildHttpClient>(new Uri("http://localhost"), new VssCredentials());

                string capturedTag = null;
                Guid capturedProject = Guid.Empty;
                int capturedBuildId = 0;

                mockClient
                    .Setup(x => x.AddBuildTagAsync(
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<string>(),
                        It.IsAny<object>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<Guid, int, string, object, CancellationToken>(
                        (project, id, t, _, __) =>
                        {
                            capturedProject = project;
                            capturedBuildId = id;
                            capturedTag = t;
                        })
                    .Returns(Task.FromResult<List<string>>(new List<string> { tag }));

                var server = new Microsoft.VisualStudio.Services.Agent.Worker.Build.BuildServer
                {
                    _buildHttpClient = mockClient.Object
                };

                var result = await server.AddBuildTag(buildId, projectId, tag);

                Assert.Equal(tag, capturedTag);
                Assert.Equal(projectId, capturedProject);
                Assert.Equal(buildId, capturedBuildId);
                Assert.Contains(tag, result);

                mockClient.Verify(
                    x => x.AddBuildTagAsync(
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<string>(),
                        It.IsAny<object>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                mockClient.Verify(
                    x => x.AddBuildTagsAsync(
                        It.IsAny<IEnumerable<string>>(),
                        It.IsAny<Guid>(),
                        It.IsAny<int>(),
                        It.IsAny<object>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                Environment.SetEnvironmentVariable(UseBuildTagsBodyApiEnvVar, previous);
            }
        }
    }
}

