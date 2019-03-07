using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.VisualStudio.Services.WebApi;

namespace Agent.Plugins.TestResultParser
{
    public class Test
    {
        public Test(VssConnection vssConnection)
        {
            _vssConnection = vssConnection;
        }

        public async Task TestMethod(TestRunContext runContext)
        {
            var files = new JunitTestFileFinder().Find().Result;

            var parser = new JUnitResultParser(new TextWriterTraceListener());

            var testData = parser.ParseTestResultFiles(runContext, files).GetTestRunData();

            var publisher = new TestRunPublisher(_vssConnection, new TextWriterTraceListener());

            await publisher.PublishTestRunDataAsync(runContext, "AzureDevOps", testData, new PublishOptions(), new CancellationToken());
        }

        private VssConnection _vssConnection;
    }
}
