// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts;
using Agent.Sdk.Knob;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    [ServiceLocator(Default = typeof(DedupManifestArtifactClientFactory))]
    public interface IDedupManifestArtifactClientFactory
    {
        /// <summary>
        /// Creates a DedupManifestArtifactClient client.
        /// </summary>
        /// <param name="verbose">If true emit verbose telemetry.</param>
        /// <param name="traceOutput">Action used for logging.</param>
        /// <param name="connection">VssConnection</param>
        /// <param name="maxParallelism">Maximum number of parallel threads that should be used for download. If 0 then
        /// use the system default. </param>
        /// <param name="cancellationToken">Cancellation token used for both creating clients and verifying client conneciton.</param>
        /// <returns>Tuple of the client and the telemtery client</returns>
        (DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry) CreateDedupManifestClient(
            bool verbose,
            Action<string> traceOutput,
            VssConnection connection,
            int maxParallelism,
            IDomainId domainId,
            BlobstoreClientSettings clientSettings,
            AgentTaskPluginExecutionContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Creates a DedupManifestArtifactClient client and retrieves any client settings from the server
        /// </summary>
        Task<(DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry)> CreateDedupManifestClientAsync(
            bool verbose,
            Action<string> traceOutput,
            VssConnection connection,
            int maxParallelism,
            IDomainId domainId,
            BlobStore.WebApi.Contracts.Client client,
            AgentTaskPluginExecutionContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Creates a DedupStoreClient client.
        /// </summary>
        /// <param name="connection">VssConnection</param>
        /// <param name="domainId">Storage domain to use, if null pulls the default domain for the given client type.</param>
        /// <param name="maxParallelism">Maximum number of parallel threads that should be used for download. If 0 then
        /// use the system default. </param>
        /// <param name="redirectTimeout">Number of seconds to wait for an http redirect.</param>
        /// <param name="verbose">If true emit verbose telemetry.</param>
        /// <param name="traceOutput">Action used for logging.</param>
        /// <param name="cancellationToken">Cancellation token used for both creating clients and verifying client conneciton.</param>
        /// <returns>Tuple of the domain, client and the telemetry client</returns>
        (DedupStoreClient client, BlobStoreClientTelemetryTfs telemetry) CreateDedupClient(
            VssConnection connection,
            IDomainId domainId,
            int maxParallelism,
            int? redirectTimeoutSeconds,
            bool verbose,
            Action<string> traceOutput,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the maximum parallelism to use for dedup related downloads and uploads.
        /// </summary>
        /// <param name="context">Context which may specify overrides for max parallelism</param>
        /// <returns>max parallelism</returns>
        int GetDedupStoreClientMaxParallelism(AgentTaskPluginExecutionContext context);
    }

    public class DedupManifestArtifactClientFactory : IDedupManifestArtifactClientFactory
    {
        // Old default for hosted agents was 16*2 cores = 32.
        // In my tests of a node_modules folder, this 32x parallelism was consistently around 47 seconds.
        // At 192x it was around 16 seconds and 256x was no faster.
        private const int DefaultDedupStoreClientMaxParallelism = 192;

        private HashType? HashType { get; set; }

        public static readonly DedupManifestArtifactClientFactory Instance = new();

        private DedupManifestArtifactClientFactory()
        {
        }

        /// <summary>
        /// Creates a DedupManifestArtifactClient client and retrieves any client settings from the server
        /// </summary>
        public async Task<(DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry)> CreateDedupManifestClientAsync(
            bool verbose,
            Action<string> traceOutput,
            VssConnection connection,
            int maxParallelism,
            IDomainId domainId,
            BlobStore.WebApi.Contracts.Client client,
            AgentTaskPluginExecutionContext context,
            CancellationToken cancellationToken)
        {
            var clientSettings = await BlobstoreClientSettings.GetClientSettingsAsync(
                connection,
                client,
                CreateArtifactsTracer(verbose, traceOutput),
                cancellationToken);

            return CreateDedupManifestClient(
                    context.IsSystemDebugTrue(),
                    (str) => context.Output(str),
                    connection,
                    DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                    domainId,
                    clientSettings,
                    context,
                    cancellationToken);
        }

        public (DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry) CreateDedupManifestClient(
            bool verbose,
            Action<string> traceOutput,
            VssConnection connection,
            int maxParallelism,
            IDomainId domainId,
            BlobstoreClientSettings clientSettings,
            AgentTaskPluginExecutionContext context,
            CancellationToken cancellationToken)
        {
            const int maxRetries = 5;
            var tracer = CreateArtifactsTracer(verbose, traceOutput);
            if (maxParallelism == 0)
            {
                maxParallelism = DefaultDedupStoreClientMaxParallelism;
            }

            traceOutput($"Max dedup parallelism: {maxParallelism}");
            traceOutput($"DomainId: {domainId}");

            IDedupStoreHttpClient dedupStoreHttpClient = GetDedupStoreHttpClient(connection, domainId, maxRetries, tracer, cancellationToken);

            var telemetry = new BlobStoreClientTelemetry(tracer, dedupStoreHttpClient.BaseAddress);
            this.HashType = clientSettings.GetClientHashType(context);

            if (this.HashType == BuildXL.Cache.ContentStore.Hashing.HashType.Dedup1024K)
            {
                dedupStoreHttpClient.RecommendedChunkCountPerCall = 10; // This is to workaround IIS limit - https://learn.microsoft.com/en-us/iis/configuration/system.webserver/security/requestfiltering/requestlimits/
            }
            traceOutput($"Hashtype: {this.HashType.Value}");

            dedupStoreHttpClient.SetRedirectTimeout(clientSettings.GetRedirectTimeout());

            var dedupClient = new DedupStoreClientWithDataport(dedupStoreHttpClient, new DedupStoreClientContext(maxParallelism), this.HashType.Value);
            return (new DedupManifestArtifactClient(telemetry, dedupClient, tracer), telemetry);
        }

        private static IDedupStoreHttpClient GetDedupStoreHttpClient(VssConnection connection, IDomainId domainId, int maxRetries, IAppTraceSource tracer, CancellationToken cancellationToken)
        {
            ArtifactHttpClientFactory factory = new ArtifactHttpClientFactory(
                connection.Credentials,
                connection.Settings.SendTimeout,
                tracer,
                cancellationToken);

            var helper = new HttpRetryHelper(maxRetries, e => true);

            IDedupStoreHttpClient dedupStoreHttpClient = helper.Invoke(
                () =>
                {
                    // since our call below is hidden, check if we are cancelled and throw if we are...
                    cancellationToken.ThrowIfCancellationRequested();

                    IDedupStoreHttpClient dedupHttpclient;
                    // this is actually a hidden network call to the location service:
                    if (domainId == WellKnownDomainIds.DefaultDomainId)
                    {
                        dedupHttpclient = factory.CreateVssHttpClient<IDedupStoreHttpClient, DedupStoreHttpClient>(connection.GetClient<DedupStoreHttpClient>().BaseAddress);
                    }
                    else
                    {
                        IDomainDedupStoreHttpClient domainClient = factory.CreateVssHttpClient<IDomainDedupStoreHttpClient, DomainDedupStoreHttpClient>(connection.GetClient<DomainDedupStoreHttpClient>().BaseAddress);
                        dedupHttpclient = new DomainHttpClientWrapper(domainId, domainClient);
                    }

                    return dedupHttpclient;
                });
            return dedupStoreHttpClient;
        }
        public (DedupStoreClient client, BlobStoreClientTelemetryTfs telemetry) CreateDedupClient(
            VssConnection connection,
            IDomainId domainId,
            int maxParallelism,
            int? redirectTimeoutSeconds,
            bool verbose,
            Action<string> traceOutput,
            CancellationToken cancellationToken)
        {
            const int maxRetries = 5;
            var tracer = CreateArtifactsTracer(verbose, traceOutput);
            if (maxParallelism == 0)
            {
                maxParallelism = DefaultDedupStoreClientMaxParallelism;
            }
            traceOutput("Creating dedup client:");
            traceOutput($" - Max dedup parallelism: {maxParallelism}");
            traceOutput($" - Using blobstore domain: {domainId}");
            traceOutput($" - Using redirect timeout: {redirectTimeoutSeconds}");

            var dedupStoreHttpClient = GetDedupStoreHttpClient(connection, domainId, maxRetries, tracer, cancellationToken);
            dedupStoreHttpClient.SetRedirectTimeout(redirectTimeoutSeconds);
            var telemetry = new BlobStoreClientTelemetryTfs(tracer, dedupStoreHttpClient.BaseAddress, connection);
            var client = new DedupStoreClient(dedupStoreHttpClient, maxParallelism);
            return (client, telemetry);
        }

        public int GetDedupStoreClientMaxParallelism(AgentTaskPluginExecutionContext context)
        {
            ConfigureEnvironmentVariables(context);

            int parallelism = DefaultDedupStoreClientMaxParallelism;

            if (context.Variables.TryGetValue("AZURE_PIPELINES_DEDUP_PARALLELISM", out VariableValue v))
            {
                if (!int.TryParse(v.Value, out parallelism))
                {
                    context.Output($"Could not parse the value of AZURE_PIPELINES_DEDUP_PARALLELISM, '{v.Value}', as an integer. Defaulting to {DefaultDedupStoreClientMaxParallelism}");
                    parallelism = DefaultDedupStoreClientMaxParallelism;
                }
                else
                {
                    context.Output($"Overriding default max parallelism with {parallelism}");
                }
            }
            else
            {
                context.Output($"Using default max parallelism.");
            }

            return parallelism;
        }

        private static readonly string[] EnvironmentVariables = new[] { "VSO_DEDUP_REDIRECT_TIMEOUT_IN_SEC" };

        private static void ConfigureEnvironmentVariables(AgentTaskPluginExecutionContext context)
        {
            foreach (string varName in EnvironmentVariables)
            {
                if (context.Variables.TryGetValue(varName, out VariableValue v))
                {
                    if (v.Value.Equals(Environment.GetEnvironmentVariable(varName), StringComparison.Ordinal))
                    {
                        context.Output($"{varName} is already set to `{v.Value}`.");
                    }
                    else
                    {
                        Environment.SetEnvironmentVariable(varName, v.Value);
                        context.Output($"Set {varName} to `{v.Value}`.");
                    }
                }
            }
        }


        public static IAppTraceSource CreateArtifactsTracer(bool verbose, Action<string> traceOutput)
        {
            return new CallbackAppTraceSource(
                str => traceOutput(str),
                verbose
                    ? System.Diagnostics.SourceLevels.Verbose
                    : System.Diagnostics.SourceLevels.Information,
                includeSeverityLevel: verbose);
        }
    }
}