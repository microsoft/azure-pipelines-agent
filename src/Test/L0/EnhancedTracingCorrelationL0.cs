// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable CA2000 // Dispose objects before losing scope - test files manage disposal appropriately

using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Agent.Sdk.SecretMasking;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Mock execution context for testing correlation without full ExecutionContext initialization
    /// </summary>
    internal class MockCorrelationContext : ICorrelationContext
    {
        public string StepId { get; set; }
        public string TaskId { get; set; }

        public string BuildCorrelationId()
        {
            var parts = new System.Collections.Generic.List<string>();
            
            if (!string.IsNullOrEmpty(StepId))
            {
                parts.Add($"STEP-{StepId}");
            }
            
            if (!string.IsNullOrEmpty(TaskId))
            {
                parts.Add($"TASK-{TaskId}");
            }
            
            return parts.Count > 0 ? string.Join("|", parts) : string.Empty;
        }
    }

    /// <summary>
    /// Tests for EnhancedTracing with correlation context integration
    /// Verifies that correlation IDs appear correctly in log output
    /// </summary>
    public sealed class EnhancedTracingCorrelationL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_WithCorrelation_IncludesCorrelationInLogs()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_corr_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            // Create a mock execution context for correlation
            var mockEc = new MockCorrelationContext { StepId = "test-step-123" };
            hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act
                trace.Info("Test message with correlation");
                
                // Dispose in proper order
                trace.Dispose();
                listener.Dispose();
                masker.Dispose();
                
                // Wait for file handles to be released
                Task.Delay(200).Wait();
                
                // Assert
                Assert.True(File.Exists(logPath), "Log file should exist");
                var logContent = File.ReadAllText(logPath);
                
                Assert.Contains("Test message with correlation", logContent);
                Assert.Contains("[STEP-test-step-123]", logContent);
            }
            finally
            {
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
        [Trait("Category", "Common")]
        public void EnhancedTracing_WithStepAndTask_IncludesBothInLogs()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_both_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            // Create a mock execution context for correlation with both step and task
            var mockEc = new MockCorrelationContext { StepId = "step-abc", TaskId = "task-xyz" };
            hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act
                trace.Info("Message with both step and task");
                trace.Dispose();
                listener.Dispose();
                masker.Dispose();
                
                Task.Delay(200).Wait();
                
                // Assert
                var logContent = File.ReadAllText(logPath);
                Assert.Contains("Message with both step and task", logContent);
                Assert.Contains("STEP-step-abc", logContent);
                Assert.Contains("TASK-task-xyz", logContent);
            }
            finally
            {
                Task.Delay(100).Wait();
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_WithoutCorrelation_NoCorrelationIdInLogs()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_nocorr_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act - No execution context set
                trace.Info("Message without correlation");
                trace.Dispose();
                listener.Dispose();
                masker.Dispose();
                
                Task.Delay(200).Wait();
                
                // Assert
                var logContent = File.ReadAllText(logPath);
                Assert.Contains("Message without correlation", logContent);
                Assert.DoesNotContain("[STEP-", logContent);
                Assert.DoesNotContain("[TASK-", logContent);
            }
            finally
            {
                Task.Delay(100).Wait();
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_DifferentLogLevels_AllIncludeCorrelation()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_levels_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            // Create a mock execution context for correlation
            var mockEc = new MockCorrelationContext { StepId = "level-test" };
            hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act - Test different log levels
                trace.Info("Info message");
                trace.Warning("Warning message");
                trace.Error("Error message");
                trace.Verbose("Verbose message");
                
                trace.Dispose();
                listener.Dispose();
                
                masker.Dispose();
                
                Task.Delay(200).Wait();
                
                // Assert
                var logContent = File.ReadAllText(logPath);
                
                // All log levels should include correlation
                var expectedMessages = new[] { "Info message", "Warning message", "Error message", "Verbose message" };
                foreach (var msg in expectedMessages)
                {
                    Assert.Contains(msg, logContent);
                    // Verify correlation appears near the message (implementation dependent)
                }
                
                Assert.Contains("[STEP-level-test]", logContent);
            }
            finally
            {
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_WithException_IncludesCorrelation()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_exception_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            // Create a mock execution context for correlation
            var mockEc = new MockCorrelationContext { StepId = "exception-test" };
            hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act
                var exception = new InvalidOperationException("Test exception");
                trace.Error(exception);
                
                trace.Dispose();
                listener.Dispose();
                
                masker.Dispose();
                
                Task.Delay(200).Wait();
                
                // Assert
                var logContent = File.ReadAllText(logPath);
                Assert.Contains("Test exception", logContent);
                Assert.Contains("[STEP-exception-test]", logContent);
            }
            finally
            {
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_CorrelationChanges_ReflectsInSubsequentLogs()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_change_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act - Log with first correlation
                var mockEc1 = new MockCorrelationContext { StepId = "first-step" };
                hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc1);
                trace.Info("Message from first step");
                
                // Change correlation
                var mockEc2 = new MockCorrelationContext { StepId = "second-step" };
                hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc2);
                trace.Info("Message from second step");
                
                trace.Dispose();
                listener.Dispose();
                
                masker.Dispose();
                
                Task.Delay(200).Wait();
                
                // Assert
                var logContent = File.ReadAllText(logPath);
                Assert.Contains("Message from first step", logContent);
                Assert.Contains("Message from second step", logContent);
                Assert.Contains("[STEP-first-step]", logContent);
                Assert.Contains("[STEP-second-step]", logContent);
            }
            finally
            {
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_WithDurationTracking_IncludesCorrelation()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_duration_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            using var hc = new TestHostContext(this);
            
            // Create a mock execution context for correlation
            var mockEc = new MockCorrelationContext { StepId = "duration-test" };
            hc.CorrelationContextManager.SetCurrentExecutionContext(mockEc);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            var trace = new EnhancedTracing("TestTrace", masker, hc.CorrelationContextManager, sourceSwitch, listener);
            
            try
            {
                // Act - Use duration tracking
                using (trace.EnteringWithDuration("TestMethod"))
                {
                    Task.Delay(10).Wait();
                }
                
                trace.Dispose();
                listener.Dispose();
                
                masker.Dispose();
                
                Task.Delay(200).Wait();
                
                // Assert
                var logContent = File.ReadAllText(logPath);
                Assert.Contains("Entering TestMethod", logContent);
                Assert.Contains("Leaving TestMethod", logContent);
                Assert.Contains("[STEP-duration-test]", logContent);
            }
            finally
            {
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EnhancedTracing_NullCorrelationManager_ThrowsArgumentNullException()
        {
            // Arrange
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_null_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
            
            using var ossMasker = new OssSecretMasker();
            var masker = LoggedSecretMasker.Create(ossMasker);
            
            var sourceSwitch = new SourceSwitch("TestSwitch", "Verbose");
            
            try
            {
                // Act & Assert
                var exception = Assert.Throws<ArgumentNullException>(() =>
                {
                    new EnhancedTracing("TestTrace", masker, null, sourceSwitch, listener);
                });
                
                Assert.Equal("correlationContextManager", exception.ParamName);
            }
            finally
            {
                listener.Dispose();
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch (IOException) { }
                }
            }
        }
    }
}
