// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    internal static class TestResultUtils
    {
        public static void StoreTestRunSummaryInEnvVar(IExecutionContext executionContext, TestRunSummary testRunSummary, string testRunner, string name, string description="")
        {
            try
            {
                String evidenceStoreMetadata = JsonConvert.SerializeObject(GetEvidenceStoreMetadata(executionContext, testRunSummary, testRunner, name, description));
                // This Environment variable will be read by the PublishPipelineMetadatTask and publish to Evidence store.
                String envVariableName = "METADATA_" + Guid.NewGuid().ToString();
                Environment.SetEnvironmentVariable("METADATA_" + Guid.NewGuid().ToString(), evidenceStoreMetadata);
                executionContext.Debug($"Setting env variable {envVariableName}: {evidenceStoreMetadata} ");
            }
            catch (Exception ex)
            {
                executionContext.Debug($"Unable to set the METADATA_* env variable, error details: {ex}");
            }
        }

        private static string GetEvidenceStoreMetadata(IExecutionContext executionContext, TestRunSummary testRunSummary, string testRunner, string name, string description)
        {
            string evidenceStoreMetadataString = string.Empty;
            try
            {
                TestAttestation testAttestation = new TestAttestation(name, testRunner, testRunSummary);
                TestMetadata testMetadata = new TestMetadata()
                {
                    Description = description,
                    HumanReadableName = "Test Results from Publish Test Results utility",
                    SerializedPayload = JsonConvert.SerializeObject(testAttestation)
                };
                EvidenceStoreMetadata evidenceStoreMetadata = new EvidenceStoreMetadata()
                {
                    Name = Guid.NewGuid().ToString(),
                    ResourceUris = GetResourceUris(executionContext),
                    Metadata = testMetadata

                };

                evidenceStoreMetadataString = JsonConvert.SerializeObject(evidenceStoreMetadata);
            }
            catch (Exception ex)
            {
                executionContext.Debug($"Unable to construct evidence store metadata, error details: {ex}");
            }

            return evidenceStoreMetadataString;
        }

        private static string[] GetResourceUris(IExecutionContext executionContext)
        {
            string[] resourceUris = {};
            try
            {
                var resourceUrisEnvVar = System.Environment.GetEnvironmentVariable("RESOURCE_URIS");
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
            this.TestPassPercentage = (testRunSummary.Total > 0 && testRunSummary.Total - testRunSummary.Skipped > 0 ? ((double)testRunSummary.Passed/(testRunSummary.Total-testRunSummary.Skipped)) * 100 : 0).ToString();
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