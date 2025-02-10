// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public interface IParser : IExtension
    {
        string Name { get; }

        TestDataProvider ParseTestResultFiles(IExecutionContext executionContext, TestRunContext testRunContext, List<string> testResultsFiles);
    }

    public abstract class Parser : AgentService
    {
        public Type ExtensionType => typeof(IParser);

        public abstract string Name { get; }

        protected abstract ITestResultParser GetTestResultParser(IExecutionContext executionContext);

        public TestDataProvider ParseTestResultFiles(IExecutionContext executionContext, TestRunContext testRunContext, List<string> testResultsFiles)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            if (string.IsNullOrEmpty(Name))
            {
                executionContext.Warning("Test runner name is null or empty");
                return null;
            }
            // Create test result parser object based on the test Runner provided
            var testResultParser = GetTestResultParser(executionContext);
            if (testResultParser == null)
            {
                return null;
            }

            // Parse with the corresponding testResultParser object
            return ParseFiles(executionContext, testRunContext, testResultsFiles, testResultParser);
        }

        private TestDataProvider ParseFiles(IExecutionContext executionContext, TestRunContext testRunContext, List<string> testResultsFiles, ITestResultParser testResultParser)
        {
            if (testResultParser == null)
            {
                return null;
            }

            TestDataProvider testDataProvider = null;
            try
            {
                // Parse test results files
                testDataProvider = testResultParser.ParseTestResultFiles(testRunContext, testResultsFiles);
            }
            catch (Exception ex)
            {
                executionContext.Write("Failed to parse result files: ", ex.ToString());
            }
            return testDataProvider;
        }
    }

    public class JUnitParser : Parser, IParser
    {
        public override string Name => "JUnit";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            var featureFlagService = executionContext.GetHostContext().GetService<IFeatureFlagService>();
            bool enableCustomTestFields = featureFlagService.GetFeatureFlagState(TestResultsConstants.CustomTestFieldsInPTRInputFilesEnabled, TestResultsConstants.TCMServiceInstanceGuid);
            return new JUnitResultParser(traceListener, false, enableCustomTestFields);
        }
    }

    public class XUnitParser : Parser, IParser
    {
        public override string Name => "XUnit";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            var featureFlagService = executionContext.GetHostContext().GetService<IFeatureFlagService>();
            bool enableCustomTestFields = featureFlagService.GetFeatureFlagState(TestResultsConstants.CustomTestFieldsInPTRInputFilesEnabled, TestResultsConstants.TCMServiceInstanceGuid);
            return new XUnitResultParser(traceListener, setNameAsDisplayName: false, isTestCaseParallelReportingEnabled: false, enableCustomTestFields);
        }

    }

    public class TrxParser : Parser, IParser
    {
        public override string Name => "VSTest";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            var featureFlagService = executionContext.GetHostContext().GetService<IFeatureFlagService>();
            var enableXUnitHeirarchicalParsing = featureFlagService.GetFeatureFlagState(TestResultsConstants.EnableXUnitHeirarchicalParsing, TestResultsConstants.TFSServiceInstanceGuid);
            bool enableCustomTestFields = featureFlagService.GetFeatureFlagState(TestResultsConstants.CustomTestFieldsInPTRInputFilesEnabled, TestResultsConstants.TCMServiceInstanceGuid);
            return new TrxResultParser(traceListener, enableXUnitHeirarchicalParsing, enableCustomTestFields);
        }

    }

    public class NUnitParser : Parser, IParser
    {
        public override string Name => "NUnit";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            var featureFlagService = executionContext.GetHostContext().GetService<IFeatureFlagService>();
            bool enableCustomTestFields = featureFlagService.GetFeatureFlagState(TestResultsConstants.CustomTestFieldsInPTRInputFilesEnabled, TestResultsConstants.TCMServiceInstanceGuid);
            return new NUnitResultParser(traceListener, isTestCaseParallelReportingEnabled: false, enableCustomTestFields);
        }

    }

    public class CTestParser : Parser, IParser
    {
        public override string Name => "CTest";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new CTestResultParser(traceListener);
        }

    }

    public class ContainerStructureTestParser : Parser, IParser
    {
        public override string Name => "ContainerStructure";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new ContainerStructureTestResultParser(traceListener);
        }
    }
}