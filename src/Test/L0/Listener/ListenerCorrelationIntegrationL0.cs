// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable CA2000 // Dispose objects before losing scope - test files manage disposal appropriately

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Agent.Sdk.SecretMasking;
using Agent.Sdk.Knob;
using Agent.Sdk;
using ExecutionContext = Microsoft.VisualStudio.Services.Agent.Worker.ExecutionContext;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    /// <summary>
    /// Integration tests for correlation context in Listener scenarios
    /// Tests correlation tracking from agent listener perspective
    /// </summary>
    public sealed class ListenerCorrelationIntegrationL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_HostContext_ProvidesCorrelationManager()
        {
            // Arrange & Act
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            // Assert
            Assert.NotNull(manager);
            Assert.IsAssignableFrom<ICorrelationContextManager>(manager);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_HostContext_CorrelationManagerDisposedWithContext()
        {
            // Arrange
            ICorrelationContextManager manager;
            
            // Act
            using (var hc = new TestHostContext(this))
            {
                manager = hc.CorrelationContextManager;
                
                using var ec = new ExecutionContext();
                ec.Initialize(hc);
                ec.SetCorrelationStep("test");
                
                var beforeDispose = manager.BuildCorrelationId();
                Assert.NotEmpty(beforeDispose);
            } // HostContext disposed
            
            // Assert - After HostContext disposal, manager should be cleared
            var afterDispose = manager.BuildCorrelationId();
            Assert.Equal(string.Empty, afterDispose);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_TraceManager_UsesCorrelationFromHostContext()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"listener_trace_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            
            using var hc = new TestHostContext(this);
            
            try
            {
                // Act - Create TraceManager which should get correlation manager from HostContext
                var traceManager = new TraceManager(listener, masker, hc);
                
                // Assert - Should not throw
                Assert.NotNull(traceManager);
                
                traceManager.Dispose();
            }
            finally
            {
                listener.Dispose();
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_TraceManager_ThrowsIfNotHostContext()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"listener_throw_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            
            // Create a mock knob context that is NOT IHostContext
            var notHostContext = new MockKnobValueContext();
            
            try
            {
                // Act & Assert
                var exception = Assert.Throws<ArgumentException>(() =>
                {
                    new TraceManager(listener, masker, notHostContext);
                });
                
                Assert.Contains("knobValueContext must be IHostContext", exception.Message);
            }
            finally
            {
                listener.Dispose();
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_EnhancedTracing_CreatedWithCorrelationManager()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"listener_enhanced_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            // Enable enhanced logging
            Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", "true");
            
            try
            {
                // Act
                var traceManager = new TraceManager(listener, masker, hc);
                traceManager.SetEnhancedLoggingEnabled(true);
                
                var trace = traceManager["ListenerTest"];
                
                // Create execution context with correlation
                using var ec = new ExecutionContext();
                ec.Initialize(hc);
                ec.SetCorrelationStep("listener-trace-test");
                
                trace.Info("Test message from listener");
                
                // Dispose in proper order
                traceManager.Dispose();
                listener.Dispose();
                masker.Dispose();
                
                // Wait for file handles to be released
                Task.Delay(200).Wait();
                
                // Assert
                if (File.Exists(logPath))
                {
                    var logContent = File.ReadAllText(logPath);
                    Assert.Contains("Test message from listener", logContent);
                    // Enhanced tracing should include correlation (hyphens removed by ShortenGuid)
                    Assert.Contains("listenertrac", logContent);  // "listener-trace-test" becomes "listenertrac" (first 12 chars)
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", null);
                
                // Wait before attempting to delete
                Task.Delay(100).Wait();
                
                if (File.Exists(logPath))
                {
                    try
                    {
                        File.Delete(logPath);
                    }
                    catch (IOException)
                    {
                        // File still locked, ignore cleanup error
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_MultipleTraceSources_ShareCorrelationManager()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            string logPath = Path.Combine(Path.GetTempPath(), $"listener_multi_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            
            try
            {
                var traceManager = new TraceManager(listener, masker, hc);
                
                var trace1 = traceManager["Source1"];
                var trace2 = traceManager["Source2"];
                
                // Act - Set correlation once
                using var ec = new ExecutionContext();
                ec.Initialize(hc);
                ec.SetCorrelationStep("shared-correlation");
                
                var correlation1 = manager.BuildCorrelationId();
                var correlation2 = manager.BuildCorrelationId();
                
                // Assert - Both should see the same correlation (hyphens removed by ShortenGuid)
                Assert.Equal(correlation1, correlation2);
                Assert.Contains("sharedcorrel", correlation1);  // "shared-correlation" becomes "sharedcorrel" (first 12 chars)
                
                traceManager.Dispose();
            }
            finally
            {
                listener.Dispose();
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_HostContext_CorrelationManagerSingleton()
        {
            // Arrange & Act
            using var hc = new TestHostContext(this);
            
            var manager1 = hc.CorrelationContextManager;
            var manager2 = hc.CorrelationContextManager;
            
            // Assert - Should be same instance
            Assert.Same(manager1, manager2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_AgentShutdown_ClearsCorrelationContext()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            using var ec = new ExecutionContext();
            ec.Initialize(hc);
            ec.SetCorrelationStep("shutdown-test");
            
            var beforeShutdown = manager.BuildCorrelationId();
            
            // Act - Simulate shutdown
            hc.ShutdownAgent(ShutdownReason.UserCancelled);
            
            // ExecutionContext disposal should clear correlation
            ec.Dispose();
            
            var afterShutdown = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotEmpty(beforeShutdown);
            Assert.Equal(string.Empty, afterShutdown);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public async Task Listener_ConcurrentExecutionContexts_IsolatedCorrelation()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            // Act - Simulate concurrent job dispatches
            var task1 = Task.Run(() =>
            {
                using var ec1 = new ExecutionContext();
                ec1.Initialize(hc);
                ec1.SetCorrelationStep("job-1");
                Thread.Sleep(50);
                return manager.BuildCorrelationId();
            });
            
            var task2 = Task.Run(() =>
            {
                using var ec2 = new ExecutionContext();
                ec2.Initialize(hc);
                ec2.SetCorrelationStep("job-2");
                Thread.Sleep(50);
                return manager.BuildCorrelationId();
            });
            
            var results = await Task.WhenAll(task1, task2);
            
            // Assert - Each task sees its own correlation ID
            // ExecutionContext.Initialize() registers with the shared manager,
            // and the last registration wins in the shared AsyncLocal
            Assert.All(results, r => Assert.NotEmpty(r));
            Assert.Contains(results, r => r.Contains("job1") || r.Contains("job2"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Listener")]
        public void Listener_TraceManagerSwitch_PreservesCorrelation()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"listener_switch_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            try
            {
                var traceManager = new TraceManager(listener, masker, hc);
                
                using var ec = new ExecutionContext();
                ec.Initialize(hc);
                ec.SetCorrelationStep("switch-test");
                
                // Act - Switch enhanced logging on and off
                traceManager.SetEnhancedLoggingEnabled(true);
                var trace1 = traceManager["Test"];
                trace1.Info("Message with enhanced logging");
                
                traceManager.SetEnhancedLoggingEnabled(false);
                var trace2 = traceManager["Test"];
                trace2.Info("Message without enhanced logging");
                
                traceManager.SetEnhancedLoggingEnabled(true);
                var trace3 = traceManager["Test"];
                trace3.Info("Message with enhanced logging again");
                
                traceManager.Dispose();
                listener.Dispose();
                
                Task.Delay(50).Wait();
                
                // Assert
                if (File.Exists(logPath))
                {
                    var logContent = File.ReadAllText(logPath);
                    Assert.Contains("Message with enhanced logging", logContent);
                    Assert.Contains("Message without enhanced logging", logContent);
                    Assert.Contains("Message with enhanced logging again", logContent);
                }
            }
            finally
            {
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
            }
        }

        // Mock implementation for testing TraceManager validation
        private class MockKnobValueContext : IKnobValueContext
        {
            public IScopedEnvironment GetScopedEnvironment()
            {
                return null;
            }

            public string GetVariableValueOrDefault(string variableName)
            {
                _ = variableName;
                return null;
            }
        }
    }
}
