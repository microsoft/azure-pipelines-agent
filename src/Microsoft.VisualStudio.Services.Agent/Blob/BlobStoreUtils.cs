// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Agent.Util;
using BlobIdentifierWithBlocks = Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifierWithBlocks;
using VsoHash = Microsoft.VisualStudio.Services.BlobStore.Common.VsoHash;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    /// <summary>
    /// Util functions for uploading files chunk-dedup blob store
    /// </summary>
    public static class BlobStoreUtils
    {
        public static async Task<(DedupIdentifier dedupId, ulong length)> UploadToBlobStore<T>(
            bool verbose,
            string itemPath,
            Func<TelemetryInformationLevel, Uri, string, T> telemetryRecordFactory,
            Action<string> traceOutput,
            DedupStoreClient dedupClient,
            BlobStoreClientTelemetry clientTelemetry,
            CancellationToken cancellationToken) where T : BlobStoreTelemetryRecord
        {
            // Create chunks and identifier
            var chunk = await ChunkerHelper.CreateFromFileAsync(FileSystem.Instance, itemPath, cancellationToken, false);
            var rootNode = new DedupNode(new []{ chunk });
            // ChunkHelper uses 64k block default size
            var dedupId = rootNode.GetDedupIdentifier(HashType.Dedup64K);

            // Setup upload session to keep file for at mimimum one day
            // Blobs will need to be associated with the server with an ID ref otherwise they will be
            // garbage collected after one day
            var tracer = DedupManifestArtifactClientFactory.CreateArtifactsTracer(verbose, traceOutput);
            var keepUntilRef = new KeepUntilBlobReference(DateTime.UtcNow.AddDays(1));
            var uploadSession = dedupClient.CreateUploadSession(keepUntilRef, tracer, FileSystem.Instance);

            // Upload the chunks
            var uploadRecord = clientTelemetry.CreateRecord<T>(telemetryRecordFactory);
            await clientTelemetry.MeasureActionAsync(
                record: uploadRecord,
                actionAsync: async () => await AsyncHttpRetryHelper.InvokeAsync(
                        async () =>
                        {
                            return await uploadSession.UploadAsync(rootNode, new Dictionary<DedupIdentifier, string>(){ [dedupId] = itemPath }, cancellationToken);
                        },
                        maxRetries: 3,
                        tracer: tracer,
                        canRetryDelegate: e => true, // this isn't great, but failing on upload stinks, so just try a couple of times
                        cancellationToken: cancellationToken,
                        continueOnCapturedContext: false)
            );
            return (dedupId, rootNode.TransitiveContentBytes);
        }

        public static async Task<(DedupIdentifier dedupId, ulong length)> UploadToBlobStore<T>(
            bool verbose,
            string itemPath,
            Func<TelemetryInformationLevel, Uri, string, T> telemetryRecordFactory,
            Action<string> traceOutput,
            VssConnection connection,
            CancellationToken cancellationToken) where T : BlobStoreTelemetryRecord
        {
            var (dedupClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance
                    .CreateDedupClientAsync(verbose, traceOutput, connection, cancellationToken);

            return await UploadToBlobStore<T>(verbose, itemPath, telemetryRecordFactory, traceOutput, dedupClient, clientTelemetry, cancellationToken);
        }
    }
}