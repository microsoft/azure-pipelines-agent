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
    public sealed class Node16Strategy : INodeVersionStrategy
    {
        public string Name => "Node16";

        public bool CanHandle(NodeContext context)
        {
            bool hasNode16Handler = context.HandlerData is Node16HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            if (hasNode16Handler)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected for Node16 task handler");
            }

            return false;
        }

        private bool DetermineNodeVersionAndSetContext(NodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            context.SelectedNodeVersion = "node16";
            context.SelectionReason = baseReason;
            context.SelectionWarning = StringUtil.Loc("NodeEOLWarning", "Node16");
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
