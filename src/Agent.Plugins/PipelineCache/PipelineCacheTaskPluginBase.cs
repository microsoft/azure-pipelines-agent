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
                var segments = s.Split(new [] {'|'},StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Trim());
                if(salt != null)
                {
                    segments = (new [] { $"{saltVariableName}={salt.Value}"}).Concat(segments);
                }
                return segments.ToArray();
            };

            string key = context.GetInput(PipelineCacheTaskPluginConstants.Key, required: true);
            context.Output($"Resolving key `{key}`...");
            Fingerprint keyFp = FingerprintCreator.ParseFromYAML(context, splitIntoSegments(key), addWildcard: false);
            context.Output($"Resolved to `{keyFp}`.");

            string restoreKeysBlock = context.GetInput(PipelineCacheTaskPluginConstants.RestoreKeys, required: false);

            IEnumerable<Fingerprint> fingerprints = new [] { keyFp };
            if(!string.IsNullOrWhiteSpace(restoreKeysBlock))
            {
                restoreKeysBlock = restoreKeysBlock.Replace("\r\n", "\n"); //normalize newlines
                string[] restoreKeys = restoreKeysBlock.Split(new [] {'\n'}, StringSplitOptions.RemoveEmptyEntries); // split by newline
                fingerprints = fingerprints.Concat(restoreKeys.Select(restoreKey => {
                    context.Output($"Resolving restore key `{restoreKey}`...");
                    Fingerprint f = FingerprintCreator.ParseFromYAML(context, splitIntoSegments(restoreKey), addWildcard: true);
                    context.Output($"Resolved to `{f}`.");
                    return f;
                }));
            }

            // TODO: Translate path from container to host (Ting)
            string path = context.GetInput(PipelineCacheTaskPluginConstants.Path, required: true);

            await ProcessCommandInternalAsync(
                context,
                fingerprints.ToArray(),
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