// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Represents the available Node.js versions supported by the agent.
    /// </summary>
    public enum NodeVersion
    {
        Node6,
        Node10,
        Node16,
        Node20,
        Node24
    }

    /// <summary>
    /// Helper class for NodeVersion operations.
    /// </summary>
    public static class NodeVersionHelper
    {
        /// <summary>
        /// Gets the folder name for the specified NodeVersion.
        /// </summary>
        public static string GetFolderName(NodeVersion version)
        {
            return version switch
            {
                NodeVersion.Node6 => "node",
                NodeVersion.Node10 => "node10",
                NodeVersion.Node16 => "node16",
                NodeVersion.Node20 => "node20_1",
                NodeVersion.Node24 => "node24",
                _ => throw new ArgumentOutOfRangeException(nameof(version))
            };
        }
    }

    /// <summary>
    /// Result containing the selected Node path and metadata.
    /// Used by strategy pattern for both host and container node selection.
    /// </summary>
    public sealed class NodeRunnerInfo
    {
        /// <summary>
        /// Full path to the node executable.
        /// </summary>
        public string NodePath { get; set; }

        /// <summary>
        /// The node version selected.
        /// </summary>
        public NodeVersion NodeVersion { get; set; }

        /// <summary>
        /// Explanation of why this version was selected.
        /// Used for debugging and telemetry.
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Optional warning message to display to user.
        /// Example: "Container OS doesn't support Node24, using Node20 instead."
        /// </summary>
        public string Warning { get; set; }
    }
}
