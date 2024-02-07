// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Agent.Plugins.PipelineArtifact.Telemetry;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Agent.Sdk.Knob;

namespace Agent.Plugins
{
    // A wrapper of DedupManifestArtifactClient, providing basic functionalities such as uploading and downloading pipeline artifacts.
    public class PipelineArtifactServer
    {
        private readonly IAppTraceSource tracer;

        public PipelineArtifactServer(IAppTraceSource tracer)
        {
            this.tracer = tracer;
        }

        // Upload from target path to Azure DevOps BlobStore service through DedupManifestArtifactClient, then associate it with the build
        internal async Task UploadAsync(
            AgentTaskPluginExecutionContext context,
            Guid projectId,
            int pipelineId,
            string name,
            string source,
            IDictionary<string, string> properties,
            CancellationToken cancellationToken)
        {
            // Get the client settings, if any.
            var tracer = DedupManifestArtifactClientFactory.CreateArtifactsTracer(verbose: false, (str) => context.Output(str));
            VssConnection connection = context.VssConnection;
            var clientSettings = await BlobstoreClientSettings.GetClientSettingsAsync(
                connection,
                Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.PipelineArtifact,
                tracer,
                cancellationToken);
            
            // Check if the pipeline has an override domain set, if not, use the default domain from the client settings.
            string overrideDomain = AgentKnobs.SendPipelineArtifactsToBlobstoreDomain.GetValue(context).AsString();
            IDomainId domainId = String.IsNullOrWhiteSpace(overrideDomain) ? clientSettings.GetDefaultDomainId() : DomainIdFactory.Create(overrideDomain);

            var (dedupManifestClient, clientTelemetry) = DedupManifestArtifactClientFactory.Instance
                .CreateDedupManifestClient(
                    context.IsSystemDebugTrue(),
                    (str) => context.Output(str),
                    connection,
                    DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                    domainId,
                    clientSettings,
                    context,
                    cancellationToken);

            using (clientTelemetry)
            {
                //Upload the pipeline artifact.
                PipelineArtifactActionRecord uploadRecord = clientTelemetry.CreateRecord<PipelineArtifactActionRecord>((level, uri, type) =>
                    new PipelineArtifactActionRecord(level, uri, type, nameof(UploadAsync), context));

                PublishResult result = await clientTelemetry.MeasureActionAsync(
                    record: uploadRecord,
                    actionAsync: async () => await AsyncHttpRetryHelper.InvokeAsync(
                            async () =>
                            {
                                return await dedupManifestClient.PublishAsync(source, cancellationToken);
                            },
                            maxRetries: 3,
                            tracer: tracer,
                            canRetryDelegate: e => true, // this isn't great, but failing on upload stinks, so just try a couple of times
                            cancellationToken: cancellationToken,
                            continueOnCapturedContext: false)

                );
                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: uploadRecord);

                // 2) associate the pipeline artifact with an build artifact
                BuildServer buildServer = new BuildServer(connection);

                var propertiesDictionary = new Dictionary<string, string>(properties ?? new Dictionary<string, string>())
                {
                    { PipelineArtifactConstants.RootId, result.RootId.ValueString },
                    { PipelineArtifactConstants.ProofNodes, StringUtil.ConvertToJson(result.ProofNodes.ToArray()) },
                    { PipelineArtifactConstants.ArtifactSize, result.ContentSize.ToString() },
                    { PipelineArtifactConstants.HashType, dedupManifestClient.HashType.Serialize() },
                    { PipelineArtifactConstants.DomainId, domainId.Serialize() }
                };

                BuildArtifact buildArtifact = await AsyncHttpRetryHelper.InvokeAsync(
                    async () =>
                    {
                        return await buildServer.AssociateArtifactAsync(projectId,
                                                                        pipelineId,
                                                                        name,
                                                                        context.Variables.GetValueOrDefault(WellKnownDistributedTaskVariables.JobId)?.Value ?? string.Empty,
                                                                        ArtifactResourceTypes.PipelineArtifact,
                                                                        result.ManifestId.ValueString,
                                                                        propertiesDictionary,
                                                                        cancellationToken);

                    },
                    maxRetries: 3,
                    tracer: tracer,
                    canRetryDelegate: e => e is TimeoutException || e.InnerException is TimeoutException,
                    cancellationToken: cancellationToken,
                    continueOnCapturedContext: false);

                context.Output(StringUtil.Loc("AssociateArtifactWithBuild", buildArtifact.Id, pipelineId));
            }
        }

        // Download pipeline artifact from Azure DevOps BlobStore service through DedupManifestArtifactClient to a target path
        // Old V0 function
        internal Task DownloadAsync(
            AgentTaskPluginExecutionContext context,
            Guid projectId,
            int pipelineId,
            string artifactName,
            string targetDir,
            CancellationToken cancellationToken)
        {
            var downloadParameters = new ArtifactDownloadParameters
            {
                ProjectRetrievalOptions = BuildArtifactRetrievalOptions.RetrieveByProjectId,
                ProjectId = projectId,
                PipelineId = pipelineId,
                ArtifactName = artifactName,
                TargetDirectory = targetDir
            };

            return this.DownloadAsync(context, downloadParameters, DownloadOptions.SingleDownload, cancellationToken);
        }

        // Download with minimatch patterns, V1.
        internal async Task DownloadAsync(
            AgentTaskPluginExecutionContext context,
            ArtifactDownloadParameters downloadParameters,
            DownloadOptions downloadOptions,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            PipelineArtifactProvider provider = new PipelineArtifactProvider(context, connection, tracer);

            BuildServer buildServer = new(connection);

            // download all pipeline artifacts if artifact name is missing
                if (downloadOptions == DownloadOptions.MultiDownload)
                {
                    List<BuildArtifact> artifacts;
                    if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectId)
                    {
                        artifacts = await buildServer.GetArtifactsAsync(downloadParameters.ProjectId, downloadParameters.PipelineId, cancellationToken);
                    }
                    else if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectName)
                    {
                        if (string.IsNullOrEmpty(downloadParameters.ProjectName))
                        {
                            throw new InvalidOperationException("Project name can't be empty when trying to fetch build artifacts!");
                        }
                        else
                        {
                            artifacts = await buildServer.GetArtifactsWithProjectNameAsync(downloadParameters.ProjectName, downloadParameters.PipelineId, cancellationToken);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Invalid {nameof(downloadParameters.ProjectRetrievalOptions)}!");
                    }

                    IEnumerable<BuildArtifact> pipelineArtifacts = artifacts.Where(a => string.Equals(a.Resource.Type, PipelineArtifactConstants.PipelineArtifact, StringComparison.OrdinalIgnoreCase));
                    if (!pipelineArtifacts.Any())
                    {
                        throw new ArgumentException("Could not find any pipeline artifacts in the build.");
                    }
                    else
                    {
                        context.Output(StringUtil.Loc("DownloadingMultiplePipelineArtifacts", pipelineArtifacts.Count()));
                        await provider.DownloadMultipleArtifactsAsync(downloadParameters, pipelineArtifacts, cancellationToken, context);
                    }
                }
                else if (downloadOptions == DownloadOptions.SingleDownload)
                {
                    // 1) get manifest id from artifact data
                    BuildArtifact buildArtifact;
                    if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectId)
                    {
                        buildArtifact = await buildServer.GetArtifact(downloadParameters.ProjectId, downloadParameters.PipelineId, downloadParameters.ArtifactName, cancellationToken);
                    }
                    else if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectName)
                    {
                        if (string.IsNullOrEmpty(downloadParameters.ProjectName))
                        {
                            throw new InvalidOperationException("Project name can't be empty when trying to fetch build artifacts!");
                        }
                        else
                        {
                            buildArtifact = await buildServer.GetArtifactWithProjectNameAsync(downloadParameters.ProjectName, downloadParameters.PipelineId, downloadParameters.ArtifactName, cancellationToken);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Invalid {nameof(downloadParameters.ProjectRetrievalOptions)}!");
                    }
                    await provider.DownloadSingleArtifactAsync(downloadParameters, buildArtifact, cancellationToken, context);
                }
                else
                {
                    throw new InvalidOperationException($"Invalid {nameof(downloadOptions)}!");
                }
        }

        // Download for version 2. This decision was made because version 1 is sealed and we didn't want to break any existing customers.
        internal async Task DownloadAsyncV2(
            AgentTaskPluginExecutionContext context,
            ArtifactDownloadParameters downloadParameters,
            DownloadOptions downloadOptions,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            BuildServer buildServer = new BuildServer(connection);

            // download all pipeline artifacts if artifact name is missing
            if (downloadOptions == DownloadOptions.MultiDownload)
            {
                List<BuildArtifact> artifacts;
                if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectId)
                {
                    artifacts = await buildServer.GetArtifactsAsync(downloadParameters.ProjectId, downloadParameters.PipelineId, cancellationToken);
                }
                else if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectName)
                {
                    if (string.IsNullOrEmpty(downloadParameters.ProjectName))
                    {
                        throw new InvalidOperationException("Project name can't be empty when trying to fetch build artifacts!");
                    }
                    else
                    {
                        artifacts = await buildServer.GetArtifactsWithProjectNameAsync(downloadParameters.ProjectName, downloadParameters.PipelineId, cancellationToken);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Invalid {nameof(downloadParameters.ProjectRetrievalOptions)}!");
                }

                IEnumerable<BuildArtifact> buildArtifacts = artifacts.Where(a => string.Equals(a.Resource.Type, PipelineArtifactConstants.Container, StringComparison.OrdinalIgnoreCase));
                IEnumerable<BuildArtifact> pipelineArtifacts = artifacts.Where(a => string.Equals(a.Resource.Type, PipelineArtifactConstants.PipelineArtifact, StringComparison.OrdinalIgnoreCase));
                IEnumerable<BuildArtifact> fileShareArtifacts = artifacts.Where(a => string.Equals(a.Resource.Type, PipelineArtifactConstants.FileShareArtifact, StringComparison.OrdinalIgnoreCase));

                if (buildArtifacts.Any())
                {
                    FileContainerProvider provider = new FileContainerProvider(connection, this.tracer);
                    await provider.DownloadMultipleArtifactsAsync(downloadParameters, buildArtifacts, cancellationToken, context);
                }

                if (pipelineArtifacts.Any())
                {
                    PipelineArtifactProvider provider = new PipelineArtifactProvider(context, connection, this.tracer);
                    await provider.DownloadMultipleArtifactsAsync(downloadParameters, pipelineArtifacts, cancellationToken, context);
                }

                if (fileShareArtifacts.Any())
                {
                    FileShareProvider provider = new FileShareProvider(context, connection, this.tracer, DedupManifestArtifactClientFactory.Instance);
                    await provider.DownloadMultipleArtifactsAsync(downloadParameters, fileShareArtifacts, cancellationToken, context);
                }
            }
            else if (downloadOptions == DownloadOptions.SingleDownload)
            {
                // 1) get manifest id from artifact data
                BuildArtifact buildArtifact;
                if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectId)
                {
                    buildArtifact = await buildServer.GetArtifact(downloadParameters.ProjectId, downloadParameters.PipelineId, downloadParameters.ArtifactName, cancellationToken);
                }
                else if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectName)
                {
                    if (string.IsNullOrEmpty(downloadParameters.ProjectName))
                    {
                        throw new InvalidOperationException("Project name can't be empty when trying to fetch build artifacts!");
                    }
                    else
                    {
                        buildArtifact = await buildServer.GetArtifactWithProjectNameAsync(downloadParameters.ProjectName, downloadParameters.PipelineId, downloadParameters.ArtifactName, cancellationToken);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Invalid {nameof(downloadParameters.ProjectRetrievalOptions)}!");
                }

                ArtifactProviderFactory factory = new ArtifactProviderFactory(context, connection, this.tracer);
                IArtifactProvider provider = factory.GetProvider(buildArtifact);

                await provider.DownloadSingleArtifactAsync(downloadParameters, buildArtifact, cancellationToken, context);
            }
            else
            {
                throw new InvalidOperationException($"Invalid {nameof(downloadOptions)}!");
            }
        }
    }

    internal enum DownloadOptions
    {
        SingleDownload,
        MultiDownload
    }
}