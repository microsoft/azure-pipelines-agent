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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Clean up
                }
                disposed = true;
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_Node24GlibcError_ReturnsCorrectStatus()
        {
            // Reset static cache fields to ensure clean test
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                // Arrange
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc);

                // Setup Node24 process to return glibc error
                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: true);
                
                // Setup Node20 process to return success
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: false);

                // Act
                var glibcChecker = new GlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync();

                // Assert
                Assert.True(result.Node24HasGlibcError, "Node24 should have glibc error");
                Assert.False(result.Node20HasGlibcError, "Node20 should not have glibc error");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_BothVersionsSuccess_ReturnsCorrectStatus()
        {
            // Reset static cache fields to ensure clean test
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                // Arrange
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc);

                // Setup both processes to return success
                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: false);
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: false);

                // Act
                var glibcChecker = new GlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync();

                // Assert
                Assert.False(result.Node24HasGlibcError, "Node24 should not have glibc error");
                Assert.False(result.Node20HasGlibcError, "Node20 should not have glibc error");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_UseNode20InUnsupportedSystem_SkipsNode20Check()
        {
            // Reset static cache fields to ensure clean test
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                // Arrange
                var knobs = new Dictionary<string, string>
                {
                    ["AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"] = "true"
                };
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc, knobs);

                // Setup Node24 to return glibc error (should still be checked)
                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: true);
                
                // Don't setup Node20 process - it should not be called because knob skips it

                // Act
                var glibcChecker = new GlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync();

                // Assert
                Assert.True(result.Node24HasGlibcError, "Node24 should have glibc error (still checked)");
                Assert.False(result.Node20HasGlibcError, "Node20 should not have glibc error (skipped due to knob)");
                
                // Verify Node20 process was never called
                VerifyProcessNotCalled(processInvokerMock, "node20_1");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_UseNode24InUnsupportedSystem_SkipsNode24Check()
        {
            // Reset static cache fields to ensure clean test
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                // Arrange
                var knobs = new Dictionary<string, string>
                {
                    ["AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM"] = "true"
                };
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc, knobs);

                // Don't setup Node24 process - it should not be called because knob skips it
                
                // Setup Node20 to return glibc error (should still be checked)
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: true);

                // Act
                var glibcChecker = new GlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync();

                // Assert
                Assert.False(result.Node24HasGlibcError, "Node24 should not have glibc error (skipped due to knob)");
                Assert.True(result.Node20HasGlibcError, "Node20 should have glibc error (still checked)");
                
                // Verify Node24 process was never called
                VerifyProcessNotCalled(processInvokerMock, "node24");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_BothUnsupportedSystemKnobs_SkipsBothChecks()
        {
            // Reset static cache fields to ensure clean test
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                // Arrange
                var knobs = new Dictionary<string, string>
                {
                    ["AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"] = "true",
                    ["AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM"] = "true"
                };
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc, knobs);

                // Don't setup any processes - neither should be called

                // Act
                var glibcChecker = new GlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result = await glibcChecker.CheckGlibcCompatibilityAsync();

                // Assert
                Assert.False(result.Node24HasGlibcError, "Node24 should not have glibc error (skipped due to knob)");
                Assert.False(result.Node20HasGlibcError, "Node20 should not have glibc error (skipped due to knob)");
                
                // Verify no processes were called
                VerifyNoProcessesCalled(processInvokerMock);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "GlibcChecker")]
        public async Task GlibcCompatibilityInfoProvider_StaticCaching_WorksCorrectly()
        {
            // Reset static cache fields to ensure clean test
            ResetGlibcCompatibilityInfoProviderCache();
            
            using (var hc = new TestHostContext(this))
            {
                // Arrange
                var (processInvokerMock, executionContextMock) = SetupTestEnvironment(hc);

                // Setup processes to return success
                SetupNodeProcessInvocation(processInvokerMock, "node24", shouldHaveGlibcError: false);
                SetupNodeProcessInvocation(processInvokerMock, "node20_1", shouldHaveGlibcError: false);

                // Act - First call should execute processes
                var glibcChecker = new GlibcCompatibilityInfoProvider(executionContextMock.Object, hc);
                var result1 = await glibcChecker.CheckGlibcCompatibilityAsync();
                
                // Act - Second call should use cache
                var result2 = await glibcChecker.CheckGlibcCompatibilityAsync();

                // Assert
                Assert.False(result1.Node24HasGlibcError, "First call: Node24 should not have glibc error");
                Assert.False(result1.Node20HasGlibcError, "First call: Node20 should not have glibc error");
                Assert.False(result2.Node24HasGlibcError, "Second call: Node24 should not have glibc error");
                Assert.False(result2.Node20HasGlibcError, "Second call: Node20 should not have glibc error");
                
                // Verify processes were called only once (due to caching)
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
            
            // Enqueue multiple process invoker instances
            for (int i = 0; i < 10; i++)
            {
                hc.EnqueueInstance<IProcessInvoker>(processInvokerMock.Object);
            }

            // Setup execution context with knobs
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

            // Setup scoped environment for knob reading
            executionContextMock
                .Setup(x => x.GetScopedEnvironment())
                .Returns(new SystemEnvironment());

            // Setup knob reading method
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

            // Setup telemetry methods
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

        /// <summary>
        /// Sets up a node process invocation mock with the specified behavior.
        /// </summary>

        private void SetupNodeProcessInvocation(Mock<IProcessInvoker> processInvokerMock, string nodeFolder, bool shouldHaveGlibcError)
        {
            string nodeExePath = Path.Combine("externals", nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
            
            processInvokerMock.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(), // workingDirectory
                    It.Is<string>(fileName => fileName.Contains(nodeExePath)), // fileName
                    "-v", // arguments
                    It.IsAny<IDictionary<string, string>>(), // environment
                    false, // requireExitCodeZero
                    It.IsAny<Encoding>(), // outputEncoding
                    It.IsAny<CancellationToken>())) // cancellationToken
                .Callback<string, string, string, IDictionary<string, string>, bool, Encoding, CancellationToken>(
                    (wd, fn, args, env, reqZero, enc, ct) =>
                    {
                        // Simulate output based on whether glibc error should occur
                        if (shouldHaveGlibcError)
                        {
                            // Simulate glibc error output that WorkerUtilities.IsCommandResultGlibcError would detect
                            processInvokerMock.Raise(x => x.ErrorDataReceived += null, 
                                processInvokerMock.Object,
                                new ProcessDataReceivedEventArgs("node: /lib/x86_64-linux-gnu/libc.so.6: version `GLIBC_2.28' not found"));
                        }
                        else
                        {
                            // Simulate successful node version output
                            processInvokerMock.Raise(x => x.OutputDataReceived += null, 
                                processInvokerMock.Object,
                                new ProcessDataReceivedEventArgs($"v{(nodeFolder.Contains("24") ? "24" : "20")}.0.0"));
                        }
                    })
                .ReturnsAsync(shouldHaveGlibcError ? 1 : 0); // Return appropriate exit code
        }

        /// <summary>
        /// Resets the static cache fields in GlibcCompatibilityInfoProvider using reflection.
        /// </summary>
        private void ResetGlibcCompatibilityInfoProviderCache()
        {
            // Use reflection to reset the static cache fields in GlibcCompatibilityInfoProvider
            var glibcType = typeof(GlibcCompatibilityInfoProvider);
            var supportsNode20Field = glibcType.GetField("_supportsNode20", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var supportsNode24Field = glibcType.GetField("_supportsNode24", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            supportsNode20Field?.SetValue(null, null);
            supportsNode24Field?.SetValue(null, null);
        }

        #endregion
    }
}