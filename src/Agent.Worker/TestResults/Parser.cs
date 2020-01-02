// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using System;
using System.Collections.Generic;

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

        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new JUnitResultParser(traceListener);
        }
    }

    public class XUnitParser : Parser, IParser
    {
        public override string Name => "XUnit";

        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new XUnitResultParser(traceListener);
        }

    }

    public class TrxParser : Parser, IParser
    {
        public override string Name => "VSTest";

        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new TrxResultParser(traceListener);
        }

    }

    public class NUnitParser : Parser, IParser
    {
        public override string Name => "NUnit";

        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new NUnitResultParser(traceListener);
        }

    }

    public class CTestParser : Parser, IParser
    {
        public override string Name => "CTest";

        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new CTestResultParser(traceListener);
        }

    }

    public class ContainerStructureTestParser: Parser, IParser
    {
        public override string Name => "ContainerStructure";

        protected override ITestResultParser GetTestResultParser(IExecutionContext executionContext)
        {
            var traceListener = new CommandTraceListener(executionContext);
            return new ContainerStructureTestResultParser(traceListener);
        }
    }
}
