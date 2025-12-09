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

    public sealed class UnifiedNode6Strategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "Node6";

        public bool CanHandle(UnifiedNodeContext context)
        {
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();
            
            bool hasNode6Handler = context.HandlerData != null && context.HandlerData.GetType() == typeof(NodeHandlerData);

            if (hasNode6Handler)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected for Node6 task handler");
            }
            
            return false;
        }

        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node6"));
            }

            context.SelectedNodeVersion = "node";
            context.SelectionReason = baseReason;
            context.SelectionWarning = StringUtil.Loc("NodeEOLWarning", "Node6");
            return true;
        }

        public NodePathResult GetNodePath(UnifiedNodeContext context)
        {
            string externalsPath = context.HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostPath = Path.Combine(externalsPath, context.SelectedNodeVersion, "bin", $"node{IOUtil.ExeExtension}");
            string finalPath = context.IsContainer && context.Container != null ? 
                              context.Container.TranslateToContainerPath(hostPath) : hostPath;

            return new NodePathResult
            {
                NodePath = finalPath,
                NodeVersion = context.SelectedNodeVersion,
                Reason = context.SelectionReason,
                Warning = context.SelectionWarning
            };
        }
    }
}