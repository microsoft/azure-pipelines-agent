// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Interface for checking glibc compatibility with Node.js versions on Linux systems.
    /// </summary>
    [ServiceLocator(Default = typeof(GlibcCompatibilityInfoProvider))]
    public interface IGlibcCompatibilityInfoProvider : IAgentService
    {
        /// <summary>
        /// Checks glibc compatibility for both Node20 and Node24.
        /// </summary>
        /// <returns>GlibcCompatibilityInfo containing compatibility results for both Node versions</returns>
        Task<GlibcCompatibilityInfo> CheckGlibcCompatibilityAsync();

        /// <summary>
        /// Gets glibc compatibility information, adapting to execution context (host vs container).
        /// </summary>
        /// <param name="context">Task execution context for determining environment</param>
        /// <returns>GlibcCompatibilityInfo containing compatibility results for both Node versions</returns>
        Task<GlibcCompatibilityInfo> GetGlibcCompatibilityAsync(TaskContext context);

        /// <summary>
        /// Checks if the specified Node.js version results in glibc compatibility errors.
        /// </summary>
        /// <param name="nodeFolder">The node folder name (e.g., "node20_1", "node24")</param>
        /// <returns>True if glibc error is detected, false otherwise</returns>
        Task<bool> CheckIfNodeResultsInGlibCErrorAsync(string nodeFolder);
    }
}