// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node10Strategy : INodeVersionStrategy
    {
        public string Name => "Node10";

        public bool CanHandle(NodeContext context)
        {
            bool hasNode10Handler = context.HandlerData is Node10HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();
            
            if (hasNode10Handler)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected for Node10 task handler");
            }

            bool isAlpine = context.IsAlpine;
            if (isAlpine)
            {
                context.ExecutionContext.Warning(
                    "Using Node10 on Alpine Linux because Node6 is not compatible. " +
                    "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24 for continued support.");
                
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected for Alpine Linux compatibility (Node6 incompatible)");
            }

            return false;
        }

        private bool DetermineNodeVersionAndSetContext(NodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node10"));
            }

            context.SelectedNodeVersion = "node10";
            context.SelectionReason = baseReason;
            context.SelectionWarning = StringUtil.Loc("NodeEOLWarning", "Node10");
            return true;
        }

        public NodeRunnerInfo GetNodePath(NodeContext context)
        {
            string externalsPath = context.HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostPath = Path.Combine(externalsPath, context.SelectedNodeVersion, "bin", $"node{IOUtil.ExeExtension}");
            string finalPath = context.IsContainer && context.Container != null ? 
                              context.Container.TranslateToContainerPath(hostPath) : hostPath;

            return new NodeRunnerInfo
            {
                NodePath = finalPath,
                NodeVersion = context.SelectedNodeVersion,
                Reason = context.SelectionReason,
                Warning = context.SelectionWarning
            };
        }
    }
}
