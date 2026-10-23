// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Xunit;

namespace Test.L0.Worker.Build
{
    public sealed class BuildServerL0
    {
        private const string _knobName = "AGENT_USE_BUILD_TAGS_BODY_API";

        // Minimal IKnobValueContext stub that exposes a settable pipeline-variable map and a
        // settable in-memory environment scope, so the knob's RuntimeKnobSource and
        // EnvironmentKnobSource can be exercised without depending on the real agent host or
        // ambient process environment.
        private sealed class TestKnobContext : IKnobValueContext
        {
            private readonly IDictionary<string, string> _variables;
            private readonly Dictionary<string, string> _envVars;

            public TestKnobContext(
                IDictionary<string, string> variables = null,
                Dictionary<string, string> envVars = null)
            {
                _variables = variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _envVars = envVars ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public string GetVariableValueOrDefault(string variableName)
            {
                _variables.TryGetValue(variableName, out var value);
                return value;
            }

            public IScopedEnvironment GetScopedEnvironment() => new LocalEnvironment(_envVars);
        }

        // Regression test for tags containing reserved URL characters such as ';' — see
        // https://github.com/microsoft/azure-pipelines-task-lib/issues/1072.
        // When the knob is enabled via a pipeline runtime variable, BuildServer.AddBuildTag must
        // call the body-based AddBuildTagsAsync overload instead of the legacy AddBuildTagAsync.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task AddBuildTag_UsesBodyApi_AndPreservesSemicolonInTag_WhenKnobEnabledViaRuntimeVariable()
        {
            const string tag = "foo;bar";
            const int buildId = 42;
            Guid projectId = Guid.NewGuid();

            var knobContext = new TestKnobContext(
                variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { _knobName, "true" }
                });

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
            using var hc = new TestHostContext(this);
            server.Initialize(hc);

            var result = await server.AddBuildTag(buildId, projectId, tag, knobContext);

            Assert.Equal(new[] { tag }, capturedTags);
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
                Times.Never);
        }

        // Default behavior (knob unset → BuiltInDefault "false") falls back to the legacy
        // URL-path overload. Locks in the kill-switch default.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task AddBuildTag_UsesLegacyApi_ByDefault()
        {
            const string tag = "simple-tag";
            const int buildId = 7;
            Guid projectId = Guid.NewGuid();

            var knobContext = new TestKnobContext();

            var mockClient = new Mock<BuildHttpClient>(new Uri("http://localhost"), new VssCredentials());

            string capturedTag = null;

            mockClient
                .Setup(x => x.AddBuildTagAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Guid, int, string, object, CancellationToken>(
                    (_, __, t, ___, ____) => { capturedTag = t; })
                .Returns(Task.FromResult<List<string>>(new List<string> { tag }));

            var server = new Microsoft.VisualStudio.Services.Agent.Worker.Build.BuildServer
            {
                _buildHttpClient = mockClient.Object
            };
            using var hc = new TestHostContext(this);
            server.Initialize(hc);

            var result = await server.AddBuildTag(buildId, projectId, tag, knobContext);

            Assert.Equal(tag, capturedTag);
            Assert.Contains(tag, result);

            mockClient.Verify(
                x => x.AddBuildTagsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // Verifies the EnvironmentKnobSource path: knob enabled via the agent-host env var (not
        // a pipeline runtime variable). Same expected behavior as the runtime-variable test.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task AddBuildTag_UsesBodyApi_WhenKnobEnabledViaEnvironmentVariable()
        {
            const string tag = "env-tag;with;semis";
            const int buildId = 99;
            Guid projectId = Guid.NewGuid();

            var knobContext = new TestKnobContext(
                envVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { _knobName, "true" }
                });

            var mockClient = new Mock<BuildHttpClient>(new Uri("http://localhost"), new VssCredentials());

            IEnumerable<string> capturedTags = null;

            mockClient
                .Setup(x => x.AddBuildTagsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<string>, Guid, int, object, CancellationToken>(
                    (tags, _, __, ___, ____) => { capturedTags = tags?.ToArray(); })
                .Returns(Task.FromResult<List<string>>(new List<string> { tag }));

            var server = new Microsoft.VisualStudio.Services.Agent.Worker.Build.BuildServer
            {
                _buildHttpClient = mockClient.Object
            };
            using var hc = new TestHostContext(this);
            server.Initialize(hc);

            var result = await server.AddBuildTag(buildId, projectId, tag, knobContext);

            Assert.Equal(new[] { tag }, capturedTags);
            Assert.Contains(tag, result);

            mockClient.Verify(
                x => x.AddBuildTagAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
