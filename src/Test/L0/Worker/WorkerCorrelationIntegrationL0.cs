// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Moq;
using System.Collections.Generic;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using ExecutionContext = Microsoft.VisualStudio.Services.Agent.Worker.ExecutionContext;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    /// <summary>
    /// Integration tests for correlation context in Worker scenarios
    /// Tests end-to-end correlation tracking through job execution
    /// </summary>
    public sealed class WorkerCorrelationIntegrationL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_AutoRegistersWithCorrelationManager()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            
            // Act
            ec.Initialize(hc);
            var manager = hc.CorrelationContextManager;
            
            // The ExecutionContext constructor should auto-register
            var correlationId = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotNull(manager);
            // Initially empty until correlation is set
            Assert.Equal(string.Empty, correlationId);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_DisposeClearsCorrelation()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            string correlationBeforeDispose;
            
            // Act
            using (var ec = new ExecutionContext())
            {
                ec.Initialize(hc);
                ec.SetCorrelationStep("dispose-test");
                correlationBeforeDispose = manager.BuildCorrelationId();
            } // Dispose called here
            
            var correlationAfterDispose = manager.BuildCorrelationId();
            
            // Assert
            Assert.Contains("disposetest", correlationBeforeDispose);  // Hyphens removed by ShortenGuid
            Assert.Equal(string.Empty, correlationAfterDispose);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_NestedExecutionContexts_MaintainIndependentCorrelation()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            using var parentEc = new ExecutionContext();
            parentEc.Initialize(hc);
            parentEc.SetCorrelationStep("parent-step");
            
            var parentCorrelation = manager.BuildCorrelationId();
            
            // Act - Create child context
            using (var childEc = new ExecutionContext())
            {
                childEc.Initialize(hc);
                childEc.SetCorrelationStep("child-step");
                
                var childCorrelation = manager.BuildCorrelationId();
                
                // Assert - Child should override parent
                Assert.Contains("parentstep", parentCorrelation);  // Hyphens removed by ShortenGuid
                Assert.Contains("childstep", childCorrelation);     // Hyphens removed by ShortenGuid
                Assert.NotEqual(parentCorrelation, childCorrelation);
            }
            
            // After child disposal, we're back in parent context
            // But since child cleared the context, it should be empty
            var afterChildDispose = manager.BuildCorrelationId();
            Assert.Equal(string.Empty, afterChildDispose);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_CorrelationFlowsThroughStepExecution()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            var manager = hc.CorrelationContextManager;
            
            var pagingLogger = new Mock<IPagingLogger>();
            hc.EnqueueInstance(pagingLogger.Object);
            
            var jobServerQueue = new Mock<IJobServerQueue>();
            jobServerQueue.Setup(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.IsAny<TimelineRecord>()));
            hc.SetSingleton(jobServerQueue.Object);
            
            ec.Initialize(hc);
            
            // Create a minimal job request
            var jobRequest = CreateMinimalJobRequest();
            ec.InitializeJob(jobRequest, CancellationToken.None);
            
            // Act - Simulate step execution
            var stepId = Guid.NewGuid();
            ec.SetCorrelationStep(stepId.ToString());
            
            var correlationDuringStep = manager.BuildCorrelationId();
            
            // Assert
            Assert.NotEmpty(correlationDuringStep);
            Assert.Contains("STEP-", correlationDuringStep);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_TaskCorrelationAddsToStepCorrelation()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            var manager = hc.CorrelationContextManager;
            
            ec.Initialize(hc);
            
            var stepId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            
            // Act
            ec.SetCorrelationStep(stepId.ToString());
            var stepOnly = manager.BuildCorrelationId();
            
            ec.SetCorrelationTask(taskId.ToString());
            var stepAndTask = manager.BuildCorrelationId();
            
            // Assert
            Assert.Contains("STEP-", stepOnly);
            Assert.DoesNotContain("TASK-", stepOnly);
            
            Assert.Contains("STEP-", stepAndTask);
            Assert.Contains("TASK-", stepAndTask);
            Assert.Contains("|", stepAndTask); // Separator between step and task
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_ClearCorrelationRemovesFromManager()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            var manager = hc.CorrelationContextManager;
            
            ec.Initialize(hc);
            ec.SetCorrelationStep("test-step");
            ec.SetCorrelationTask("test-task");
            
            var withBoth = manager.BuildCorrelationId();
            
            // Act
            ec.ClearCorrelationTask();
            var withStepOnly = manager.BuildCorrelationId();
            
            ec.ClearCorrelationStep();
            var withNone = manager.BuildCorrelationId();
            
            // Assert
            Assert.Contains("STEP-", withBoth);
            Assert.Contains("TASK-", withBoth);
            
            Assert.Contains("STEP-", withStepOnly);
            Assert.DoesNotContain("TASK-", withStepOnly);
            
            Assert.Equal("TASK-testtask", withBoth.Split('|')[1]);  // Hyphens removed by ShortenGuid
            Assert.Equal(string.Empty, withNone);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task Worker_ExecutionContext_CorrelationFlowsAcrossAsyncOperations()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            var manager = hc.CorrelationContextManager;
            
            ec.Initialize(hc);
            ec.SetCorrelationStep("async-test");
            
            var beforeAsync = manager.BuildCorrelationId();
            
            // Act - Simulate async operation
            await Task.Delay(10);
            
            var afterAsync = manager.BuildCorrelationId();
            
            // Assert - Correlation should persist across await
            Assert.Equal(beforeAsync, afterAsync);
            Assert.Contains("asynctest", afterAsync);  // Hyphens removed by ShortenGuid
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_MultipleExecutionContexts_LastRegisteredWins()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            var manager = hc.CorrelationContextManager;
            
            using var ec1 = new ExecutionContext();
            using var ec2 = new ExecutionContext();
            
            ec1.Initialize(hc);
            ec2.Initialize(hc);
            
            ec1.SetCorrelationStep("context-1");
            
            // Act - ec2 registers after ec1
            ec2.SetCorrelationStep("context-2");
            
            var currentCorrelation = manager.BuildCorrelationId();
            
            // Assert - Most recent registration wins
            Assert.Contains("context2", currentCorrelation);  // Hyphens removed by ShortenGuid
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_GuidShorteningConsistency()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            var manager = hc.CorrelationContextManager;
            
            ec.Initialize(hc);
            
            var fullGuid = "60cf5508-70a7-5ba0-b727-5dd7f6763eb4";
            
            // Act
            ec.SetCorrelationStep(fullGuid);
            var correlation = manager.BuildCorrelationId();
            
            // Assert - Should be shortened consistently
            Assert.Equal("STEP-60cf550870a7", correlation);
            Assert.Equal(17, correlation.Length); // "STEP-" (5) + 12 chars
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Worker_ExecutionContext_CorrelationPersistsThroughJobLifecycle()
        {
            // Arrange
            using var hc = new TestHostContext(this);
            using var ec = new ExecutionContext();
            var manager = hc.CorrelationContextManager;
            
            var pagingLogger = new Mock<IPagingLogger>();
            hc.EnqueueInstance(pagingLogger.Object);
            
            var jobServerQueue = new Mock<IJobServerQueue>();
            jobServerQueue.Setup(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.IsAny<TimelineRecord>()));
            hc.SetSingleton(jobServerQueue.Object);
            
            ec.Initialize(hc);
            
            // Act - Simulate job lifecycle
            var jobRequest = CreateMinimalJobRequest();
            ec.InitializeJob(jobRequest, CancellationToken.None);
            
            var jobId = jobRequest.JobId;
            ec.SetCorrelationStep(jobId.ToString());
            
            var duringJob = manager.BuildCorrelationId();
            
            // Complete job
            ec.Complete();
            
            var afterComplete = manager.BuildCorrelationId();
            
            // Assert
            Assert.Contains("STEP-", duringJob);
            Assert.Contains("STEP-", afterComplete);
            Assert.Equal(duringJob, afterComplete); // Should persist until disposed
        }

        // Helper method to create minimal job request
        private Pipelines.AgentJobRequestMessage CreateMinimalJobRequest()
        {
            var plan = new TaskOrchestrationPlanReference();
            var timeline = new TimelineReference();
            var environment = new JobEnvironment();
            environment.SystemConnection = new ServiceEndpoint();
            var tasks = new List<TaskInstance>();
            var jobId = Guid.NewGuid();
            var jobName = "Test Job";
            
            var message = new AgentJobRequestMessage(plan, timeline, jobId, jobName, jobName, environment, tasks);
            return Pipelines.AgentJobRequestMessageUtil.Convert(message);
        }
    }
}
