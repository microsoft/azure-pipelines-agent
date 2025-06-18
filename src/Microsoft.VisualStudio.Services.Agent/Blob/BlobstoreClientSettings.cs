// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    public class BlobstoreClientSettings
    {
        private readonly ClientSettingsInfo clientSettings;
        private readonly IAppTraceSource tracer;

        internal BlobstoreClientSettings(ClientSettingsInfo settings, IAppTraceSource tracer)
        {
            clientSettings = settings;
            this.tracer = tracer;
        }

        /// <summary>
        /// Get the client settings for the given client.
        /// </summary>
        /// <notes> This should  only be called once per client type.  This is intended to fail fast so it has no retries.</notes>
        public static async Task<BlobstoreClientSettings> GetClientSettingsAsync(
            VssConnection connection,
            BlobStore.WebApi.Contracts.Client? client,
            IAppTraceSource tracer,
            CancellationToken cancellationToken)
        {
            if (client.HasValue)
            {
                try
                {
                    ArtifactHttpClientFactory factory = new(
                        connection.Credentials,
                        connection.Settings.SendTimeout,
                        tracer,
                        cancellationToken);

                    var blobUri = connection.GetClient<ClientSettingsHttpClient>().BaseAddress;
                    var clientSettingsHttpClient = factory.CreateVssHttpClient<IClientSettingsHttpClient, ClientSettingsHttpClient>(blobUri);
                    return new BlobstoreClientSettings(await clientSettingsHttpClient.GetSettingsAsync(client.Value, userState: null, cancellationToken), tracer);
                }
                catch (Exception exception)
                {
                    // Use info cause we don't want to fail builds with warnings as errors...
                    tracer.Info($"Error while retrieving client Settings for {client}. Exception: {exception}.  Falling back to defaults.");
                }
            }
            return new BlobstoreClientSettings(null, tracer);
        }

        public IDomainId GetDefaultDomainId()
        {
            IDomainId domainId = WellKnownDomainIds.DefaultDomainId;
            if (clientSettings != null && clientSettings.Properties.ContainsKey(ClientSettingsConstants.DefaultDomainId))
            {
                try
                {
                    domainId = DomainIdFactory.Create(clientSettings.Properties[ClientSettingsConstants.DefaultDomainId]);
                    tracer.Verbose($"Using domain id '{domainId}' from client settings.");
                }
                catch (Exception exception)
                {
                    tracer.Info($"Error converting the domain id '{clientSettings.Properties[ClientSettingsConstants.DefaultDomainId]}': {exception.Message}.  Falling back to default.");
                }
            }
            else
            {
                tracer.Verbose($"No client settings found, using the default domain id '{domainId}'.");
            }
            return domainId;
        }

        public HashType GetClientHashType(AgentTaskPluginExecutionContext context)
        {
            HashType hashType = ChunkerHelper.DefaultChunkHashType;

            // Note: 9/6/2023 Remove the below check in couple of months.
            if (AgentKnobs.AgentEnablePipelineArtifactLargeChunkSize.GetValue(context).AsBoolean())
            {
                // grab the client settings from the server first if available:
                if (clientSettings?.Properties.ContainsKey(ClientSettingsConstants.ChunkSize) == true)
                {
                    try
                    {
                        HashTypeExtensions.Deserialize(clientSettings.Properties[ClientSettingsConstants.ChunkSize], out hashType);
                    }
                    catch (Exception exception)
                    {
                        tracer.Info($"Error converting the chunk size '{clientSettings.Properties[ClientSettingsConstants.ChunkSize]}': {exception.Message}.  Falling back to default.");
                    }
                }

                // now check if this pipeline has an override chunk size set, and use that if available:
                string overrideChunkSize = AgentKnobs.OverridePipelineArtifactChunkSize.GetValue(context).AsString();
                if (!String.IsNullOrEmpty(overrideChunkSize))
                {
                    try
                    {
                        HashTypeExtensions.Deserialize(overrideChunkSize, out HashType overrideHashType);
                        if (ChunkerHelper.IsHashTypeChunk(overrideHashType))
                        {
                            hashType = overrideHashType;
                            tracer.Info($"Overriding chunk size to '{overrideChunkSize}'.");
                        }
                        else
                        {
                            tracer.Info($"Override chunk size '{overrideChunkSize}' is not a valid chunk type. Falling back to client settings.");
                        }
                    }
                    catch (Exception exception)
                    {
                        tracer.Info($"Error overriding the chunk size to '{overrideChunkSize}': {exception.Message}.  Falling back to client settings.");
                    }
                }
            }

            return ChunkerHelper.IsHashTypeChunk(hashType) ? hashType : ChunkerHelper.DefaultChunkHashType;
        }

        public int? GetRedirectTimeout()
        {
            if (int.TryParse(clientSettings?.Properties.GetValueOrDefault(ClientSettingsConstants.RedirectTimeout), out int redirectTimeoutSeconds))
            {
                return redirectTimeoutSeconds;
            }
            else
            {
                return null;
            }
        }

        public int? GetMaxParallelism()
        {
            const string MaxParallelism = "MaxParallelism";
            if (int.TryParse(clientSettings?.Properties.GetValueOrDefault(MaxParallelism), out int maxParallelism))
            {
                return maxParallelism;
            }
            else
            {
                return null;
            }
        }
    }
}