using System;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    [DataContract]
    public class DirectoryOwnershipInfo
    {
        [DataMember]
        public string ServerUrl { get; set; }

        [DataMember]
        public int PoolId { get; set; }

        [DataMember]
        public string AgentName { get; set; }

        [DataMember]
        public string AgentPath { get; set; }
    }

    public sealed class DirectoryOwnershipMismatchException : Exception
    {
        public DirectoryOwnershipMismatchException()
            : base()
        { }

        public DirectoryOwnershipMismatchException(string message)
            : base(message)
        { }

        public DirectoryOwnershipMismatchException(string message, Exception inner)
            : base(message, inner)
        { }
    }

    [ServiceLocator(Default = typeof(DirectoryOwnershipTracker))]
    public interface IDirectoryOwnershipTracker : IAgentService
    {
        void RegisterDirectoryOwnership(string path);
        void UnRegisterDirectoryOwnership(string path);
        void EnsureDirectoryOwneByAgent(string path);
    }

    public sealed class DirectoryOwnershipTracker : AgentService, IDirectoryOwnershipTracker
    {
        public void RegisterDirectoryOwnership(string path)
        {
            // Register ownership always overwrite existing ownership
            Trace.Entering();

            // ensure directory exist
            Directory.CreateDirectory(path);

            var configurationStore = HostContext.GetService<IConfigurationStore>();
            var agentSettings = configurationStore.GetSettings();

            // create ownership info 
            DirectoryOwnershipInfo ownership = new DirectoryOwnershipInfo()
            {
                ServerUrl = agentSettings.ServerUrl,
                PoolId = agentSettings.PoolId,
                AgentName = agentSettings.AgentName,
                AgentPath = HostContext.GetDirectory(WellKnownDirectory.Root)
            };

            Trace.Info($"Stamp ownership info for directory: {path}{Environment.NewLine}{StringUtil.ConvertToJson(ownership)}");

            // create .ownership file
            string ownershipFile = IOUtil.GetDirectoryOwnershipFilePath(path);
            if (File.Exists(ownershipFile))
            {
                // trace and overwrite existing ownership file.
                Trace.Info($"Load exist ownership info from {ownershipFile}");
                var existOwnership = IOUtil.LoadObject<DirectoryOwnershipInfo>(ownershipFile);
                Trace.Info(StringUtil.ConvertToJson(existOwnership));

                var term = HostContext.GetService<ITerminal>();
                term.WriteLine(StringUtil.Loc("OverwriteDirectoryOwnership", existOwnership.AgentPath));

                IOUtil.DeleteFile(ownershipFile);
            }

            IOUtil.SaveObject(ownership, ownershipFile);
            Trace.Info($"Directory ownership tracking file created: {ownershipFile}");
        }

        public void UnRegisterDirectoryOwnership(string path)
        {
            // Unregister ownership is best effort
            Trace.Entering();
            if (!Directory.Exists(path))
            {
                Trace.Info($"Directory doesn't exist: {path}");
                return;
            }

            string ownershipFile = IOUtil.GetDirectoryOwnershipFilePath(path);
            if (!File.Exists(ownershipFile))
            {
                Trace.Info($"Directory ownership file doesn't exist: {ownershipFile}");
                return;
            }

            // make sure the current agent own the directory before delete the ownership file.
            try
            {
                EnsureDirectoryOwneByAgent(path);
            }
            catch (Exception ex)
            {
                Trace.Error("Can't remove directory ownership file, since the current agent doesn't own the directory.");
                Trace.Error(ex);
                return;
            }

            Trace.Info($"Remove directory ownership tracking file {ownershipFile}.");
            IOUtil.DeleteFile(ownershipFile);
        }

        public void EnsureDirectoryOwneByAgent(string path)
        {
            Trace.Entering();

            // ensure directory exist
            Directory.CreateDirectory(path);

            string ownershipFile = IOUtil.GetDirectoryOwnershipFilePath(path);
            if (!File.Exists(ownershipFile))
            {
                // The folder doesn't belongs to any agent, register the ownership.
                // This can happen when customer change the workFolder after agent config by modify the .agent json file.
                RegisterDirectoryOwnership(path);
            }
            else
            {
                var ownership = IOUtil.LoadObject<DirectoryOwnershipInfo>(ownershipFile);
                ArgUtil.NotNull(ownership, nameof(DirectoryOwnershipInfo));

                var configurationStore = HostContext.GetService<IConfigurationStore>();
                var agentSettings = configurationStore.GetSettings();

                if (!string.Equals(ownership.ServerUrl, agentSettings.ServerUrl, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(ownership.AgentName, agentSettings.AgentName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(ownership.AgentPath, HostContext.GetDirectory(WellKnownDirectory.Root), StringComparison.OrdinalIgnoreCase) ||
                    ownership.PoolId != agentSettings.PoolId)
                {
                    throw new DirectoryOwnershipMismatchException(StringUtil.Loc("DirectoryOwnByOtherAgent", path, ownership.AgentName, ownership.ServerUrl, ownership.PoolId, ownership.AgentName));
                }
            }
        }
    }
}