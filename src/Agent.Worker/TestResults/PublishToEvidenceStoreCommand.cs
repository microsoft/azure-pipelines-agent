using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;
using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public sealed class PublishToEvidenceStoreCommand : IWorkerCommand
    {
        public string Name => "publishtoevidencestore";

        public List<string> Aliases => null;

        private IExecutionContext _executionContext;
        private TestRunSummary testRunSummary;
        private string testRunner;
        private string description;
        private string name;

        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));

            var eventProperties = command.Properties;

            _executionContext = context;
            ParseInputParameters(context, eventProperties);

            var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("PublishTestResultsToEvidenceStore"));
            commandContext.Task = PublishTestResultsDataToEvidenceStore(context);
            _executionContext.AsyncCommands.Add(commandContext);
        }

        private Task PublishTestResultsDataToEvidenceStore(IExecutionContext context)
        {
            TestResultUtils.StoreTestRunSummaryInEnvVar(context, testRunSummary, testRunner, name, description);

            return Task.FromResult(0);
        }

        private void ParseInputParameters(IExecutionContext context, Dictionary<string, string> eventProperties)
        {
            eventProperties.TryGetValue("testrunner", out testRunner);
            eventProperties.TryGetValue("name", out name);
            eventProperties.TryGetValue("testRunSummary", out string testRunSummaryString);
            if(string.IsNullOrEmpty(testRunSummaryString)) 
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "TestRunSummary"));
            }
            testRunSummary = JsonConvert.DeserializeObject<TestRunSummary>(testRunSummaryString);
            eventProperties.TryGetValue("description", out description);
        }
    }
}