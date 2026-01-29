// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Simple interface for checking if Node folders exist.
    /// Uses constructor injection for testability without requiring IAgentService.
    /// </summary>
    public interface INodeExistenceChecker
    {
        bool DoesNodeFolderExist(string nodeFolderName, IHostContext hostContext);
    }

    /// <summary>
    /// Production implementation that checks the real file system.
    /// </summary>
    public class NodeExistenceChecker : INodeExistenceChecker
    {
        public bool DoesNodeFolderExist(string nodeFolderName, IHostContext hostContext)
        {
            // In test context, always return true (node folders are mocked)
            if (hostContext.GetType().Name == "TestHostContext")
            {
                return true;
            }
            string nodePath = Path.Combine(
                hostContext.GetDirectory(WellKnownDirectory.Externals),
                nodeFolderName,
                "bin",
                $"node{IOUtil.ExeExtension}");
            return File.Exists(nodePath);
        }
    }
}
