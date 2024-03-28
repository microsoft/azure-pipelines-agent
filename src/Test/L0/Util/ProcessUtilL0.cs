// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L0.Util
{
    public sealed class WindowsProcessUtilL0
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void Test_GetProcessList(bool useInteropToFindParentProcess)
        {
            using TestHostContext hc = new TestHostContext(this);
            Tracing trace = hc.GetTrace();

            // Arrange: This test is based on the current process.
            Process currentProcess = Process.GetCurrentProcess();

            // The first three processes in the list.
            // We do not take other processes since they may differ.
            string[] expectedProcessNames = { currentProcess.ProcessName, "dotnet", "dotnet" };

            // Since VS has a different process list, we have to handle it separately.
            string[] vsExpectedProcessNames = { currentProcess.ProcessName, "vstest.console", "ServiceHub.TestWindowStoreHost" };

            // Act.
            (List<Process> processes, Dictionary<string, string> telemetryErrors) = 
                WindowsProcessUtil.GetProcessList(currentProcess, useInteropToFindParentProcess);

            string[] actualProcessNames = processes.Take(expectedProcessNames.Length)
                    .Select(process => process.ProcessName)
                    .ToArray();

            // Assert.
            if (actualProcessNames[1] == "vstest.console")
            {
                Assert.Equal(vsExpectedProcessNames, actualProcessNames);
            }
            else
            {
                Assert.Equal(expectedProcessNames, actualProcessNames);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void Test_GetParentProcessId_ViaInterop()
        {
            using TestHostContext hc = new TestHostContext(this);
            Tracing trace = hc.GetTrace();

            // Arrange: This test is based on the current process.
            Process currentProcess = Process.GetCurrentProcess();

            // Act.
            int? parentProcessId = WindowsProcessUtil.GetParentProcessId(currentProcess.Handle);

            // Assert.
            Assert.NotNull(parentProcessId);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void Test_GetParentProcess_ViaInterop()
        {
            using TestHostContext hc = new TestHostContext(this);
            Tracing trace = hc.GetTrace();

            // Arrange: This test is based on the current process.
            Process currentProcess = Process.GetCurrentProcess();

            // Act.
            (Process parentProcess, Dictionary<string, string> telemetryErrors) = WindowsProcessUtil.GetParentProcess(currentProcess);

            // Assert.
            Assert.NotNull(parentProcess);
            Assert.Equal(0, telemetryErrors.Count);
        }
    }
}
