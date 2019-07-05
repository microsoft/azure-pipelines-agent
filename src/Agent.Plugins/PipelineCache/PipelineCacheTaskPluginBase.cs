using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;

namespace Agent.Plugins.PipelineCache
{
    public abstract class PipelineCacheTaskPluginBase : IAgentTaskPlugin
    {
        public Guid Id => PipelineCachePluginConstants.CacheTaskId;

        public abstract String Stage { get; }

        public async Task RunAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
        {
            ArgUtil.NotNull(context, nameof(context));

            string saltVariableName = "AZDEVOPS_PIPELINECACHE_SALT";
            VariableValue salt = context.Variables.GetValueOrDefault(saltVariableName);

            Func<string,string[]> splitIntoSegments = (s) => {
                var segments = s.Trim().Split(new [] {'|'},StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Trim());
                if(salt != null)
                {
                    segments = (new [] { $"{saltVariableName}={salt.Value}"}).Concat(segments);
                }
                return segments.ToArray();
            };

            string key = context.GetInput(PipelineCacheTaskPluginConstants.Key, required: true);

            string restoreKeysBlock = context.GetInput(PipelineCacheTaskPluginConstants.RestoreKeys, required: false);

            IEnumerable<string> restoreKeysPerKey;
            if(string.IsNullOrWhiteSpace(restoreKeysBlock))
            {
                restoreKeysPerKey = Enumerable.Empty<string>();
            }
            else
            {
                restoreKeysBlock = restoreKeysBlock.Replace("\r\n", "\n"); //normalize newlines
                restoreKeysPerKey = restoreKeysBlock.Split(new [] {'\n'}, StringSplitOptions.RemoveEmptyEntries); // split by marker
                restoreKeysPerKey = restoreKeysPerKey.Select(k => $"{k} | **"); // all restore-only keys are assumed to be wildcards
            }

            IEnumerable<string> rawFingerprints = (new [] { key }).Concat(restoreKeysPerKey);
            Fingerprint[] resovledFingerprints = rawFingerprints.Select(f => {
                context.Output($"Resolving key `{f}...");
                Fingerprint fp = FingerprintCreator.CreateFingerprint(context, splitIntoSegments(f));
                context.Output($"Resolved to `{fp}.");
                return fp;
            }).ToArray();

            // TODO: Translate path from container to host (Ting)
            string path = context.GetInput(PipelineCacheTaskPluginConstants.Path, required: true);

            await ProcessCommandInternalAsync(
                context,
                resovledFingerprints,
                path,
                token);
        }

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context,
            Fingerprint[] fingerprints,
            string path,
            CancellationToken token);

        // Properties set by tasks
        protected static class PipelineCacheTaskPluginConstants
        {
            public static readonly string Key = "key"; // this needs to match the input in the task.
            public static readonly string RestoreKeys = "restoreKeys";
            public static readonly string Path = "path";
            public static readonly string PipelineId = "pipelineId";
            public static readonly string CacheHitVariable = "cacheHitVar";
            public static readonly string Salt = "salt";

        }
    }
}