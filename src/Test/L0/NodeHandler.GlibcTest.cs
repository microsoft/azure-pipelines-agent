// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies;
using Moq;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class NodeHandlerGlibcTest : IDisposable
    {
        private bool disposed = false;

        private class TestableGlibcCompatibilityInfoProvider : GlibcCompatibilityInfoProvider
        {
            public TestableGlibcCompatibilityInfoProvider(IExecutionContext executionContext, IHostContext hostContext)
                : base(executionContext, hostContext)
            {
            }

            protected override bool IsLinuxPlatform() => true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                disposed = true;
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_Node24GlibcError_ReturnsCorrectStatus()
        {
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc);

                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: true);
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: false);

                var glibcChecker = new TestableGlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);

                Assert.True(result.Node24HasGlibcError);
                Assert.False(result.Node20HasGlibcError);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_BothVersionsSuccess_ReturnsCorrectStatus()
        {
            ResetGlibcCompatibilityInfoProviderCache();
            using (var hc = new TestHostContext(this))
            {
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc);

                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: false);
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: false);

                var glibcChecker = new TestableGlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);

                Assert.False(result.Node24HasGlibcError);
                Assert.False(result.Node20HasGlibcError);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_UseNode20InUnsupportedSystem_SkipsNode20Check()
        {
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                var knobs = new Dictionary<string, string>
                {
                    ["AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"] = "true"
                };
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc, knobs);

                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: true);

                var glibcChecker = new TestableGlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);

                Assert.True(result.Node24HasGlibcError);
                Assert.False(result.Node20HasGlibcError);
                
                VerifyProcessNotCalled(processInvokerMock, "node20_1");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_UseNode24InUnsupportedSystem_SkipsNode24Check()
        {
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                var knobs = new Dictionary<string, string>
                {
                    ["AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM"] = "true"
                };
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc, knobs);

                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: true);

                var glibcChecker = new TestableGlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);

                Assert.False(result.Node24HasGlibcError);
                Assert.True(result.Node20HasGlibcError);
                
                VerifyProcessNotCalled(processInvokerMock, "node24");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_BothUnsupportedSystemKnobs_SkipsBothChecks()
        {
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                var knobs = new Dictionary<string, string>
                {
                    ["AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"] = "true",
                    ["AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM"] = "true"
                };
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc, knobs);

                var glibcChecker = new TestableGlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);

                Assert.False(result.Node24HasGlibcError);
                Assert.False(result.Node20HasGlibcError);
                VerifyNoProcessesCalled(processInvokerMock);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_StaticCaching_WorksCorrectly()
        {
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc);

                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: false);
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: false);

                var glibcChecker = new TestableGlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result1 = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);
                var result2 = await glibcChecker.CheckGlibcCompatibilityAsync(executionContextMock.Object);

                Assert.False(result1.Node24HasGlibcError);
                Assert.False(result1.Node20HasGlibcError);
                Assert.False(result2.Node24HasGlibcError);
                Assert.False(result2.Node20HasGlibcError);
                VerifyProcessCalledOnce(processInvokerMock, "node24");
                VerifyProcessCalledOnce(processInvokerMock, "node20_1");
            }
        }

        #region Helper Methods

        /// <summary>
        /// Sets up the common test environment with process invoker and execution context mocks.
        /// </summary>
        /// <param name="hc">Test host context</param>
        /// <param name="knobs">Optional knob settings to configure</param>
        /// <returns>Tuple of (processInvokerMock, executionContextMock)</returns>
        private (Mock<IProcessInvoker>, Mock<IExecutionContext>) SetupTestEnvironment(TestHostContext hc, Dictionary<string, string> knobs = null)
        {
            var processInvokerMock = new Mock<IProcessInvoker>();
            var executionContextMock = new Mock<IExecutionContext>();
            
            for (int i = 0; i < 10; i++)
            {
                hc.EnqueueInstance<IProcessInvoker>(processInvokerMock.Object);
            }

            var variables = new Dictionary<string, VariableValue>();
            if (knobs != null)
            {
                foreach (var knob in knobs)
                {
                    variables[knob.Key] = new VariableValue(knob.Value);
                }
            }
            
            List<string> warnings = new List<string>();
            executionContextMock
                .Setup(x => x.Variables)
                .Returns(new Variables(hc, copy: variables, warnings: out warnings));

            executionContextMock
                .Setup(x => x.GetScopedEnvironment())
                .Returns(new SystemEnvironment());

            executionContextMock
                .Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>()))
                .Returns((string variableName) =>
                {
                    if (variables.TryGetValue(variableName, out VariableValue value))
                    {
                        return value.Value;
                    }
                    return Environment.GetEnvironmentVariable(variableName);
                });

            executionContextMock.Setup(x => x.EmitHostNode20FallbackTelemetry(It.IsAny<bool>()));
            executionContextMock.Setup(x => x.EmitHostNode24FallbackTelemetry(It.IsAny<bool>()));

            return (processInvokerMock, executionContextMock);
        }

        /// <summary>
        /// Verifies that a specific node process was never called.
        /// </summary>
        private void VerifyProcessNotCalled(Mock<IProcessInvoker> processInvokerMock, string nodeFolder)
        {
            processInvokerMock.Verify(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.Is<string>(fileName => fileName.Contains(nodeFolder)),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<Encoding>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that no processes were called at all.
        /// </summary>
        private void VerifyNoProcessesCalled(Mock<IProcessInvoker> processInvokerMock)
        {
            processInvokerMock.Verify(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<Encoding>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that a specific node process was called exactly once.
        /// </summary>
        private void VerifyProcessCalledOnce(Mock<IProcessInvoker> processInvokerMock, string nodeFolder)
        {
            processInvokerMock.Verify(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.Is<string>(fileName => fileName.Contains(nodeFolder)),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<Encoding>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private void SetupNodeProcessInvocation(Mock<IProcessInvoker> processInvokerMock, string nodeFolder, bool shouldHaveGlibcError)
        {
            string nodeExePath = Path.Combine("externals", nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
            
            processInvokerMock.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.Is<string>(fileName => fileName.Contains(nodeExePath)),
                    "-v",
                    It.IsAny<IDictionary<string, string>>(),
                    false,
                    It.IsAny<Encoding>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, string, IDictionary<string, string>, bool, Encoding, CancellationToken>(
                    (wd, fn, args, env, reqZero, enc, ct) =>
                    {
                        if (shouldHaveGlibcError)
                        {
                            processInvokerMock.Raise(x => x.ErrorDataReceived += null, 
                                processInvokerMock.Object,
                                new ProcessDataReceivedEventArgs("node: /lib/x86_64-linux-gnu/libc.so.6: version `GLIBC_2.28' not found"));
                        }
                        else
                        {
                            processInvokerMock.Raise(x => x.OutputDataReceived += null, 
                                processInvokerMock.Object,
                                new ProcessDataReceivedEventArgs($"v{(nodeFolder.Contains("24") ? "24" : "20")}.0.0"));
                        }
                    })
                .ReturnsAsync(shouldHaveGlibcError ? 1 : 0);
        }

        private void ResetGlibcCompatibilityInfoProviderCache()
        {
            var glibcType = typeof(GlibcCompatibilityInfoProvider);
            var supportsNode20Field = glibcType.GetField("_supportsNode20", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var supportsNode24Field = glibcType.GetField("_supportsNode24", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            supportsNode20Field?.SetValue(null, null);
            supportsNode24Field?.SetValue(null, null);
        }

        #endregion
    }
}