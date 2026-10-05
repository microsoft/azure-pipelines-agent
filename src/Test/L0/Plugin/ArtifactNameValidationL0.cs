// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins;
using Agent.Plugins.PipelineArtifact;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.WebApi;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Plugin
{
    public sealed class ArtifactNameValidationL0 : IDisposable
    {
        private const string KnobVariable = "AZP_AGENT_ENABLE_ARTIFACT_NAME_VALIDATION";
        private const string StrictError = "ArtifactNameIsNotValidWithStrictValidation";
        private readonly string target = Path.Combine(TestUtil.GetSrcPath(), "Test", "TestResults", Guid.NewGuid().ToString("N"));
        private readonly ConcurrentQueue<string> output = new ConcurrentQueue<string>();
        private readonly Mock<BuildHttpClient> client;
        private readonly VssHttpMessageHandler handler = new VssHttpMessageHandler(new VssCredentials(), VssClientHttpRequestSettings.Default);
        private readonly VssConnection connection;
        private List<BuildArtifact> artifacts = new List<BuildArtifact> { Artifact("../outside", "Unsupported") };

        public ArtifactNameValidationL0() : this(new Uri("https://example.invalid")) { }

        internal ArtifactNameValidationL0(Uri buildAddress)
        {
            client = new Mock<BuildHttpClient>(MockBehavior.Strict, buildAddress, new VssCredentials());
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
        public void SharedValidatorOnlyAddsStrictRulesWhenEnabled()
        {
            Assert.Throws<ArgumentNullException>(() => PipelineArtifactPathHelper.IsValidArtifactName(null));
            Assert.False(PipelineArtifactPathHelper.IsValidArtifactName(null, true));
            foreach (string name in new[] { "", ".", "..", "...", " ", " . ", ". . " })
            {
                Assert.True(PipelineArtifactPathHelper.IsValidArtifactName(name), $"Legacy name rejected: '{name}'");
                Assert.False(PipelineArtifactPathHelper.IsValidArtifactName(name, true), $"Strict alias accepted: '{name}'");
            }
            foreach (string name in new[] { "drop", "release-1.2", ".build", "\u53d1\u5e03", "../outside", @"..\outside", @"C:\root", @"\\server\share", "a\0b", "a:b" })
            {
                Assert.Equal(PipelineArtifactPathHelper.IsValidArtifactName(name), PipelineArtifactPathHelper.IsValidArtifactName(name, true));
            }
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
            Assert.Equal(enabled ? 4 : 0, Errors().Count);
            Assert.Equal(enabled ? 4 : 0, FailureCommands());
            Assert.Equal("", context.Variables["DownloadPipelineArtifactResourceTypes"].Value);
            Assert.Empty(Warnings());
            client.Verify(x => x.GetArtifactsAsync(It.IsAny<Guid>(), 1, null, It.IsAny<CancellationToken>()),
                source == "current" ? Times.Once() : Times.Never());
            client.Verify(x => x.GetArtifactsAsync(It.IsAny<string>(), 1, null, It.IsAny<CancellationToken>()),
                source == "specific" ? Times.Once() : Times.Never());
        }

        [Theory]
        [InlineData(false, "false", "../outside")]
        [InlineData(false, "true", "../outside")]
        [InlineData(false, "true", "..")]
        [InlineData(true, "false", "../outside")]
        [InlineData(true, "true", "../outside")]
        [InlineData(true, "true", " . . ")]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task SharedValidatorCallersUseFlaggedRulesAndMessages(bool publish, string flag, string name)
        {
            var context = publish ? PublishContext(name, flag) : Context(flag);
            context.Inputs["artifact"] = name;
            IAgentTaskPlugin task = publish ? new PublishPipelineArtifactTaskV1() : new DownloadPipelineArtifactTaskV2_0_0();
            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                task.RunAsync(context, CancellationToken.None));
            Assert.Equal(StringUtil.Loc(flag == "true" ? StrictError : "ArtifactNameIsNotValid", name), error.Message);
            Assert.Empty(Warnings());
            Assert.False(Directory.Exists(target));
            client.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(null, "true")]
        [InlineData("", "true")]
        [InlineData(" ", "true")]
        [InlineData(" . . ", "false")]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PublishPreservesAutomaticNamingAndLegacyAliases(string name, string flag)
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                new PublishPipelineArtifactTaskV1().RunAsync(PublishContext(name, flag), CancellationToken.None));
            Assert.Empty(Errors());
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
        public async Task MissingSelectorStillDownloadsAllWithStrictValidation()
        {
            var context = Context("true");
            context.Inputs.Remove("artifact");
            await new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None);
            Assert.Single(Errors());
            Assert.Equal(1, FailureCommands());
            Assert.True(Directory.Exists(target));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task ValidArtifactIoFailureStillPropagatesAfterInvalidNameErrors()
        {
            await using var server = new ContainerServer();
            using var fixture = new ArtifactNameValidationL0(server.Address);
            var context = fixture.Context("true");
            fixture.artifacts = new List<BuildArtifact> { Artifact("../outside", "Container"), Artifact("release-1.2", "Container") };
            Directory.CreateDirectory(fixture.target);
            File.WriteAllText(Path.Combine(fixture.target, "release-1.2"), "inert blocking file");

            await Assert.ThrowsAsync<IOException>(() => new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None));
            Assert.Single(fixture.Errors());
            Assert.Equal(1, fixture.FailureCommands());
            Assert.Equal(0, server.Downloads);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task NamedDownloadDoesNotFilterReturnedMetadataOrChangeLegacySelector()
        {
            var context = Context("false");
            context.Inputs["artifact"] = " . ";
            client.Setup(x => x.GetArtifactAsync(It.IsAny<Guid>(), 1, " . ", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Artifact("../outside", "Unsupported"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None));
            client.Verify(x => x.GetArtifactAsync(It.IsAny<Guid>(), 1, " . ", null, It.IsAny<CancellationToken>()), Times.Once());
            Assert.True(Directory.Exists(target));
            Assert.Empty(Warnings());
            Assert.Empty(Errors());
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
            Assert.Empty(Errors());
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
            Assert.Equal(enabled ? 1 : 0, Errors().Count);
            Assert.Equal(enabled ? 1 : 0, FailureCommands());

            context.Variables[KnobVariable] = "false";
            output.Clear();
            await plugin.RunAsync(context, CancellationToken.None);
            Assert.Empty(Warnings());
            Assert.Empty(Errors());
            Assert.Equal(0, FailureCommands());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task ErrorEscapesNameWithoutChangingGlobalLocalization()
        {
            string localizationBefore = StringUtil.Loc("MissingNodePath");
            const string name = "invalid\r\n##vso[task.complete result=Failed;]%0A";
            var context = Context("true");
            context.Variables["DECODE_PERCENTS"] = "true";
            artifacts = new List<BuildArtifact> { Artifact(name, "Container") };
            await new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None);
            Assert.Single(Errors());
            Assert.Equal(1, FailureCommands());
            output.Clear();

            var resources = IOUtil.LoadObject<Dictionary<string, object>>(Path.Combine(TestUtil.GetSrcPath(), "Misc", "layoutbin", "en-US", "strings.json"));
            string legacy = (string)resources["ArtifactNameIsNotValid"];
            string detailed = (string)resources[StrictError];
            Assert.StartsWith(legacy + ".", detailed);
            Assert.Contains("It cannot be empty or consist entirely of dots or spaces.", detailed);
            string message = StringUtil.Format(detailed, name);
            context.Error(message);
            string commandText = Assert.Single(Errors());
            Assert.DoesNotContain("\r", commandText);
            Assert.DoesNotContain("\n", commandText);
            Assert.Contains("%0D%0A", commandText);
            Assert.Contains("%AZP250A", commandText);
            Assert.True(Command.TryParse(commandText, true, out Command command));
            Assert.Equal("logissue", command.Event);
            Assert.Equal("error", command.Properties["type"]);
            Assert.Equal(message, command.Data);
            Assert.Equal(1, FailureCommands());
            Assert.Equal(localizationBefore, StringUtil.Loc("MissingNodePath"));
        }

        internal async Task VerifyDownloadContinuationAsync(bool mixed, Action<string> processOutput)
        {
            await using var server = new ContainerServer();
            using var fixture = mixed ? new ArtifactNameValidationL0(server.Address) : new ArtifactNameValidationL0();
            var context = fixture.Context("true", processOutput: processOutput);
            context.Variables["DISABLE_BUILD_ARTIFACTS_TO_BLOB"] = "true";
            fixture.artifacts = new List<BuildArtifact>
            {
                Artifact("../outside", "Container"), Artifact("", "PipelineArtifact"),
                Artifact("..", "FilePath"), new BuildArtifact { Name = null }
            };
            if (mixed)
            {
                fixture.artifacts.Add(Artifact("release-1.2", "Container"));
                fixture.artifacts.Add(Artifact(".build", "Container"));
                server.BeforeDownload = () =>
                {
                    Assert.Equal(4, fixture.Errors().Count);
                    Assert.Equal(4, fixture.FailureCommands());
                };
            }
            await new DownloadPipelineArtifactTaskV2_0_0().RunAsync(context, CancellationToken.None);
            Assert.Equal(4, fixture.Errors().Count);
            Assert.Equal(4, fixture.FailureCommands());
            if (mixed)
            {
                Assert.Equal(2, server.Downloads);
                Assert.Equal("inert data", File.ReadAllText(Path.Combine(fixture.target, "release-1.2", "marker.txt")));
                Assert.Equal("inert data", File.ReadAllText(Path.Combine(fixture.target, ".build", "marker.txt")));
                Assert.Equal(2, Directory.GetDirectories(fixture.target).Length);
            }
            else
            {
                Assert.Empty(Directory.GetFileSystemEntries(fixture.target));
            }
        }

        private AgentTaskPluginExecutionContext Context(string flag = null, string source = "current", Action<string> processOutput = null)
        {
            var trace = new Mock<ITraceWriter>();
            trace.Setup(x => x.Info(It.IsAny<string>(), It.IsAny<string>())).Callback((string message, string operation) =>
            {
                output.Enqueue(message);
                processOutput?.Invoke(message);
            });
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

        private AgentTaskPluginExecutionContext PublishContext(string name, string flag)
        {
            var context = Context(flag);
            context.Inputs["artifactName"] = name;
            context.Variables["system.hostType"] = "Build";
            context.Variables["system.jobIdentifier"] = "Build.Job";
            return context;
        }

        private static BuildArtifact Artifact(string name, string type) =>
            new BuildArtifact { Name = name, Resource = new ArtifactResource { Type = type, Data = "#/1/" + name } };

        private List<string> Warnings() => output.Where(line => line.StartsWith("##vso[task.logissue type=warning;]", StringComparison.Ordinal)).ToList();

        private List<string> Errors() => output.Where(line => line.StartsWith("##vso[task.logissue type=error;]", StringComparison.Ordinal)).ToList();

        private int FailureCommands() => output.Count(line => line == "##vso[task.complete result=Failed;]");

        private sealed class ContainerServer : IAsyncDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            private readonly Task serve;
            public Uri Address { get; }
            public int Downloads { get; private set; }
            public Action BeforeDownload { get; set; }

            public ContainerServer()
            {
                listener.Start();
                Address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
                serve = Task.Run(ServeAsync);
            }

            private async Task ServeAsync()
            {
                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        using var socket = await listener.AcceptTcpClientAsync(cancellation.Token);
                        using var stream = socket.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                        string request = await reader.ReadLineAsync(cancellation.Token);
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellation.Token))) { }
                        string body;
                        string contentType = "application/json";
                        if (request.Contains("/_apis/connectionData", StringComparison.Ordinal))
                        {
                            body = "{\"instanceId\":\"11111111-1111-1111-1111-111111111111\",\"locationServiceData\":{\"serviceOwner\":\"11111111-1111-1111-1111-111111111111\",\"defaultAccessMappingMoniker\":\"PublicAccessMapping\",\"accessMappings\":[{\"moniker\":\"PublicAccessMapping\",\"accessPoint\":\"" + Address + "\"}],\"serviceDefinitions\":[]}}";
                        }
                        else if (request.StartsWith("OPTIONS ", StringComparison.Ordinal))
                        {
                            body = "{\"count\":1,\"value\":[{\"id\":\"e4f5c81e-e250-447b-9fef-bd48471bea5e\",\"area\":\"resources\",\"resourceName\":\"Containers\",\"routeTemplate\":\"_apis/resources/Containers/{containerId}\",\"resourceVersion\":4,\"minVersion\":\"1.0\",\"maxVersion\":\"7.2\",\"releasedVersion\":\"7.0\"}]}";
                        }
                        else
                        {
                            var uri = new Uri(Address, request.Split(' ')[1]);
                            string itemPath = Uri.UnescapeDataString(uri.Query.Split('&').Single(part => part.TrimStart('?').StartsWith("itemPath=", StringComparison.Ordinal)).Split('=')[1]);
                            if (itemPath.EndsWith("/marker.txt", StringComparison.Ordinal))
                            {
                                BeforeDownload();
                                Downloads++;
                                contentType = "application/octet-stream";
                                body = "inert data";
                            }
                            else
                            {
                                body = StringUtil.ConvertToJson(new
                                {
                                    count = 1,
                                    value = new[] { new FileContainerItem { ContainerId = 1, Path = itemPath + "/marker.txt", ItemType = ContainerItemType.File, Status = ContainerItemStatus.Created, FileLength = 10 } }
                                });
                            }
                        }
                        byte[] payload = Encoding.UTF8.GetBytes(body);
                        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(headers, cancellation.Token);
                        await stream.WriteAsync(payload, cancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                finally { listener.Stop(); }
            }

            public async ValueTask DisposeAsync()
            {
                cancellation.Cancel();
                listener.Dispose();
                await serve;
                cancellation.Dispose();
            }
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
