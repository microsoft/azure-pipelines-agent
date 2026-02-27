using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{

    public class BlobstoreClientSettingsL0
    {
        private const string OverrideChunkSize = "OVERRIDE_PIPELINE_ARTIFACT_CHUNKSIZE";
        private const string EnablePipelineArtifactLargeChunkSize = "AGENT_ENABLE_PIPELINEARTIFACT_LARGE_CHUNK_SIZE";
        [Fact]
        public void GetDefaultDomainId_ReturnsDefault_WhenNoSettings()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var settings = new BlobstoreClientSettings(null, tracer.Object);

            // Act
            var result = settings.GetDefaultDomainId();

            // Assert
            Assert.Equal(WellKnownDomainIds.DefaultDomainId, result);
        }

        [Fact]
        public void GetDefaultDomainId_ReturnsDomainId_WhenSettingsPresent()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var domainId = Guid.NewGuid().ToString();
            var clientSettings = new ClientSettingsInfo
            {
                Properties = new Dictionary<string, string>
            {
                { ClientSettingsConstants.DefaultDomainId, domainId }
            }
            };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            // Act
            var result = settings.GetDefaultDomainId();

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void GetClientHashType_EnablePipelineArtifactLargeChunkSize_EnablesOrDisablesChunkSizing()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var clientSettings = new ClientSettingsInfo { Properties = new Dictionary<string, string>() 
            {
                { ClientSettingsConstants.ChunkSize, HashType.Dedup1024K.ToString() }
            }
            };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            var environment = new LocalEnvironment();
            var context = new Mock<AgentTaskPluginExecutionContext>();
            
            context.As<IKnobValueContext>()
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            context.As<IKnobValueContext>()
                .Setup(x => x.GetVariableValueOrDefault(EnablePipelineArtifactLargeChunkSize ))
                .Returns("false");
            environment.SetEnvironmentVariable(EnablePipelineArtifactLargeChunkSize, "false");

            // Act
            var result = settings.GetClientHashType(context.Object);

            // make sure if we enable it, it uses the client settings
            Assert.Equal(ChunkerHelper.DefaultChunkHashType, result);
            context.As<IKnobValueContext>()
                .Setup(x => x.GetVariableValueOrDefault(EnablePipelineArtifactLargeChunkSize))
                .Returns("true");
            environment.SetEnvironmentVariable(EnablePipelineArtifactLargeChunkSize, "true");

            // Act
            result = settings.GetClientHashType(context.Object);

            // Assert
            Assert.Equal(HashType.Dedup1024K, result);
        }

        [Fact]
        public void GetClientHashType_PipelineOverride()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var clientSettings = new ClientSettingsInfo
            {
                Properties = new Dictionary<string, string>()
            {
                { ClientSettingsConstants.ChunkSize, HashType.Dedup64K.ToString() }
            }
            };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            var environment = new LocalEnvironment();
            var context = new Mock<AgentTaskPluginExecutionContext>();

            context.As<IKnobValueContext>()
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            context.As<IKnobValueContext>()
                .Setup(x => x.GetVariableValueOrDefault(EnablePipelineArtifactLargeChunkSize))
                .Returns("true");
            environment.SetEnvironmentVariable(EnablePipelineArtifactLargeChunkSize, "true");

            context.As<IKnobValueContext>()
                .Setup(x => x.GetVariableValueOrDefault(OverrideChunkSize))
                .Returns(HashType.Dedup1024K.ToString());
            environment.SetEnvironmentVariable(OverrideChunkSize, HashType.Dedup1024K.ToString());

            // Act
            var result = settings.GetClientHashType(context.Object);

            // we should successfully override the chunk size in the client settings:
            Assert.Equal(HashType.Dedup1024K, result);

            // now let's setup a bad override and make sure it falls back to the client settings:
            clientSettings.Properties[ClientSettingsConstants.ChunkSize] = HashType.Dedup1024K.ToString();
            context.As<IKnobValueContext>()
                .Setup(x => x.GetVariableValueOrDefault(OverrideChunkSize))
                .Returns("nonsense");
            environment.SetEnvironmentVariable(OverrideChunkSize, "nonsense");

            // Act
            result = settings.GetClientHashType(context.Object);

            // Assert
            Assert.Equal(HashType.Dedup1024K, result);
        }

        [Fact]
        public void GetRedirectTimeout_ReturnsNull_WhenNotPresent()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var clientSettings = new ClientSettingsInfo { Properties = new Dictionary<string, string>() };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            // Act
            var result = settings.GetRedirectTimeout();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetRedirectTimeout_ReturnsValue_WhenPresent()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var clientSettings = new ClientSettingsInfo
            {
                Properties = new Dictionary<string, string>
            {
                { ClientSettingsConstants.RedirectTimeout, "42" }
            }
            };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            // Act
            var result = settings.GetRedirectTimeout();

            // Assert
            Assert.Equal(42, result);
        }

        [Fact]
        public void GetMaxParallelism_ReturnsNull_WhenNotPresent()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var clientSettings = new ClientSettingsInfo { Properties = new Dictionary<string, string>() };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            // Act
            var result = settings.GetMaxParallelism();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetMaxParallelism_ReturnsValue_WhenPresent()
        {
            // Arrange
            var tracer = new Mock<IAppTraceSource>();
            var clientSettings = new ClientSettingsInfo
            {
                Properties = new Dictionary<string, string>
            {
                { "MaxParallelism", "8" }
            }
            };
            var settings = new BlobstoreClientSettings(clientSettings, tracer.Object);

            // Act
            var result = settings.GetMaxParallelism();

            // Assert
            Assert.Equal(8, result);
        }
    }
}