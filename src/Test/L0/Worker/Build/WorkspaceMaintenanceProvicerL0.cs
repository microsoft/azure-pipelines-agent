// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Moq;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Test.L0.Worker.Build
{
    public sealed class WorkspaceMaintenanceProvicerL0
    {
        private Mock<IExecutionContext> _ec;
        private Mock<ITrackingManager> _trackingManager;
        private WorkspaceMaintenanceProvider _workspaceMaintenanceProvider;
        private Variables _variables;


        private TestHostContext Setup(int daysthreshold = 0, [CallerMemberName] string name = "")
        {
            // Setup the host context.
            TestHostContext hc = new TestHostContext(this, name);
            _ec = new Mock<IExecutionContext>();
            _trackingManager = new Mock<ITrackingManager>();
            List<string> warnings;
            _variables = new Variables(hc, new Dictionary<string, VariableValue>(), out warnings);
            _variables.Set(Constants.Variables.Maintenance.JobTimeout, "0");
            _variables.Set(Constants.Variables.Maintenance.DeleteWorkingDirectoryDaysThreshold, daysthreshold.ToString());
            _ec.Setup(x => x.Variables).Returns(_variables);
            _workspaceMaintenanceProvider = new WorkspaceMaintenanceProvider();
            _workspaceMaintenanceProvider.Initialize(hc);
            hc.SetSingleton<ITrackingManager>(_trackingManager.Object);
            Tracing trace = hc.GetTrace();
            return hc;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        private async Task ShouldMarkExpiredForGarbageCollection()
        {
            var daysthreshold = 4;
            using (TestHostContext hc = Setup(daysthreshold))
            {
                _trackingManager.Setup(x => x.MarkExpiredForGarbageCollection(_ec.Object, It.IsAny<TimeSpan>()));

                await _workspaceMaintenanceProvider.RunMaintenanceOperation(_ec.Object);

                _trackingManager.Verify(x => x.MarkExpiredForGarbageCollection(_ec.Object, TimeSpan.FromDays(daysthreshold)), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        private async Task ShouldNotMarkForGarbageCollectionIfThresholdIsZero()
        {
            var daysthreshold = 0;
            using (TestHostContext hc = Setup(daysthreshold))
            {
                _trackingManager.Setup(x => x.MarkExpiredForGarbageCollection(_ec.Object, It.IsAny<TimeSpan>()));

                await _workspaceMaintenanceProvider.RunMaintenanceOperation(_ec.Object);

                _trackingManager.Verify(x => x.MarkExpiredForGarbageCollection(_ec.Object, It.IsAny<TimeSpan>()), Times.Never);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        private async Task ShouldDisposeCollectedGarbage()
        {
            using (TestHostContext hc = Setup(4))
            {
                await _workspaceMaintenanceProvider.RunMaintenanceOperation(_ec.Object);

                _trackingManager.Verify(x => x.DisposeCollectedGarbage(_ec.Object), Times.Once);
            }
        }

    }
}
