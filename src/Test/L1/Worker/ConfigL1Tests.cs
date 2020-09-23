// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class ConfigL1Tests : L1TestBase
    {
        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TrackingConfigsShouldBeConsistentAcrossRuns()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                var message1 = LoadTemplateMessage();
                // second message is the same definition but a different job with a different repository checked out
                var message2 = LoadTemplateMessage(jobId: "642e8db6-0794-4b7b-8fd9-33ee9202a795", jobName: "__default2", jobDisplayName: "Job2", checkoutRepoAlias: "repo2");

                // Act
                var results1 = await RunWorker(message1);
                var trackingConfig1 = GetTrackingConfig(message1);
                AssertJobCompleted(1);
                Assert.Equal(TaskResult.Succeeded, results1.Result);

                // Act2
                var results2 = await RunWorker(message2);
                var trackingConfig2 = GetTrackingConfig(message2);
                AssertJobCompleted(2);
                Assert.Equal(TaskResult.Succeeded, results2.Result);

                // Assert
                Assert.Equal(trackingConfig1.BuildDirectory, trackingConfig2.BuildDirectory);
                Assert.Equal(trackingConfig1.HashKey, trackingConfig2.HashKey);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TrackingConfigsShouldBeConsistentAcrossMulticheckoutRuns()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                var message1 = LoadTemplateMessage(additionalRepos: 2);
                message1.Steps.Add(CreateCheckoutTask("Repo2"));
                message1.Steps.Add(CreateCheckoutTask("Repo2"));
                // second message is the same definition but a different job with a different order of the repos being checked out in a different order
                var message2 = LoadTemplateMessage(jobId: "642e8db6-0794-4b7b-8fd9-33ee9202a795", jobName: "__default2", jobDisplayName: "Job2", checkoutRepoAlias: "Repo3", additionalRepos: 2);
                message2.Steps.Add(CreateCheckoutTask("Repo2"));
                message2.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results1 = await RunWorker(message1);
                var trackingConfig1 = GetTrackingConfig(message1);
                AssertJobCompleted(1);
                Assert.Equal(TaskResult.Succeeded, results1.Result);

                // Act2
                var results2 = await RunWorker(message2);
                var trackingConfig2 = GetTrackingConfig(message2);
                AssertJobCompleted(2);
                Assert.Equal(TaskResult.Succeeded, results2.Result);

                // Assert
                Assert.Equal(trackingConfig1.BuildDirectory, trackingConfig2.BuildDirectory);
                Assert.Equal(trackingConfig1.HashKey, trackingConfig2.HashKey);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TrackingConfigsShouldBeConsistentAcrossRunsWithDifferentCheckouts()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                var message1 = LoadTemplateMessage(additionalRepos: 2);
                message1.Variables.Add("agent.useWorkspaceId", new VariableValue(Boolean.TrueString, false, true));

                // second message is the same definition but a different job with a different order of the repos being checked out in a different order
                var message2 = LoadTemplateMessage(jobId: "642e8db6-0794-4b7b-8fd9-33ee9202a795", jobName: "__default2", jobDisplayName: "Job2", checkoutRepoAlias: "Repo2", additionalRepos: 1);
                message2.Variables.Add("agent.useWorkspaceId", new VariableValue(Boolean.TrueString, false, true));

                // third message uses the same repos as the first
                var message3 = LoadTemplateMessage(additionalRepos: 2);
                message3.Variables.Add("agent.useWorkspaceId", new VariableValue(Boolean.TrueString, false, true));

                // Act
                var results1 = await RunWorker(message1);
                var trackingConfig1 = GetTrackingConfig(message1);
                AssertJobCompleted(1);
                Assert.Equal(TaskResult.Succeeded, results1.Result);

                // Act2
                var results2 = await RunWorker(message2);
                var trackingConfig2 = GetTrackingConfig(message2);
                AssertJobCompleted(2);
                Assert.Equal(TaskResult.Succeeded, results2.Result);

                // Act3
                var results3 = await RunWorker(message3);
                var trackingConfig3 = GetTrackingConfig(message3);
                AssertJobCompleted(3);
                Assert.Equal(TaskResult.Succeeded, results3.Result);

                // Assert - the first and third runs should be consistent
                Assert.NotEqual(trackingConfig1.BuildDirectory, trackingConfig2.BuildDirectory);
                Assert.Equal(trackingConfig1.BuildDirectory, trackingConfig3.BuildDirectory);
                Assert.Equal(trackingConfig1.HashKey, trackingConfig3.HashKey);
            }
            finally
            {
                TearDown();
            }
        }
    }
}
