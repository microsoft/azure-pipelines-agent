using System;
using Xunit;
using System.IO;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Moq;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class DirectoryOwnershipL0
    {
        private Mock<IConfigurationStore> _configurationStore;
        private Mock<ITerminal> _term;
        private AgentSettings _agentSettings;

        public DirectoryOwnershipL0()
        {
            _agentSettings = new AgentSettings();
            _agentSettings.AgentId = 999;
            _agentSettings.AgentName = "defaultAgent999";
            _agentSettings.PoolId = 999;
            _agentSettings.PoolName = "defaultPool999";
            _agentSettings.ServerUrl = "https://vstsagent.test.com";
            _agentSettings.WorkFolder = "_work";

            _configurationStore = new Mock<IConfigurationStore>();
            _configurationStore.Setup(x => x.GetSettings()).Returns(_agentSettings);

            _term = new Mock<ITerminal>();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void RegisterDirectoryOwnershipMultipleTimes()
        {
            // Configure same agent to use the same workfolder over and over again, but some unconfig failed that leave ownship file around
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                try
                {
                    var dirOwner = new DirectoryOwnershipTracker();
                    dirOwner.Initialize(hc);
                    dirOwner.RegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));
                    dirOwner.RegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));
                    dirOwner.EnsureDirectoryOwneByAgent(hc.GetDirectory(WellKnownDirectory.Work));

                    Assert.True(File.Exists(IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work))));
                }
                finally
                {
                    string existOwnshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                    if (File.Exists(existOwnshipFile))
                    {
                        File.Delete(existOwnshipFile);
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void RegisterDirectoryOwnershipOverwrite()
        {
            // Configure agent to use a workfolder leaved by another agent.
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                string existOwnshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                try
                {
                    // create a fake .ownship file.                    
                    DirectoryOwnershipInfo ownership = new DirectoryOwnershipInfo();
                    ownership.AgentName = "agent1";
                    ownership.AgentPath = Path.GetTempPath();
                    ownership.PoolId = 1;
                    ownership.ServerUrl = "https://visualStudio.com";
                    IOUtil.SaveObject(ownership, existOwnshipFile);

                    var dirOwner = new DirectoryOwnershipTracker();
                    dirOwner.Initialize(hc);
                    dirOwner.RegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));
                    dirOwner.EnsureDirectoryOwneByAgent(hc.GetDirectory(WellKnownDirectory.Work));

                    Assert.True(File.Exists(IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work))));
                }
                finally
                {
                    if (File.Exists(existOwnshipFile))
                    {
                        File.Delete(existOwnshipFile);
                    }
                }
            }
        }


        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void RegisterUnregiesterDirectoryOwnership()
        {
            // Configure and unconfigure an agent.
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                try
                {
                    var dirOwner = new DirectoryOwnershipTracker();
                    dirOwner.Initialize(hc);
                    dirOwner.RegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));
                    dirOwner.UnRegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));

                    string ownshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                    Assert.False(File.Exists(ownshipFile));
                }
                finally
                {
                    string existOwnshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                    if (File.Exists(existOwnshipFile))
                    {
                        File.Delete(existOwnshipFile);
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void UnregiesterDirectoryOwnershipNonExist()
        {
            // Unconfigure an agent that the workfolder might been deleted.
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                string existOwnshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                if (File.Exists(existOwnshipFile))
                {
                    File.Delete(existOwnshipFile);
                }

                var dirOwner = new DirectoryOwnershipTracker();
                dirOwner.Initialize(hc);
                dirOwner.UnRegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));

                string ownshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                Assert.False(File.Exists(ownshipFile));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void CheckDirectoryOwnershipSucceed()
        {
            // register the ownship then check the ownship
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                try
                {
                    var dirOwner = new DirectoryOwnershipTracker();
                    dirOwner.Initialize(hc);
                    dirOwner.RegisterDirectoryOwnership(hc.GetDirectory(WellKnownDirectory.Work));
                    dirOwner.EnsureDirectoryOwneByAgent(hc.GetDirectory(WellKnownDirectory.Work));

                    Assert.True(File.Exists(IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work))));
                }
                finally
                {
                    string existOwnshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                    if (File.Exists(existOwnshipFile))
                    {
                        File.Delete(existOwnshipFile);
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void CheckDirectoryOwnershipThrow()
        {
            // Check the ownship for an not owned directory
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                string existOwnshipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                try
                {
                    // create a fake .ownship file.                    
                    DirectoryOwnershipInfo ownership = new DirectoryOwnershipInfo();
                    ownership.AgentName = "agent1";
                    ownership.AgentPath = Path.GetTempPath();
                    ownership.PoolId = 1;
                    ownership.ServerUrl = "https://visualStudio.com";
                    IOUtil.SaveObject(ownership, existOwnshipFile);

                    var dirOwner = new DirectoryOwnershipTracker();
                    dirOwner.Initialize(hc);
                    Assert.Throws<DirectoryOwnershipMismatchException>(() => dirOwner.EnsureDirectoryOwneByAgent(hc.GetDirectory(WellKnownDirectory.Work)));
                }
                finally
                {
                    if (File.Exists(existOwnshipFile))
                    {
                        File.Delete(existOwnshipFile);
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void CheckDirectoryOwnershipRegisterOwnershipOnNewFolder()
        {
            // The first time the agent that will create the onwership file on the directory withour reconfig
            using (var hc = new TestHostContext(this))
            {
                hc.SetSingleton(_term.Object);
                hc.SetSingleton(_configurationStore.Object);

                string ownershipFile = IOUtil.GetDirectoryOwnershipFilePath(hc.GetDirectory(WellKnownDirectory.Work));
                try
                {
                    var dirOwner = new DirectoryOwnershipTracker();
                    dirOwner.Initialize(hc);
                    dirOwner.EnsureDirectoryOwneByAgent(hc.GetDirectory(WellKnownDirectory.Work));

                    Assert.True(File.Exists(ownershipFile));
                }
                finally
                {
                    if (File.Exists(ownershipFile))
                    {
                        File.Delete(ownershipFile);
                    }
                }
            }
        }
    }
}