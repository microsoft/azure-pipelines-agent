using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public interface IJobExtension : IExtension
    {
        IList<string> HostTypes { get; }
        IStep PrepareStep { get; }
        IStep FinallyStep { get; }
        string GetRootedPath(IExecutionContext context, string path);
        void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath);
    }
}