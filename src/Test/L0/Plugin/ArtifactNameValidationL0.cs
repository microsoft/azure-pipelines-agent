// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins;
using Agent.Plugins.PipelineArtifact;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Plugin
{
    public sealed class ArtifactNameValidationL0 : IDisposable
    {
        private const string KnobVariable = "agent.EnableArtifactNameValidation";
        private readonly string target = Path.Combine(TestUtil.GetSrcPath(), "Test", "TestResults", Guid.NewGuid().ToString("N"));
        private readonly List<string> output = new List<string>();
        private readonly Mock<BuildHttpClient> client = new Mock<BuildHttpClient>(MockBehavior.Strict, new Uri("https://example.invalid"), new VssCredentials());
        private readonly VssHttpMessageHandler handler = new VssHttpMessageHandler(new VssCredentials(), VssClientHttpRequestSettings.Default);
        private readonly VssConnection connection;
        private List<BuildArtifact> artifacts = new List<BuildArtifact> { Artifact("../outside", "Unsupported") };

        public ArtifactNameValidationL0()
        {
            var http = new Mock<DelegatingHandler>();
            http.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Unexpected HTTP request in unit test."));
            connection = new VssConnection(new Uri("https://example.invalid"), handler, new[] { http.Object });
            var cache = (IDictionary)typeof(VssConnection).GetField("m_cachedTypes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(connection);
            object serviceId = typeof(VssConnection).GetMethod("GetServiceIdentifier", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(connection, new object[] { typeof(BuildHttpClient) });
            object key = Activator.CreateInstance(cache.GetType().GenericTypeArguments[0], typeof(BuildHttpClient), serviceId);
            cache.Add(key, client.Object);
            Assert.Same(client.Object, connection.GetClient<BuildHttpClient>());
            client.Setup(x => x.GetArtifactsAsync(It.IsAny<Guid>(), 1, null, It.IsAny<CancellationToken>())).ReturnsAsync(() => artifacts);
            client.Setup(x => x.GetArtifactsAsync(It.IsAny<string>(), 1, null, It.IsAny<CancellationToken>())).ReturnsAsync(() => artifacts);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void FilterKeepsOriginalValidRecordsAndWarnsForInvalidMetadata()
        {
            var context = Context();
            var valid = new[] { Artifact("drop", "Container"), Artifact("release-1.2", "PipelineArtifact"), Artifact("\u53d1\u5e03", "FilePath") };
            var invalid = new[] { null, "", ".", "..", " . ", "../outside", @"..\outside", @"C:\root", @"\\server\share", "a\0b", "a:b" };
            var records = valid.Concat(invalid.Select(name => new BuildArtifact { Name = name })).ToList();

            Assert.Equal(valid, PipelineArtifactServer.FilterArtifactsWithValidNames(context, records));
            Assert.Equal(invalid.Length, Warnings().Count);
            AssertWarningOnly();
            Assert.False(Directory.Exists(target));
        }

        [Theory]
        [InlineData("current", null)]
        [InlineData("specific", "false")]
        [InlineData("current", "true")]
        [InlineData("specific", "true")]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task DownloadAllUsesFlagForBothSourcesAndKeepsOriginalTargetCreation(string source, string flag)
        {
            bool enabled = flag == "true";
            var context = Context(flag, source);
            if (enabled)
            {
                artifacts = new List<BuildArtifact>
                {
                    Artifact("../outside", "Container"), Artifact("..", "PipelineArtifact"),
                    Artifact("", "FilePath"), new BuildArtifact { Name = null }
                };
            }

            await new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None);

            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.GetFileSystemEntries(target));
            Assert.Equal(enabled ? 4 : 0, Warnings().Count);
            Assert.Equal("", context.Variables["DownloadPipelineArtifactResourceTypes"].Value);
            AssertWarningOnly();
            client.Verify(x => x.GetArtifactsAsync(It.IsAny<Guid>(), 1, null, It.IsAny<CancellationToken>()),
                source == "current" ? Times.Once() : Times.Never());
            client.Verify(x => x.GetArtifactsAsync(It.IsAny<string>(), 1, null, It.IsAny<CancellationToken>()),
                source == "specific" ? Times.Once() : Times.Never());
        }

        [Theory]
        [InlineData("false")]
        [InlineData("true")]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task ExplicitInvalidSelectorRetainsOriginalError(string flag)
        {
            var context = Context(flag);
            context.Inputs["artifact"] = "../outside";
            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None));
            Assert.Equal(StringUtil.Loc("ArtifactNameIsNotValid", "../outside"), error.Message);
            Assert.Empty(Warnings());
            Assert.False(Directory.Exists(target));
            client.VerifyNoOtherCalls();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RetrievalFailurePropagatesAfterOriginalTargetCreation()
        {
            var failure = new IOException("Artifact retrieval failed");
            client.Setup(x => x.GetArtifactsAsync(It.IsAny<Guid>(), 1, null, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
            Assert.Same(failure, await Record.ExceptionAsync(() =>
                new DownloadPipelineArtifactTaskV2_0_0().RunAsync(Context("true"), CancellationToken.None)));
            Assert.True(Directory.Exists(target));
            Assert.Empty(Warnings());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task NamedDownloadDoesNotFilterReturnedMetadataOrChangeLegacySelector()
        {
            var context = Context("true");
            context.Inputs["artifact"] = " . ";
            client.Setup(x => x.GetArtifactAsync(It.IsAny<Guid>(), 1, " . ", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Artifact("../outside", "Unsupported"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None));
            client.Verify(x => x.GetArtifactAsync(It.IsAny<Guid>(), 1, " . ", null, It.IsAny<CancellationToken>()), Times.Once());
            Assert.True(Directory.Exists(target));
            Assert.Empty(Warnings());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task OtherDownloadCallersDoNotOptIntoFiltering()
        {
            var context = Context("true");
            await new PipelineArtifactServer(null).DownloadAsyncV2(context,
                new ArtifactDownloadParameters { ProjectId = Guid.NewGuid(), PipelineId = 1 },
                DownloadOptions.MultiDownload, CancellationToken.None);
            Assert.Empty(Warnings());
            Assert.Equal("", context.Variables["DownloadPipelineArtifactResourceTypes"].Value);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task InheritedEnvironmentEnablesFilteringAndRuntimeFalseOverrides()
        {
            var context = Context();
            bool enabled = StringUtil.ConvertToBoolean(Environment.GetEnvironmentVariable("AZP_AGENT_ENABLE_ARTIFACT_NAME_VALIDATION"));
            var plugin = new DownloadPipelineArtifactTaskV2_0_0();
            await plugin.RunAsync(context, CancellationToken.None);
            Assert.Equal(enabled ? 1 : 0, Warnings().Count);

            context.Variables[KnobVariable] = "false";
            output.Clear();
            await plugin.RunAsync(context, CancellationToken.None);
            Assert.Empty(Warnings());
            AssertWarningOnly();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void WarningEscapesNameAndRetainsOriginalRestrictionTextWithoutGlobalLocalization()
        {
            string localizationBefore = StringUtil.Loc("MissingNodePath");
            const string name = "invalid\r\n##vso[task.complete result=Failed;]%0A";
            var context = Context();
            context.Variables["DECODE_PERCENTS"] = "true";
            Assert.Empty(PipelineArtifactServer.FilterArtifactsWithValidNames(context, new[] { Artifact(name, "Container") }));
            Assert.Single(Warnings());
            AssertWarningOnly();
            output.Clear();

            var resources = IOUtil.LoadObject<Dictionary<string, object>>(Path.Combine(TestUtil.GetSrcPath(), "Misc", "layoutbin", "en-US", "strings.json"));
            string error = (string)resources["ArtifactNameIsNotValid"];
            string warning = (string)resources["SkippingArtifactWithInvalidName"];
            Assert.Equal(error.Substring(error.IndexOf("It cannot contain", StringComparison.Ordinal)),
                warning.Substring(warning.IndexOf("It cannot contain", StringComparison.Ordinal)));
            string message = StringUtil.Format(warning, name);
            context.Warning(message);
            string commandText = Assert.Single(Warnings());
            Assert.DoesNotContain("\r", commandText);
            Assert.DoesNotContain("\n", commandText);
            Assert.Contains("%0D%0A", commandText);
            Assert.Contains("%AZP250A", commandText);
            Assert.True(Command.TryParse(commandText, true, out Command command));
            Assert.Equal("logissue", command.Event);
            Assert.Equal("warning", command.Properties["type"]);
            Assert.Equal(message, command.Data);
            Assert.Equal(localizationBefore, StringUtil.Loc("MissingNodePath"));
        }

        private AgentTaskPluginExecutionContext Context(string flag = null, string source = "current")
        {
            var trace = new Mock<ITraceWriter>();
            trace.Setup(x => x.Info(It.IsAny<string>(), It.IsAny<string>())).Callback((string message, string operation) => output.Add(message));
            var context = new AgentTaskPluginExecutionContext(trace.Object);
            typeof(AgentTaskPluginExecutionContext).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, connection);
            context.Inputs["source"] = source;
            context.Inputs["artifact"] = "";
            context.Inputs["path"] = target;
            context.Inputs["tags"] = "";
            context.Inputs["project"] = Guid.NewGuid().ToString();
            context.Inputs["runVersion"] = "specific";
            context.Inputs["runId"] = "1";
            context.Variables["system.defaultworkingdirectory"] = TestUtil.GetSrcPath();
            context.Variables["system.servertype"] = "Hosted";
            context.Variables["system.teamProjectId"] = context.Inputs["project"];
            context.Variables["build.buildId"] = "1";
            if (flag != null) context.Variables[KnobVariable] = flag;
            return context;
        }

        private static BuildArtifact Artifact(string name, string type) =>
            new BuildArtifact { Name = name, Resource = new ArtifactResource { Type = type } };

        private List<string> Warnings() => output.Where(line => line.StartsWith("##vso[task.logissue type=warning;]", StringComparison.Ordinal)).ToList();

        private void AssertWarningOnly()
        {
            Assert.DoesNotContain(output, line => line.StartsWith("##vso[task.complete", StringComparison.Ordinal));
            Assert.DoesNotContain(output, line => line.StartsWith("##vso[task.logissue type=error;", StringComparison.Ordinal));
        }

        public void Dispose()
        {
            client.Object.Dispose();
            connection.Dispose();
            handler.Dispose();
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
