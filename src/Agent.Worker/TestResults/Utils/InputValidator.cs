using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    internal class InputValidator
    {
        public InputValidator(IExecutionContext executionContext)
        {
            ExecutionContext = executionContext;
        }

        protected readonly IExecutionContext ExecutionContext;

        public void CheckFilePathList(IList<string> filePathList)
        {
            if (filePathList == null)
            {
                throw new ArgumentNullException(nameof(filePathList));
            }
            if (!CheckIList(filePathList))
            {
                ExecutionContext.Debug("Atleast one member of FilePathList is null");
            }
        }

        public void CheckTestRunContext(TestRunContext runContext)
        {
            if (runContext == null)
            {
                ExecutionContext.Debug("runContext null");
                return;
            }
            if (runContext.Platform == null)
            {
                ExecutionContext.Debug("runContext.Platform is null");
            }
            if (runContext.Configuration == null)
            {
                ExecutionContext.Debug("runContext.Configuration is null");
            }
            if (runContext.BuildId < 0)
            {
                ExecutionContext.Debug("runContext.BuildId is negative");
            }
            if (runContext.BuildUri == null)
            {
                ExecutionContext.Debug("runContext.BuildURI is null");
            }
            if (runContext.ReleaseUri == null)
            {
                ExecutionContext.Debug("runContext.ReleaseURI is null");
            }
            if (runContext.ReleaseEnvironmentUri == null)
            {
                ExecutionContext.Debug("runContext.ReleaseEnvironmentUri is null");
            }
        }

        protected bool CheckIList<T>(IList<T> list)
        {
            return list != null && list.All(l => l != null);
        }
    }
}