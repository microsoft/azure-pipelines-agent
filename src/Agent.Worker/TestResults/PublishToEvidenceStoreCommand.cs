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
            try
            {
                ArgUtil.NotNull(context, nameof(context));
                ArgUtil.NotNull(command, nameof(command));
                var eventProperties = command.Properties;

                _executionContext = context;
                ParseInputParameters(context, eventProperties);

                var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
                commandContext.InitializeCommandContext(context, "PublishTestResultsToEvidenceStore");
                commandContext.Task = PublishTestResultsDataToEvidenceStore(context);
                _executionContext.AsyncCommands.Add(commandContext);
            }
            catch (System.Exception ex)
            {
                _executionContext.Debug($"Error in executing the command, Error Details {ex}");
            }
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
            if (string.IsNullOrEmpty(testRunSummaryString))
            {
                throw new ArgumentException($"ArgumentNeeded : TestRunSummary");
            }
            testRunSummary = JsonConvert.DeserializeObject<TestRunSummary>(testRunSummaryString);
            eventProperties.TryGetValue("description", out description);
        }
    }
}