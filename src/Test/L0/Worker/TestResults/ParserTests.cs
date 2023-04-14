// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.TestResults
{
    public class ParserTests
    {
        private Mock<IExecutionContext> _ec;
        private Mock<IFeatureFlagService> _mockFeatureFlagService;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void PublishTrxResults()
        {
            SetupMocks();
            String trxContents = "<?xml version = \"1.0\" encoding = \"UTF-8\"?>" +
               "<TestRun id = \"ee3d8b3b-1ac9-4a7e-abfa-3d3ed2008613\" name = \"somerandomusername@SOMERANDOMCOMPUTERNAME 2015-03-20 16:53:32\" runUser = \"FAREAST\\somerandomusername\" xmlns =\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Times creation = \"2015-03-20T16:53:32.3309380+05:30\" queuing = \"2015-03-20T16:53:32.3319381+05:30\" start = \"2015-03-20T16:53:32.3349628+05:30\" finish = \"2015-03-20T16:53:32.9232329+05:30\" />" +
                 "<TestDefinitions>" +
                   "<UnitTest name = \"TestMethod2\" storage = \"c:/users/somerandomusername/source/repos/projectx/unittestproject4/unittestproject4/bin/debug/unittestproject4.dll\" priority = \"1\" id = \"f0d6b58f-dc08-9c0b-aab7-0a1411d4a346\"><Owners><Owner name = \"asdf2\" /></Owners><Execution id = \"48ec1e47-b9df-43b9-aef2-a2cc8742353d\" /><TestMethod codeBase = \"c:/users/somerandomusername/source/repos/projectx/unittestproject4/unittestproject4/bin/debug/unittestproject4.dll\" adapterTypeName = \"Microsoft.VisualStudio.TestTools.TestTypes.Unit.UnitTestAdapter\" className = \"UnitTestProject4.UnitTest1\" name = \"TestMethod2\" /></UnitTest>" +
                   "<WebTest name=\"PSD_Startseite\" storage=\"c:\\vsoagent\\a284d2cc\\vseqa1\\psd_startseite.webtest\" id=\"01da1a13-b160-4ee6-9d84-7a6dfe37b1d2\" persistedWebTest=\"7\"><TestCategory><TestCategoryItem TestCategory=\"PSD\" /></TestCategory><Execution id=\"eb421c16-4546-435a-9c24-0d2878ea76d4\" /></WebTest>" +
                   "<OrderedTest name=\"OrderedTest1\" storage=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\orderedtest1.orderedtest\" id=\"4eb63268-af79-48f1-b625-05ef09b0301a\"><Execution id=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\" /><TestLinks><TestLink id=\"fd846020-c6f8-3c49-3ed0-fbe1e1fd340b\" name=\"CodedUITestMethod1\" storage=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\codeduitestproject1.dll\" /><TestLink id=\"1c7ece84-d949-bed1-0a4c-dfad4f9c953e\" name=\"CodedUITestMethod2\" storage=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\codeduitestproject1.dll\" /></TestLinks></OrderedTest>" +
                   "<UnitTest name=\"CodedUITestMethod1\" storage=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\codeduitestproject1.dll\" id=\"fd846020-c6f8-3c49-3ed0-fbe1e1fd340b\"><Execution id=\"4f82d822-cd28-4bcc-b091-b08a66cf92e7\" parentId=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\" /><TestMethod codeBase=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\codeduitestproject1.dll\" adapterTypeName=\"executor://orderedtestadapter/v1\" className=\"CodedUITestProject1.CodedUITest1\" name=\"CodedUITestMethod1\" /></UnitTest>" +
                   "<UnitTest name=\"CodedUITestMethod2\" storage=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\codeduitestproject1.dll\" priority=\"1\" id=\"1c7ece84-d949-bed1-0a4c-dfad4f9c953e\"><Execution id=\"5918f7d4-4619-4869-b777-71628227c62a\" parentId=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\" /><TestMethod codeBase=\"c:\\users\\random\\source\\repos\\codeduitestproject1\\codeduitestproject1\\bin\\debug\\codeduitestproject1.dll\" adapterTypeName=\"executor://orderedtestadapter/v1\" className=\"CodedUITestProject1.CodedUITest1\" name=\"CodedUITestMethod2\" /></UnitTest>" +
                 "</TestDefinitions>" +

                 "<Results>" +
                   "<UnitTestResult executionId = \"48ec1e47-b9df-43b9-aef2-a2cc8742353d\" testId = \"f0d6b58f-dc08-9c0b-aab7-0a1411d4a346\" testName = \"TestMethod2\" computerName = \"SOMERANDOMCOMPUTERNAME\" duration = \"00:00:00.0834563\" startTime = \"2015-03-20T16:53:32.3099353+05:30\" endTime = \"2015-03-20T16:53:32.3939623+05:30\" testType = \"13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b\" outcome = \"Pending\" testListId = \"8c84fa94-04c1-424b-9868-57a2d4851a1d\" relativeResultsDirectory = \"48ec1e47-b9df-43b9-aef2-a2cc8742353d\" ><Output><StdOut>Show console log output.</StdOut><ErrorInfo><Message>Assert.Fail failed.</Message><StackTrace>at UnitTestProject4.UnitTest1.TestMethod2() in C:\\Users\\somerandomusername\\Source\\Repos\\Projectx\\UnitTestProject4\\UnitTestProject4\\UnitTest1.cs:line 21</StackTrace></ErrorInfo></Output>" +
                     "<ResultFiles><ResultFile path=\"DIGANR-DEV4\\x.txt\" /></ResultFiles>" +
                   "</UnitTestResult>" +
                   "<WebTestResult executionId=\"eb421c16-4546-435a-9c24-0d2878ea76d4\" testId=\"01da1a13-b160-4ee6-9d84-7a6dfe37b1d2\" testName=\"PSD_Startseite\" computerName=\"LAB-BUILDVNEXT\" duration=\"00:00:01.6887389\" startTime=\"2015-05-20T18:53:51.1063165+00:00\" endTime=\"2015-05-20T18:54:03.9160742+00:00\" testType=\"4e7599fa-5ecb-43e9-a887-cd63cf72d207\" outcome=\"Passed\" testListId=\"8c84fa94-04c1-424b-9868-57a2d4851a1d\" relativeResultsDirectory=\"eb421c16-4546-435a-9c24-0d2878ea76d4\"><Output><StdOut>Do not show console log output.</StdOut></Output>" +
                     "<ResultFiles>" +
                       "<ResultFile path=\"PSD_Startseite.webtestResult\" />" +
                     "</ResultFiles>" +
                     "<WebTestResultFilePath>LOCAL SERVICE_LAB-BUILDVNEXT 2015-05-20 18_53_41\\In\\eb421c16-4546-435a-9c24-0d2878ea76d4\\PSD_Startseite.webtestResult</WebTestResultFilePath>" +
                   "</WebTestResult>" +
                   "<TestResultAggregation executionId=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\" testId=\"4eb63268-af79-48f1-b625-05ef09b0301a\" testName=\"OrderedTest1\" computerName=\"random-DT\" duration=\"00:00:01.4031295\" startTime=\"2017-12-14T16:27:24.2216619+05:30\" endTime=\"2017-12-14T16:27:25.6423256+05:30\" testType=\"ec4800e8-40e5-4ab3-8510-b8bf29b1904d\" outcome=\"Passed\" testListId=\"8c84fa94-04c1-424b-9868-57a2d4851a1d\" relativeResultsDirectory=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\">" +
                     "<InnerResults>" +
                       "<UnitTestResult executionId=\"4f82d822-cd28-4bcc-b091-b08a66cf92e7\" parentExecutionId=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\" testId=\"fd846020-c6f8-3c49-3ed0-fbe1e1fd340b\" testName=\"01- CodedUITestMethod1 (OrderedTest1)\" computerName=\"random-DT\" duration=\"00:00:00.3658086\" startTime=\"2017-12-14T10:57:24.2386920+05:30\" endTime=\"2017-12-14T10:57:25.3440342+05:30\" testType=\"13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b\" outcome=\"Passed\" testListId=\"8c84fa94-04c1-424b-9868-57a2d4851a1d\" relativeResultsDirectory=\"4f82d822-cd28-4bcc-b091-b08a66cf92e7\" />" +
                       "<UnitTestResult executionId=\"5918f7d4-4619-4869-b777-71628227c62a\" parentExecutionId=\"20927d24-2eb4-473f-b5b2-f52667b88f6f\" testId=\"1c7ece84-d949-bed1-0a4c-dfad4f9c953e\" testName=\"02- CodedUITestMethod2 (OrderedTest1)\" computerName=\"random-DT\" duration=\"00:00:00.0448870\" startTime=\"2017-12-14T10:57:25.3480349+05:30\" endTime=\"2017-12-14T10:57:25.3950371+05:30\" testType=\"13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b\" outcome=\"Passed\" testListId=\"8c84fa94-04c1-424b-9868-57a2d4851a1d\" relativeResultsDirectory=\"5918f7d4-4619-4869-b777-71628227c62a\" />" +
                     "</InnerResults>" +
                   "</TestResultAggregation>" +
                 "</Results>" +

                 "<ResultSummary outcome=\"Failed\"><Counters total = \"3\" executed = \"3\" passed=\"2\" failed=\"1\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />" +
                 "</ResultSummary>" +
               "</TestRun>";
            string resultFile = TestUtil.WriteAllTextToTempFile(trxContents, "trx");
            TrxParser reader = new TrxParser();
            TestRunContext runContext = new TestRunContext();
            TestDataProvider runDataProvider = reader.ParseTestResultFiles(_ec.Object, runContext, new List<string> { resultFile });
            List<TestRunData> runData = runDataProvider.GetTestRunData();


            Assert.Equal(runData[0].TestResults.Count, 3);
            Assert.Equal(runData[0].TestResults[0].Outcome, "NotExecuted");
            Assert.Equal(runData[0].TestResults[0].TestCaseTitle, "TestMethod2");
            Assert.Equal(runData[0].TestResults[1].Outcome, "Passed");
            Assert.Equal(runData[0].TestResults[1].TestCaseTitle, "PSD_Startseite");
            Assert.Equal(runData[0].TestResults[2].Outcome, "Passed");
            Assert.Equal(runData[0].TestResults[2].TestCaseTitle, "OrderedTest1");
            CleanupTempFile(resultFile);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void PublishNUnitResultFile()
        {
            SetupMocks();
            string nUnitBasicResultsXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>" +
            "<!--This file represents the results of running a test suite-->" +
            "<test-results name=\"C:\\testws\\mghost\\projvc\\TBScoutTest1\\bin\\Debug\\TBScoutTest1.dll\" total=\"3\" errors=\"0\" failures=\"1\" not-run=\"0\" inconclusive=\"0\" ignored=\"0\" skipped=\"0\" invalid=\"0\" date=\"2015-04-15\" time=\"12:25:14\">" +
            "  <environment nunit-version=\"2.6.4.14350\" clr-version=\"2.0.50727.8009\" os-version=\"Microsoft Windows NT 6.2.9200.0\" platform=\"Win32NT\" cwd=\"D:\\Software\\NUnit-2.6.4\\bin\" machine-name=\"MGHOST\" user=\"madhurig\" user-domain=\"REDMOND\" />" +
            "  <culture-info current-culture=\"en-US\" current-uiculture=\"en-US\" />" +
            "  <test-suite type=\"Assembly\" name=\"C:\\testws\\mghost\\projvc\\TBScoutTest1\\bin\\Debug\\TBScoutTest1.dll\" executed=\"True\" result=\"Failure\" success=\"False\" time=\"0.059\" asserts=\"0\">" +
            "    <results>" +
            "      <test-suite type=\"Namespace\" name=\"TBScoutTest1\" executed=\"True\" result=\"Failure\" success=\"False\" time=\"0.051\" asserts=\"0\">" +
            "        <results>" +
            "          <test-suite type=\"TestFixture\" name=\"ProgramTest1\" executed=\"True\" result=\"Failure\" success=\"False\" time=\"0.050\" asserts=\"0\">" +
            "            <results>" +
            "              <test-case name=\"TBScoutTest1.ProgramTest1.MultiplyTest\" executed=\"True\" result=\"Success\" success=\"True\" time=\"-0.027\" asserts=\"1\" />" +
            "              <test-case name=\"TBScoutTest1.ProgramTest1.SumTest\" executed=\"True\" result=\"Success\" success=\"True\" time=\"0.000\" asserts=\"1\" />" +
            "              <test-case name=\"TBScoutTest1.ProgramTest1.TestSumWithZeros\" executed=\"True\" result=\"Failure\" success=\"False\" time=\"0.009\" asserts=\"1\">" +
            "                <failure>" +
            "                  <message><![CDATA[  TBScout.Program.Sum did not return the expected value." +
            "  Expected: 0" +
            "  But was:  25" +
            "]]></message>" +
            "                  <stack-trace><![CDATA[at TBScoutTest1.ProgramTest1.TestSumWithZeros() in C:\\testws\\mghost\\projvc\\TBScoutTest1\\ProgramTest1.cs:line 63" +
            "]]></stack-trace>" +
            "                </failure>" +
            "              </test-case>" +
            "            </results>" +
            "          </test-suite>" +
            "        </results>" +
            "      </test-suite>" +
            "    </results>" +
            "  </test-suite>" +
            "</test-results>";

            string resultFile = TestUtil.WriteAllTextToTempFile(nUnitBasicResultsXml, "xml");
            NUnitParser reader = new NUnitParser();
            TestRunContext runContext = new TestRunContext();
            TestDataProvider runDataProvider = reader.ParseTestResultFiles(_ec.Object, runContext, new List<string> { resultFile });
            List<TestRunData> runData = runDataProvider.GetTestRunData();

            Assert.NotNull(runData[0].TestResults);
            Assert.Equal("C:\\testws\\mghost\\projvc\\TBScoutTest1\\bin\\Debug\\TBScoutTest1.dll", runData[0].RunCreateModel.Name);
            Assert.Equal(3, runData[0].TestResults.Count);
            Assert.Equal(2, runData[0].TestResults.Count(r => r.Outcome.Equals("Passed")));
            Assert.Equal(1, runData[0].TestResults.Count(r => r.Outcome.Equals("Failed")));
            Assert.Equal(1, runData[0].AttachmentsFilePathList.Count);

            Assert.Equal(null, runData[0].TestResults[0].AutomatedTestId);
            Assert.Equal(null, runData[0].TestResults[0].AutomatedTestTypeId);
            CleanupTempFile(resultFile);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void PublishBasicNestedJUnitResults()
        {
            SetupMocks();
            string junitResultsToBeRead = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<testsuites>"
            + "<testsuite name=\"com.contoso.billingservice.ConsoleMessageRendererTest\" errors=\"0\" failures=\"1\" skipped=\"0\" tests=\"2\" time=\"0.006\" timestamp=\"2015-04-06T21:56:24\">"
            + "<testsuite errors=\"0\" failures=\"1\" hostname=\"mghost\" name=\"com.contoso.billingservice.ConsoleMessageRendererTest\" skipped=\"0\" tests=\"2\" time=\"0.006\" timestamp=\"2015-04-06T21:56:24\">"
            + "<testcase classname=\"com.contoso.billingservice.ConsoleMessageRendererTest\" name=\"testRenderNullMessage\" testType=\"asdasdas\" time=\"0.001\" />"
            + "<testcase classname=\"com.contoso.billingservice.ConsoleMessageRendererTest\" name=\"testRenderMessage\" time=\"0.003\">"
            + "<failure type=\"junit.framework.AssertionFailedError\">junit.framework.AssertionFailedError at com.contoso.billingservice.ConsoleMessageRendererTest.testRenderMessage(ConsoleMessageRendererTest.java:11)"
            + "</failure>"
            + "</testcase >"
            + "<system-out><![CDATA[Hello World!]]>"
            + "</system-out>"
            + "<system-err><![CDATA[]]></system-err>"
            + "</testsuite>"
            + "</testsuite>"
            + "</testsuites>";

            string resultFile = TestUtil.WriteAllTextToTempFile(junitResultsToBeRead, "xml");
            JUnitParser reader = new JUnitParser();
            TestRunContext runContext = new TestRunContext();
            TestDataProvider runDataProvider = reader.ParseTestResultFiles(_ec.Object, runContext, new List<string> { resultFile });
            List<TestRunData> runData = runDataProvider.GetTestRunData();

            Assert.NotNull(runData[0].TestResults);
            Assert.Equal(2, runData[0].TestResults.Count);
            Assert.Equal(1, runData[0].TestResults.Count(r => r.Outcome.Equals("Passed")));
            Assert.Equal(null, runData[0].TestResults[0].AutomatedTestId);
            Assert.Equal(null, runData[0].TestResults[0].AutomatedTestTypeId);
            Assert.Equal(1, runData[0].TestResults.Count(r => r.Outcome.Equals("Failed")));
            Assert.Equal("com.contoso.billingservice.ConsoleMessageRendererTest", runData[0].RunCreateModel.Name);
            CleanupTempFile(resultFile);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void PublishBasicXUnitResults()
        {
            SetupMocks();
            string xunitResultsToBeRead = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<assemblies>" +
            "<assembly name = \"C:\\Users\\somerandomusername\\Source\\Workspaces\\p1\\ClassLibrary2\\ClassLibrary2\\bin\\Debug\\ClassLibrary2.DLL\">" +
            "<class name=\"MyFirstUnitTests.Class1\">" +
            "<test name=\"MyFirstUnitTests.Class1.FailingTest\">" +
            "</test>" +
            "</class>" +
            "</assembly>" +
            "</assemblies>";

            string resultFile = TestUtil.WriteAllTextToTempFile(xunitResultsToBeRead, "xml");
            JUnitParser reader = new JUnitParser();
            TestRunContext runContext = new TestRunContext();
            TestDataProvider runDataProvider = reader.ParseTestResultFiles(_ec.Object, runContext, new List<string> { resultFile });
            List<TestRunData> runData = runDataProvider.GetTestRunData();

            Assert.Equal(1, runData[0].AttachmentsFilePathList.Count);
            CleanupTempFile(resultFile);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void PublishBasicCTestResults()
        {
            SetupMocks();
            string cTestResultsToBeRead = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Site BuildName=\"(empty)\" BuildStamp=\"20180515-1731-Experimental\" Name=\"(empty)\" Generator=\"ctest-3.11.0\" "
            + "CompilerName=\"\" CompilerVersion=\"\" OSName=\"Linux\" Hostname=\"3tnavBuild\" OSRelease=\"4.4.0-116-generic\" "
            + "OSVersion=\"#140-Ubuntu SMP Mon Feb 12 21:23:04 UTC 2018\" OSPlatform=\"x86_64\" Is64Bits=\"1\">"
            + "<Testing>"
            + "<StartDateTime>May 15 10:31 PDT</StartDateTime>"
            + "<StartTestTime>1526405497</StartTestTime>"
            + "<TestList>"
            + "<Test>./libs/MgmtVisualization/tests/LoggingSinkRandomTests.loggingSinkRandomTest_CallLoggingManagerCallback</Test>"
            + "<Test>./tools/simulator/test/simulator.SimulatorTest.readEventFile_mediaDetectedEvent_oneSignalEmitted</Test>"
            + "</TestList>"
            + "<Test Status =\"passed\">"
            + "<Name>LoggingSinkRandomTests.loggingSinkRandomTest_CallLoggingManagerCallback</Name>"
            + "<Path>./libs/MgmtVisualization/tests</Path>"
            + "<FullName>./libs/MgmtVisualization/tests/LoggingSinkRandomTests.loggingSinkRandomTest_CallLoggingManagerCallback</FullName>"
            + "<FullCommandLine>D:/a/r1/a/libs/MgmtVisualization/tests/MgmtVisualizationResultsAPI \"--gtest_filter=LoggingSinkRandomTests.loggingSinkRandomTest_CallLoggingManagerCallback\"</FullCommandLine>"
            + "<Results>"
            + "<NamedMeasurement type =\"numeric/double\" name=\"Execution Time\">"
            + "<Value>0.074303</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type =\"numeric/double\" name=\"Processors\">"
            + "<Value>1</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type =\"text/string\" name=\"Completion Status\">"
            + "<Value>Completed</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type =\"text/string\" name=\"Command Line\">"
            + "<Value>/home/ctc/jenkins/workspace/build_TNAV-dev_Pull-Request/build/libs/MgmtVisualization/tests/MgmtVisualizationTestPublicAPI \"--gtest_filter=loggingSinkRandomTests.loggingSinkRandomTest_CallLoggingCallback\"</Value>"
            + "</NamedMeasurement>"
            + "<Measurement>"
            + "<Value>output : [----------] Global test environment set-up.</Value>"
            + "</Measurement>"
            + "</Results>"
            + "</Test>"
            + "<Test Status =\"notrun\">"
            + "<Name>simulator.SimulatorTest.readEventFile_mediaDetectedEvent_oneSignalEmitted</Name>"
            + "<Path>./tools/simulator/test</Path>"
            + "<FullName>./tools/simulator/test/simulator.SimulatorTest.readEventFile_mediaDetectedEvent_oneSignalEmitted</FullName>"
            + "<FullCommandLine></FullCommandLine>"
            + "<Results>"
            + "<NamedMeasurement type =\"numeric/double\" name=\"Processors\">"
            + "<Value>1</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type =\"text/string\" name=\"Completion Status\">"
            + "<Value>Disabled</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type =\"text/string\" name=\"Command Line\">"
            + "<Value></Value>"
            + "</NamedMeasurement>"
            + "<Measurement>"
            + "<Value>Disabled</Value>"
            + "</Measurement>"
            + "</Results>"
            + "</Test>"
            + "<Test Status=\"passed\">"
            + "<Name>test_cgreen_run_named_test</Name>"
            + "<Path>./tests</Path>"
            + "<FullName>./tests/test_cgreen_run_named_test</FullName>"
            + "<FullCommandLine>/var/lib/jenkins/workspace/Cgreen-thoni56/build/build-c/tests/test_cgreen_c &quot;integer_one_should_assert_true&quot;</FullCommandLine>"
            + "<Results>"
            + "<NamedMeasurement type=\"numeric/double\" name=\"Execution Time\"><Value>0.00615707</Value></NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Completion Status\"><Value>Completed</Value></NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Command Line\"><Value>/var/lib/jenkins/workspace/Cgreen-thoni56/build/build-c/tests/test_cgreen_c &quot;integer_one_should_assert_true&quot;</Value></NamedMeasurement>"
            + "<Measurement>"
            + "<Value>Running &quot;all_c_tests&quot; (136 tests)..."
            + "Completed &quot;assertion_tests&quot;: 1 pass, 0 failures, 0 exceptions in 0ms."
            + "Completed &quot;all_c_tests&quot;: 1 pass, 0 failures, 0 exceptions in 0ms."
            + "</Value>"
            + "</Measurement>"
            + "</Results>"
            + "</Test>"
            + "<Test Status=\"passed\">"
            + "<Name>runner_test_cgreen_c</Name>"
            + "<Path>./tests</Path>"
            + "<FullName>./tests/runner_test_cgreen_c</FullName>"
            + "<FullCommandLine>D:/a/r1/a/Cgreen-thoni56/build/build-c/tools/cgreen-runner &quot;-x&quot; &quot;TEST&quot; &quot;libcgreen_c_tests.so&quot;</FullCommandLine>"
            + "<Results>"
            + "<NamedMeasurement type=\"numeric/double\" name=\"Execution Time\"><Value>0.499399</Value></NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Completion Status\"><Value>Completed</Value></NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Command Line\"><Value>/var/lib/jenkins/workspace/Cgreen-thoni56/build/build-c/tools/cgreen-runner &quot;-x&quot; &quot;TEST&quot; &quot;libcgreen_c_tests.so&quot;</Value></NamedMeasurement>"
            + "<Measurement>"
            + "<Value>	CGREEN EXCEPTION: Too many assertions within a single test."
            + "</Value>"
            + "</Measurement>"
            + "</Results>"
            + "</Test>"
            + "<Test Status=\"failed\">"
            + "<Name>WGET-testU-MD5-fail</Name>"
            + "<Path>E_/foo/sources</Path>"
            + "<FullName>E_/foo/sources/WGET-testU-MD5-fail</FullName>"
            + "<FullCommandLine>E:\\Tools\\cmake\\cmake-2.8.11-rc4-win32-x86\\bin\\cmake.exe &quot;-DTEST_OUTPUT_DIR:PATH=E:/foo/build-vs2008-visual/_cmake/modules/testU_WGET&quot;"
            + "&quot;-P&quot; &quot;E:/foo/sources/modules/testU/WGET-testU-MD5-fail.cmake&quot;</FullCommandLine>"
            + "<Results>"
            + "<NamedMeasurement type=\"text/string\" name=\"Exit Code\">"
            + "<Value>Failed</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Exit Value\">"
            + "<Value>0</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"numeric/double\" name=\"Execution Time\">"
            + "<Value>0.0760078</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Completion Status\">"
            + "<Value>Completed</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Command Line\">"
            + "<Value>E:\\Tools\\cmake\\cmake-2.8.11-rc4-win32-x86\\bin\\cmake.exe &quot;-DTEST_OUTPUT_DIR:PATH=E:/foo/build-vs2008-visual/_cmake/modules/testU_WGET&quot;"
            + "&quot;-P&quot; &quot;E:/foo/sources/modules/testU/WGET-testU-MD5-fail.cmake&quot;</Value>"
            + "</NamedMeasurement>"
            + "<Measurement>"
            + "<Value>-- Download of file://\\abc-mang.md5.txt"
            + "failed with message: [37]&quot;couldn&apos;t read a file:// file&quot;"
            + "</Value>"
            + "</Measurement>"
            + "</Results>"
            + "</Test>"
            + "<Test Status=\"failed\">"
            + "<Name>WGET-testU-noMD5</Name>"
            + "<Path>E_/foo/sources</Path>"
            + "<FullName>E_/foo/sources/WGET-testU-noMD5</FullName>"
            + "<FullCommandLine>E:\\Tools\\cmake\\cmake-2.8.11-rc4-win32-x86\\bin\\cmake.exe &quot;-DTEST_OUTPUT_DIR:PATH=E:/foo/build-vs2008-visual/_cmake/modules/testU_WGET&quot;"
            + "&quot;-P&quot; &quot;E:/foo/sources/modules/testU/WGET-testU-noMD5.cmake&quot;</FullCommandLine>"
            + "<Results>"
            + "<NamedMeasurement type=\"text/string\" name=\"Exit Code\">"
            + "<Value>Failed</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Exit Value\">"
            + "<Value>1</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"numeric/double\" name=\"Execution Time\">"
            + "<Value>0.0820084</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Completion Status\">"
            + "<Value>Completed</Value>"
            + "</NamedMeasurement>"
            + "<NamedMeasurement type=\"text/string\" name=\"Command Line\">"
            + "<Value>E:\\Tools\\cmake\\cmake-2.8.11-rc4-win32-x86\\bin\\cmake.exe &quot;-DTEST_OUTPUT_DIR:PATH=E:/foo/build-vs2008-visual/_cmake/modules/testU_WGET&quot;"
            + "&quot;-P&quot; &quot;E:/foo/sources/modules/testU/WGET-testU-noMD5.cmake&quot;</Value>"
            + "</NamedMeasurement>"
            + "<Measurement>"
            + "<Value>-- Download of file://\\abc-mang.md5.txt"
            + "failed with message: [37]&quot;couldn&apos;t read a file:// file&quot;"
            + "CMake Error at modules/Logging.cmake:121 (message):"
            + ""
            + ""
            + "test BAR_wget_file succeed: result is &quot;OFF&quot; instead of &quot;ON&quot;"
            + ""
            + "Call Stack (most recent call first):"
            + "modules/Test.cmake:74 (BAR_msg_fatal)"
            + "modules/testU/WGET-testU-noMD5.cmake:14 (BAR_check_equal)"
            + ""
            + ""
            + "</Value>"
            + "</Measurement>"
            + "</Results>"
            + "</Test>"
            + "<EndDateTime>May 15 10:37 PDT</EndDateTime>"
            + "<EndTestTime>1526405879</EndTestTime>"
            + "<ElapsedMinutes>6</ElapsedMinutes>"
            + "</Testing>"
            + "</Site>";

            string resultFile = TestUtil.WriteAllTextToTempFile(cTestResultsToBeRead, "xml");
            CTestParser reader = new CTestParser();
            TestRunContext runContext = new TestRunContext();
            TestDataProvider runDataProvider = reader.ParseTestResultFiles(_ec.Object, runContext, new List<string> { resultFile });
            List<TestRunData> runData = runDataProvider.GetTestRunData();

            Assert.NotNull(runData[0].TestResults);
            Assert.Equal(6, runData[0].TestResults.Count);
            Assert.Equal(3, runData[0].TestResults.Count(r => r.Outcome.Equals("Passed")));
            Assert.Equal(2, runData[0].TestResults.Count(r => r.Outcome.Equals("Failed")));
            Assert.Equal(1, runData[0].TestResults.Count(r => r.Outcome.Equals("NotExecuted")));
            Assert.Equal("CTest Test Run  ", runData[0].RunCreateModel.Name);
            Assert.Equal("Completed", runData[0].TestResults[0].State);
            Assert.Equal("./libs/MgmtVisualization/tests/LoggingSinkRandomTests.loggingSinkRandomTest_CallLoggingManagerCallback", runData[0].TestResults[0].AutomatedTestName);
            Assert.Equal("./libs/MgmtVisualization/tests", runData[0].TestResults[0].AutomatedTestStorage);
            Assert.Equal("LoggingSinkRandomTests.loggingSinkRandomTest_CallLoggingManagerCallback", runData[0].TestResults[0].TestCaseTitle);
            Assert.Equal(null, runData[0].TestResults[0].AutomatedTestId);
            Assert.Equal(null, runData[0].TestResults[0].AutomatedTestTypeId);
            CleanupTempFile(resultFile);
        }

        private void CleanupTempFile(string resultFile)
        {
            try
            {
                File.Delete(resultFile);
            }
            catch
            {
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "TestHostContext")]
        private void SetupMocks([CallerMemberName] string name = "")
        {
            TestHostContext hc = new TestHostContext(this, name);
            _ec = new Mock<IExecutionContext>();
            List<string> warnings;
            var variables = new Variables(hc, new Dictionary<string, VariableValue>(), out warnings);
            _ec.Setup(x => x.Variables).Returns(variables);
            _ec.Setup(x => x.Write(It.IsAny<string>(), It.IsAny<string>(), true))
                .Callback<string, string, bool>
                ((tag, message, canMaskSecrets) =>
                {
                    Console.Error.WriteLine(tag + ": " + message);
                });
            _mockFeatureFlagService = new Mock<IFeatureFlagService>();
            _mockFeatureFlagService.Setup(x => x.GetFeatureFlagState(It.IsAny<string>(), It.IsAny<Guid>())).Returns(true);
            _ec.Setup(x => x.GetHostContext()).Returns(hc);
            hc.SetSingleton(_mockFeatureFlagService.Object);
        }
    }
}
