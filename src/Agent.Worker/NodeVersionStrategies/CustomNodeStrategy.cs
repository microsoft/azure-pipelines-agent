// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class CustomNodeStrategy : INodeVersionStrategy
    {
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            string customPath = null;
            string source = null;

            if (context.Container == null && context.StepTarget != null)
            {
                customPath = context.StepTarget.CustomNodePath;
                source = "StepTarget.CustomNodePath";
            }
            else if (context.Container != null)
            {
                customPath = context.Container.CustomNodePath;
                source = "Container.CustomNodePath";
            }

            if (string.IsNullOrWhiteSpace(customPath))
            {
                executionContext.Debug("[CustomNodeStrategy] No custom node path found");
                return null;
            }

            executionContext.Debug($"[CustomNodeStrategy] Found custom node path in {source}: {customPath}");

            return new NodeRunnerInfo
            {
                NodePath = customPath,
                NodeVersion = NodeVersion.Custom,
                Reason = $"Custom Node.js path specified by user ({source})",
                Warning = null
            };
        }

        public NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            // Use the same logic as CanHandle, but specifically for container scenarios
            return CanHandle(context, executionContext, null);
        }
    }
}