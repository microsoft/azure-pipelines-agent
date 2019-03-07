using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.Log.TestFilePublisher.Plugin;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.VisualStudio.Services.WebApi;

namespace Agent.Plugins.TestFilePublisher
{
    public interface ITestFilePublisher
    {
        Task InitializeAsync();
        Task PublishAsync();
    }

    public class TestFilePublisher : ITestFilePublisher
    {
        private readonly VssConnection _vssConnection;
        private readonly PipelineConfig _pipelineConfig;
        private readonly TraceListener _traceListener;
        private ITestFileFinder _testFileFinder;
        private ITestResultParser _testResultParser;
        private ITestRunPublisher _testRunPublisher;

        public TestFilePublisher(VssConnection vssConnection, PipelineConfig pipelineConfig, TraceListener traceListener)
        {
            _vssConnection = vssConnection;
            _pipelineConfig = pipelineConfig;
            _traceListener = traceListener;
        }

        public TestFilePublisher(VssConnection vssConnection, PipelineConfig pipelineConfig, TraceListener traceListener,
            ITestFileFinder testFileFinder, ITestResultParser testResultParser, ITestRunPublisher testRunPublisher)
        : this(vssConnection, pipelineConfig, traceListener)
        {
            _testFileFinder = testFileFinder;
            _testResultParser = testResultParser;
            _testRunPublisher = testRunPublisher;
        }

        public async Task InitializeAsync()
        {
            await Task.Run(Initialize);
        }

        public async Task PublishAsync()
        {
            var testRunContext = new TestRunContextBuilder("Auto Published Test Run")
                .WithBuildId(_pipelineConfig.BuildId)
                .WithBuildUri(_pipelineConfig.BuildUri)
                .Build();

            var testResultFiles = await FindTestFilesAsync();


            var testData = _testResultParser.ParseTestResultFiles(testRunContext, testResultFiles.ToList()).GetTestRunData();
            var testRuns = await _testRunPublisher.PublishTestRunDataAsync(testRunContext, _pipelineConfig.ProjectName, testData, new PublishOptions(), new CancellationToken());
        }

        protected async Task<IEnumerable<string>> FindTestFilesAsync()
        {
            return await _testFileFinder.FindAsync(_pipelineConfig.Pattern);
        }

        private void Initialize()
        {
            _testFileFinder = _testFileFinder ?? new TestFileFinder(_pipelineConfig.SearchFolders);
            _testResultParser = _testResultParser ?? new JUnitResultParser(_traceListener);
            _testRunPublisher = _testRunPublisher ?? new TestRunPublisher(_vssConnection, _traceListener);
        }
    }
}
