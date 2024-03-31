// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    public class TestResultsConstants
    {
        public static readonly Guid TFSServiceInstanceGuid = new Guid("00025394-6065-48CA-87D9-7F5672854EF7");

        public static readonly Guid TCMServiceInstanceGuid = new Guid("00000054-0000-8888-8000-000000000000");

        public const string UsePublishTestResultsLibFeatureFlag = "TestManagement.Server.UsePublishTestResultsLibInAgent";

        public const string CalculateTestRunSummaryFeatureFlag = "TestManagement.PTR.CalculateTestRunSummary";

        public const string UseStatAPIFeatureFlag = "TestManagement.PublishTestResultsTask.UseStatsAPI";

        public static readonly string UseNewRunSummaryAPIFeatureFlag = "TestManagement.Server.UseNewRunSummaryAPIForPublishTestResultsTask";

        public const string EnableFlakyCheckInAgentFeatureFlag = "TestManagement.Agent.PTR.EnableFlakyCheck";

        public static readonly string EnableXUnitHeirarchicalParsing = "TestManagement.PublishTestResultsTask.EnableXUnitHeirarchicalParsing";

        public static readonly string JUnitTestCaseAttachmentsEnabled = "TestManagement.Server.JUnitTestCaseAttachmentsEnabled";

        public static readonly string EnableAzureTestPlanTaskFeatureFlag = "TestManagement.Server.EnableAzureTestPlanTaskFlow";
    }
}