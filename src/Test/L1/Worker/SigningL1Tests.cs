// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class SigningL1Tests : L1TestBase
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        // TODO: When NuGet works cross-platform, remove these traits. Also, package NuGet with the Agent.
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task SignatureVerification_PassesWhenAllTasksAreSigned(bool useFingerprintList, bool useTopLevelFingerprint)
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.SignatureVerification = new SignatureVerificationSettings()
                {
                    Mode = SignatureVerificationMode.Error
                };
                if (useFingerprintList)
                {
                    settings.SignatureVerification.Fingerprints = new List<string>() { _fingerprint };
                }
                else if (useTopLevelFingerprint)
                {
                    settings.Fingerprint = _fingerprint;
                }
                fakeConfigurationStore.UpdateSettings(settings);

                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(GetSignedTask());

                // Act
                var results = await RunWorker(message);

                // Assert
                FakeJobServer fakeJobServer = GetMockedService<FakeJobServer>();
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        // TODO: When NuGet works cross-platform, remove these traits. Also, package NuGet with the Agent.
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task SignatureVerification_FailsWhenTasksArentSigned(bool useFingerprintList, bool useTopLevelFingerprint)
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.SignatureVerification = new SignatureVerificationSettings()
                {
                    Mode = SignatureVerificationMode.Error
                };
                if (useFingerprintList)
                {
                    settings.SignatureVerification.Fingerprints = new List<string>() { _fingerprint };
                }
                else if (useTopLevelFingerprint)
                {
                    settings.Fingerprint = _fingerprint;
                }
                fakeConfigurationStore.UpdateSettings(settings);
                var message = LoadTemplateMessage();

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Failed, results.Result);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task SignatureVerification_MultipleFingerprints()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.SignatureVerification = new SignatureVerificationSettings()
                {
                    Mode = SignatureVerificationMode.Error,
                    Fingerprints = new List<string>() { "BAD", _fingerprint }
                };
                fakeConfigurationStore.UpdateSettings(settings);
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(GetSignedTask());

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SignatureVerification_Warning(bool writeToBlobstorageService)
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.SignatureVerification = new SignatureVerificationSettings()
                {
                    Mode = SignatureVerificationMode.Warning,
                    Fingerprints = new List<string>() { "BAD" }
                };
                fakeConfigurationStore.UpdateSettings(settings);
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(GetSignedTask());
                message.Variables.Add("agent.LogToBlobstorageService", writeToBlobstorageService.ToString());

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var log = GetTimelineLogLines(steps[1]);

                Assert.Equal(1, log.Where(x => x.Contains("##[warning]Task signature verification failed.")).Count());
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task SignatureVerification_Disabled()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.SignatureVerification = new SignatureVerificationSettings()
                {
                    Mode = SignatureVerificationMode.None,
                    Fingerprints = new List<string>() { "BAD" }
                };
                fakeConfigurationStore.UpdateSettings(settings);
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(GetSignedTask());

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
            }
            finally
            {
                TearDown();
            }
        }

        private static TaskStep GetSignedTask()
        {
            var step = new TaskStep
            {
                Reference = new TaskStepDefinitionReference
                {
                    Id = Guid.Parse("d9bafed4-0b18-4f58-0001-86655b4d2ce9"),
                    Name = "CmdLine",
                    Version = "2.212.0"
                },
                Name = "CmdLine",
                DisplayName = "Command line - SIGNED",
                Id = Guid.NewGuid()
            };

            // These inputs let the task itself succeed.
            step.Inputs.Add("script", "echo Hey!");

            return step;
        }

        private static string _fingerprint = "AA12DA22A49BCE7D5C1AE64CC1F3D892F150DA76140F210ABD2CBFFCA2C18A27";
    }
}
