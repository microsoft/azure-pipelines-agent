// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Unit tests for ICorrelationContextManager and CorrelationContextManager
    /// Tests the core correlation context functionality at the service layer
    /// </summary>
    public sealed class CorrelationContextManagerL0
    {
        /// <summary>
        /// Mock execution context for correlation testing.
        /// Provides BuildCorrelationId() method without requiring full ExecutionContext initialization.
        /// </summary>
        private class MockCorrelationContext : ICorrelationContext
        {
            public string StepId { get; set; }
            public string TaskId { get; set; }

            public string BuildCorrelationId()
            {
                if (string.IsNullOrEmpty(StepId) && string.IsNullOrEmpty(TaskId))
                {
                    return string.Empty;
                }

                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(StepId))
                {
                    parts.Add($"STEP-{StepId}");
                }
                if (!string.IsNullOrEmpty(TaskId))
                {
                    parts.Add($"TASK-{TaskId}");
                }
                return string.Join("|", parts);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_BasicLifecycle_WorksCorrectly()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            
            // Act - Should not throw
            manager.SetCurrentExecutionContext(null);
            var result1 = manager.BuildCorrelationId();
            
            manager.ClearCurrentExecutionContext();
            var result2 = manager.BuildCorrelationId();
            
            // Assert
            Assert.Equal(string.Empty, result1);
            Assert.Equal(string.Empty, result2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_WithValidExecutionContext_ReturnsCorrelationId()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext { StepId = "test-step-123" };
            
            // Act
            manager.SetCurrentExecutionContext(mockEc);
            var correlationId = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotEmpty(correlationId);
            Assert.StartsWith("STEP-", correlationId);
            Assert.Contains("test-step-123", correlationId);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_AfterClear_ReturnsEmptyString()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext { StepId = "test-step-456" };
            manager.SetCurrentExecutionContext(mockEc);
            
            // Act
            var beforeClear = manager.BuildCorrelationId();
            manager.ClearCurrentExecutionContext();
            var afterClear = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotEmpty(beforeClear);
            Assert.Equal(string.Empty, afterClear);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_WithNullContext_ReturnsEmpty()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            
            // Act
            manager.SetCurrentExecutionContext(null);
            var correlationId = manager.BuildCorrelationId();
            
            // Assert
            Assert.Equal(string.Empty, correlationId);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_AsyncLocalFlow_PreservesContextAcrossAsyncBoundaries()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext { StepId = "async-test-789" };
            manager.SetCurrentExecutionContext(mockEc);
            
            // Act & Assert - Use async/await to test AsyncLocal flow
            var task = Task.Run(async () =>
            {
                // Context should flow to async continuation
                await Task.Delay(10);
                return manager.BuildCorrelationId();
            });
            
            var result = task.Result;
            
            // Note: With MockCorrelationContext, the manager is captured by reference
            // So the correlation ID is available even in Task.Run
            // This tests the manager's ability to be accessed across async boundaries
            Assert.NotEmpty(result);
            Assert.Contains("async-test-789", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task CorrelationContextManager_AsyncLocalFlow_PreservesInSameContext()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext { StepId = "same-context-test" };
            manager.SetCurrentExecutionContext(mockEc);
            
            var before = manager.BuildCorrelationId();
            
            // Act - Continue in same async context
            await Task.Delay(10);
            var after = manager.BuildCorrelationId();
            
            // Assert - Should preserve context in same async flow
            Assert.Equal(before, after);
            Assert.NotEmpty(after);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_MultipleContexts_IsolatedProperly()
        {
            // Arrange
            using var manager1 = new CorrelationContextManager();
            using var manager2 = new CorrelationContextManager();
            
            var mockEc1 = new MockCorrelationContext { StepId = "context-1" };
            var mockEc2 = new MockCorrelationContext { StepId = "context-2" };
            
            // Act
            manager1.SetCurrentExecutionContext(mockEc1);
            manager2.SetCurrentExecutionContext(mockEc2);
            
            var result1 = manager1.BuildCorrelationId();
            var result2 = manager2.BuildCorrelationId();
            
            // Assert - Each manager maintains independent context
            Assert.Contains("context-1", result1);
            Assert.Contains("context-2", result2);
            Assert.NotEqual(result1, result2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_Dispose_ClearsContext()
        {
            // Arrange
            var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext { StepId = "dispose-test" };
            manager.SetCurrentExecutionContext(mockEc);
            
            var beforeDispose = manager.BuildCorrelationId();
            
            // Act
            manager.Dispose();
            var afterDispose = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotEmpty(beforeDispose);
            Assert.Equal(string.Empty, afterDispose);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_UpdateContext_ReflectsNewValue()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext { StepId = "initial-step" };
            manager.SetCurrentExecutionContext(mockEc);
            
            // Act - Update correlation through mock context
            var initial = manager.BuildCorrelationId();
            
            mockEc.StepId = "updated-step";
            var updated = manager.BuildCorrelationId();
            
            // Assert
            Assert.Contains("initial-step", initial);
            Assert.Contains("updated-step", updated);
            Assert.NotEqual(initial, updated);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_WithStepAndTask_ReturnsCombinedId()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            var mockEc = new MockCorrelationContext 
            { 
                StepId = "test-step",
                TaskId = "test-task"
            };
            manager.SetCurrentExecutionContext(mockEc);
            
            // Act
            var correlationId = manager.BuildCorrelationId();
            
            // Assert
            Assert.Contains("STEP-", correlationId);
            Assert.Contains("TASK-", correlationId);
            Assert.Contains("|", correlationId); // Should contain separator
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_ExceptionInBuildCorrelationId_Throws()
        {
            // Arrange
            using var manager = new CorrelationContextManager();
            
            // Create a context with a BuildCorrelationId method that throws
            var throwingContext = new ThrowingCorrelationContext();
            
            // Act & Assert - Exception should propagate (no more reflection try-catch)
            manager.SetCurrentExecutionContext(throwingContext);
            Assert.Throws<InvalidOperationException>(() => manager.BuildCorrelationId());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CorrelationContextManager_HostContextIntegration_WorksEndToEnd()
        {
            // Arrange & Act
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            var mockEc = new MockCorrelationContext { StepId = "integration-test" };
            manager.SetCurrentExecutionContext(mockEc);
            
            var correlationId = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotNull(manager);
            Assert.NotEmpty(correlationId);
            Assert.Contains("integration-test", correlationId);
        }

        // Helper class for testing error handling
        private class ThrowingCorrelationContext : ICorrelationContext
        {
            public string BuildCorrelationId()
            {
                throw new InvalidOperationException("Simulated error in BuildCorrelationId");
            }
        }
    }
}
