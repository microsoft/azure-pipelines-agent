// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Result containing the selected Node path and metadata.
    /// Used by unified strategy pattern for both host and container node selection.
    /// </summary>
    public sealed class NodePathResult
    {
        /// <summary>
        /// Full path to the node executable.
        /// </summary>
        public string NodePath { get; set; }

        /// <summary>
        /// The node version folder name (e.g., "node24", "node20_1", "node16").
        /// </summary>
        public string NodeVersion { get; set; }

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
