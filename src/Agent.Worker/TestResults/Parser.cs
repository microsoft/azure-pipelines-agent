using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public class Parser
    {
        private IExecutionContext _executionContext;

        public Parser(IExecutionContext context){
            _executionContext = context;
        }

        public TestDataProvider ParseTestResultFiles(string testRunner, TestRunContext testRunContext, List<string> testResultsFiles)
        {
            if (string.IsNullOrEmpty(testRunner))
            {
                _executionContext.Warning("Test runner name is null or empty");
                return null;
            }
            // Create test result parser object based on the test Runner provided
            var testResultParser = GetTestResultParser(testRunner);
            if (testResultParser == null)
            {
                return null;
            }

            // Parse with the corresponding testResultParser object
            return ParseFiles(testRunContext, testResultsFiles, testResultParser);
        }

        private ITestResultParser GetTestResultParser(string testRunner)
        {
            var traceListener = new CommandTraceListener(_executionContext);
            if (testRunner.Equals("VSTest", StringComparison.OrdinalIgnoreCase))
            {
                return new TrxResultParser(traceListener);
            }
            else if (testRunner.Equals("NUnit", StringComparison.OrdinalIgnoreCase))
            {
                return new NUnitResultParser(traceListener);
            }
            else if (testRunner.Equals("XUnit", StringComparison.OrdinalIgnoreCase))
            {
                return new XUnitResultParser(traceListener);
            }
            else if (testRunner.Equals("JUnit", StringComparison.OrdinalIgnoreCase))
            {
                return new JUnitResultParser(traceListener);
            }
            else if (testRunner.Equals("CTest", StringComparison.OrdinalIgnoreCase))
            {
                return new CTestResultParser(traceListener);
            }
            // Return null if testRunner value is not in supported test runners.
            _executionContext.Warning("Invalid format: testRunner");
            return null;
        }

        private TestDataProvider ParseFiles(TestRunContext testRunContext, List<string> testResultsFiles, ITestResultParser testResultParser)
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
                _executionContext.Write("Failed to parse result files: ", ex.ToString());
            }
            return testDataProvider;
        }
    }
}
