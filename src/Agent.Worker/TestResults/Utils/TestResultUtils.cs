// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk.Knob;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    internal static class TestResultUtils
    {
        public static void StoreTestRunSummaryInEnvVar(IExecutionContext executionContext, TestRunSummary testRunSummary, string testRunner, string name, string description = "")
        {
            if (AgentKnobs.DisableTestsMetadata.GetValue(executionContext).AsBoolean())
            {
                return;
            }

            try
            {
                string metadata = GetEvidenceStoreMetadata(executionContext, testRunSummary, testRunner, name, description);
                string taskVariableName = "METADATA_" + Guid.NewGuid().ToString();

                // This variable will be read by the PublishPipelineMetadatTask and publish to Evidence store.
                executionContext.SetVariable(taskVariableName, metadata);
                executionContext.Debug($"Setting task variable {taskVariableName}: {metadata} ");
            }
            catch (Exception ex)
            {
                executionContext.Debug($"Unable to set the METADATA_* env variable, error details: {ex}");
            }
        }

        public static TestCaseResultData CloneTestCaseResultData(TestCaseResultData original)
        {
            return new TestCaseResultData
            {
                Id = original.Id,
                Comment = original.Comment,
                Configuration = original.Configuration,
                Project = original.Project,
                StartedDate = original.StartedDate,
                CompletedDate = original.CompletedDate,
                DurationInMs = original.DurationInMs,
                Outcome = original.Outcome,
                Revision = original.Revision,
                State = original.State,
                TestCase = original.TestCase,
                TestPoint = original.TestPoint,
                TestRun = original.TestRun,
                ResolutionStateId = original.ResolutionStateId,
                ResolutionState = original.ResolutionState,
                LastUpdatedDate = original.LastUpdatedDate,
                Priority = original.Priority,
                ComputerName = original.ComputerName,
                ResetCount = original.ResetCount,
                Build = original.Build,
                Release = original.Release,
                ErrorMessage = original.ErrorMessage,
                CreatedDate = original.CreatedDate,
                IterationDetails = original.IterationDetails?.ToList(),
                AssociatedBugs = original.AssociatedBugs?.ToList(),
                Url = original.Url,
                FailureType = original.FailureType,
                AutomatedTestName = original.AutomatedTestName,
                AutomatedTestStorage = original.AutomatedTestStorage,
                AutomatedTestType = original.AutomatedTestType,
                AutomatedTestTypeId = original.AutomatedTestTypeId,
                AutomatedTestId = original.AutomatedTestId,
                Area = original.Area,
                TestCaseTitle = original.TestCaseTitle,
                StackTrace = original.StackTrace,
                CustomFields = original.CustomFields?.ToList(),
                BuildReference = original.BuildReference,
                ReleaseReference = original.ReleaseReference,
                TestPlan = original.TestPlan,
                TestSuite = original.TestSuite,
                TestCaseReferenceId = original.TestCaseReferenceId,
                Owner = original.Owner,
                RunBy = original.RunBy,
                LastUpdatedBy = original.LastUpdatedBy,
                ResultGroupType = original.ResultGroupType,
                TestCaseRevision = original.TestCaseRevision,
                TestCaseSubResultData = original.TestCaseSubResultData?.ToList(),
                AttachmentData = original.AttachmentData
            };
        }

        private static string GetEvidenceStoreMetadata(IExecutionContext executionContext, TestRunSummary testRunSummary, string testRunner, string name, string description)
        {
            string evidenceStoreMetadataString = string.Empty;
            try
            {
                // Need these settings for converting the property name to camelCase, that's what honored in the tasks.
                var camelCaseJsonSerializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };

                TestAttestation testAttestation = new TestAttestation(name, testRunner, testRunSummary);
                TestMetadata testMetadata = new TestMetadata()
                {
                    Description = description,
                    HumanReadableName = "Test Results from Publish Test Results utility"
                };

                string pipelinesUrl = GetPipelinesUrl(executionContext);
                if (!string.IsNullOrEmpty(pipelinesUrl))
                {
                    var relatedUrls = new[] { new RelatedUrl() { Label = "pipeline-url", Url = pipelinesUrl } };
                    testMetadata.RelatedUrls = relatedUrls;
                    testAttestation.RelatedUrls = relatedUrls;
                }

                testMetadata.SerializedPayload = JsonConvert.SerializeObject(testAttestation, camelCaseJsonSerializerSettings);

                EvidenceStoreMetadata evidenceStoreMetadata = new EvidenceStoreMetadata()
                {
                    Name = Guid.NewGuid().ToString(),
                    ResourceUris = GetResourceUris(executionContext),
                    Metadata = testMetadata
                };

                evidenceStoreMetadataString = JsonConvert.SerializeObject(evidenceStoreMetadata, camelCaseJsonSerializerSettings);
            }
            catch (Exception ex)
            {
                executionContext.Debug($"Unable to construct evidence store metadata, error details: {ex}");
            }

            return evidenceStoreMetadataString;
        }

        private static string[] GetResourceUris(IExecutionContext executionContext)
        {
            string[] resourceUris = { };
            try
            {
                var resourceUrisEnvVar = executionContext.GetVariableValueOrDefault("RESOURCE_URIS");
                executionContext.Debug("RESOURCE_URIS:" + resourceUrisEnvVar);

                if (!string.IsNullOrEmpty(resourceUrisEnvVar))
                {
                    resourceUris = resourceUrisEnvVar.Split(',');
                }
            }
            catch (Exception ex)
            {
                executionContext.Debug($"RESOURCE_URIS is not set or unable to get the variable, error details: {ex}");
            }

            return resourceUris;
        }

        private static string GetPipelinesUrl(IExecutionContext executionContext)
        {
            try
            {
                string hostType = executionContext.Variables.System_HostType.ToString();
                if (string.IsNullOrEmpty(hostType))
                {
                    return string.Empty;
                }

                bool isBuild = string.Equals(hostType, "build", StringComparison.OrdinalIgnoreCase);
                string pipeLineId = isBuild ? executionContext.Variables.Build_BuildId.Value.ToString() : executionContext.Variables.Release_ReleaseId;
                if (string.IsNullOrEmpty(pipeLineId))
                {
                    return string.Empty;
                }

                string baseUri = executionContext.Variables.System_TFCollectionUrl;
                string project = executionContext.Variables.System_TeamProject;

                if (string.IsNullOrEmpty(baseUri) || string.IsNullOrEmpty(project))
                {
                    return string.Empty;
                }

                string pipelineUri;
                if (isBuild)
                {
                    pipelineUri = $"{baseUri.TrimEnd('/')}/{project}/_build/results?buildId={pipeLineId}";
                }
                else
                {
                    pipelineUri = $"{baseUri.TrimEnd('/')}/{project}/_releaseProgress?releaseId={pipeLineId}";
                }
                return pipelineUri;
            }
            catch (Exception ex)
            {
                executionContext.Debug($"Unable to get pipelines url, error details: {ex}");
            }

            return string.Empty;
        }
    }

    internal class TestRunSummary
    {
        public TestRunSummary()
        {
            Total = 0;
            Failed = 0;
            Passed = 0;
            Skipped = 0;
        }

        public int Total;
        public int Failed;
        public int Passed;
        public int Skipped;
    }

    internal class TestAttestation
    {
        public string TestId;
        public string TestTool;
        public TestRunSummary TestResultAttestation;
        public double TestDurationSeconds;
        public string TestPassPercentage;
        public RelatedUrl[] RelatedUrls;

        public TestAttestation(string testId, string testTool, TestRunSummary testRunSummary)
        {
            this.TestId = testId;
            this.TestTool = testTool;
            this.TestResultAttestation = testRunSummary;
            this.TestPassPercentage = (testRunSummary.Total > 0 && testRunSummary.Total - testRunSummary.Skipped > 0 ? ((double)testRunSummary.Passed / (testRunSummary.Total - testRunSummary.Skipped)) * 100 : 0).ToString();
            // Will populate this in separate PR. As it required change in logic at client side.
            this.TestDurationSeconds = 0.0;
        }
    }

    internal class RelatedUrl
    {
        public string Url;
        public string Label;
    }

    internal class TestMetadata
    {
        public string Description;
        public RelatedUrl[] RelatedUrls;
        public string HumanReadableName;
        public string SerializedPayload;
    }

    internal class EvidenceStoreMetadata
    {
        public string Name;
        public string[] ResourceUris;
        public TestMetadata Metadata;
    }
}