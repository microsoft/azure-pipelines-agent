// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
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
        public static async Task<(List<BlobFileInfo> fileDedupIds, ulong length)> UploadBatchToBlobstore(
            bool verbose,
            IReadOnlyList<string> itemPaths,
            Func<TelemetryInformationLevel, Uri, string, BlobStoreTelemetryRecord> telemetryRecordFactory,
            Action<string> traceOutput,
            DedupStoreClient dedupClient,
            BlobStoreClientTelemetry clientTelemetry,
            CancellationToken cancellationToken,
            bool enableReporting = false)
        {
            // Create chunks and identifier
            traceOutput(StringUtil.Loc("BuildingFileTree"));            
            var fileNodes = await GenerateHashes(itemPaths, dedupClient.HashType, cancellationToken);
            var rootNode = CreateNodeToUpload(fileNodes.Where(x => x.Success).Select(y => y.Node));

            // If there are multiple paths to one DedupId (duplicate files)
            // take the last one
            var fileDedupIds = new Dictionary<DedupIdentifier, string>();
            foreach (var file in fileNodes.Where(x => x.Success))
            {
                // ChunkHelper uses 64k block default size
                var dedupId = file.Node.GetDedupIdentifier();
                fileDedupIds[dedupId] = file.Path;
            }

            // Setup upload session to keep file for at mimimum one day
            // Blobs will need to be associated with the server with an ID ref otherwise they will be
            // garbage collected after one day
            var tracer = DedupManifestArtifactClientFactory.CreateArtifactsTracer(verbose, traceOutput);
            var keepUntilRef = new KeepUntilBlobReference(DateTime.UtcNow.AddDays(1));
            var uploadSession = dedupClient.CreateUploadSession(keepUntilRef, tracer, FileSystem.Instance);

            using (var reportingCancelSrc = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // Log stats
                Task reportingTask = null;
                if (enableReporting)
                {
                    reportingTask = StartReportingTask(traceOutput, (long)rootNode.TransitiveContentBytes, uploadSession, reportingCancelSrc);
                }

                // Upload the chunks
                var uploadRecord = clientTelemetry.CreateRecord<BlobStoreTelemetryRecord>(telemetryRecordFactory);
                await clientTelemetry.MeasureActionAsync(
                    record: uploadRecord,
                    actionAsync: async () => await AsyncHttpRetryHelper.InvokeAsync(
                            async () =>
                            {
                                await uploadSession.UploadAsync(rootNode, fileDedupIds, cancellationToken);
                                return uploadSession.UploadStatistics;
                            },
                            maxRetries: 3,
                            tracer: tracer,
                            canRetryDelegate: e => true, // this isn't great, but failing on upload stinks, so just try a couple of times
                            cancellationToken: cancellationToken,
                            continueOnCapturedContext: false)
                );

                if (enableReporting)
                {
                    reportingCancelSrc.Cancel();
                    await reportingTask;
                }
            }

            return (fileNodes, rootNode.TransitiveContentBytes);
        }

        private static Task StartReportingTask(Action<string> traceOutput, long totalBytes, IDedupUploadSession uploadSession, CancellationTokenSource reportingCancel)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!reportingCancel.IsCancellationRequested)
                    {
                        traceOutput($"Uploaded {uploadSession.UploadStatistics.TotalContentBytes:N0} out of {totalBytes:N0} bytes.");
                        await Task.Delay(10000, reportingCancel.Token);
                    }
                }
                catch (OperationCanceledException oce) when (oce.CancellationToken == reportingCancel.Token)
                {
                    // Expected
                }
                // Print final result
                traceOutput($"Uploaded {uploadSession.UploadStatistics.TotalContentBytes:N0} out of {totalBytes:N0} bytes.");
            });
        }

        private static async Task<List<BlobFileInfo>> GenerateHashes(IReadOnlyList<string> filePaths, HashType hashType, CancellationToken cancellationToken)
        {
            var nodes = new BlobFileInfo[filePaths.Count];
            var queue = NonSwallowingActionBlock.Create<int>(
                async i =>
                {
                    var itemPath = filePaths[i];
                    try
                    {
                        var dedupNode = await ChunkerHelper.CreateFromFileAsync(FileSystem.Instance, itemPath, false, hashType, cancellationToken);
                        nodes[i] = new BlobFileInfo
                        {
                            Path = itemPath,
                            Node = dedupNode,
                            Success = true
                        };
                    }
                    catch (Exception)
                    {
                        nodes[i] = new BlobFileInfo
                        {
                            Path = itemPath,
                            Success = false
                        };
                    }
                },
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken,
                });

            await queue.SendAllAndCompleteSingleBlockNetworkAsync(Enumerable.Range(0, filePaths.Count), cancellationToken);

            return nodes.ToList();
        }

        private static DedupNode CreateNodeToUpload(IEnumerable<DedupNode> nodes)
        {
            while (nodes.Count() > 1)
            {
                nodes = nodes
                    .GetPages(DedupNode.MaxDirectChildrenPerNode)
                    .Select(children => new DedupNode(children))
                    .ToList();
            }

            DedupNode root = nodes.Single();
            if (root.Type == DedupNode.NodeType.ChunkLeaf)
            {
                root = new DedupNode(new[] { root });
            }

            return root;
        }

        public static async Task<(DedupIdentifier dedupId, ulong length)> UploadToBlobStore(
            bool verbose,
            string itemPath,
            Func<TelemetryInformationLevel, Uri, string, BlobStoreTelemetryRecord> telemetryRecordFactory,
            Action<string> traceOutput,
            DedupStoreClient dedupClient,
            BlobStoreClientTelemetry clientTelemetry,
            CancellationToken cancellationToken)
        {
            // Create chunks and identifier
            var chunk = await ChunkerHelper.CreateFromFileAsync(FileSystem.Instance, itemPath, cancellationToken, false);
            var rootNode = new DedupNode(new[] { chunk });
            // ChunkHelper uses 64k block default size
            var dedupId = rootNode.GetDedupIdentifier();

            // Setup upload session to keep file for at mimimum one day
            // Blobs will need to be associated with the server with an ID ref otherwise they will be
            // garbage collected after one day
            var tracer = DedupManifestArtifactClientFactory.CreateArtifactsTracer(verbose, traceOutput);
            var keepUntilRef = new KeepUntilBlobReference(DateTime.UtcNow.AddDays(1));
            var uploadSession = dedupClient.CreateUploadSession(keepUntilRef, tracer, FileSystem.Instance);

            // Upload the chunks
            var uploadRecord = clientTelemetry.CreateRecord<BlobStoreTelemetryRecord>(telemetryRecordFactory);
            await clientTelemetry.MeasureActionAsync(
                record: uploadRecord,
                actionAsync: async () => await AsyncHttpRetryHelper.InvokeAsync(
                        async () =>
                        {
                            await uploadSession.UploadAsync(rootNode, new Dictionary<DedupIdentifier, string>() { [dedupId] = itemPath }, cancellationToken);
                            return uploadSession.UploadStatistics;
                        },
                        maxRetries: 3,
                        tracer: tracer,
                        canRetryDelegate: e => true, // this isn't great, but failing on upload stinks, so just try a couple of times
                        cancellationToken: cancellationToken,
                        continueOnCapturedContext: false)
            );
            return (dedupId, rootNode.TransitiveContentBytes);
        }
    }
}