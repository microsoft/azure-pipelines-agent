// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker;

public interface IJobExecutionContext : IExecutionContext
{
    public ITaskExecutionContext CreateTaskExecutionContext(
        Guid recordId,
        string displayName,
        string refName,
        Variables taskVariables = null,
        bool outputForward = false,
        List<TaskRestrictions> taskRestrictions = null);
}

public sealed class JobExecutionContext : ExecutionContext, IJobExecutionContext
{
    public ITaskExecutionContext CreateTaskExecutionContext(
        Guid recordId,
        string displayName,
        string refName,
        Variables taskVariables = null,
        bool outputForward = false,
        List<TaskRestrictions> taskRestrictions = null)
    {
        Trace.Entering();

        var taskContext = new TaskExecutionContext();
        taskContext.Initialize(HostContext);
        taskContext.InitContextProperties(
            jobContext: this,
            features: Features,
            variables: Variables,
            endpoints: Endpoints,
            repositories: Repositories,
            jobSettings: JobSettings,
            secureFiles: SecureFiles,
            taskVariables: taskVariables,
            writeDebug: WriteDebug,
            prependPath: PrependPath,
            containers: Containers,
            sidecarContainers: SidecarContainers,
            outputForward: outputForward,
            defaultStepTarget: _defaultStepTarget,
            currentStepTarget: _currentStepTarget,
            cancellationTokenSource: new CancellationTokenSource());

        if (taskRestrictions != null)
        {
            taskContext.Restrictions.AddRange(taskRestrictions);
        }

        taskContext.InitializeTimelineRecord(_mainTimelineId, recordId, _record.Id, ExecutionContextType.Task, displayName, refName, ++_childTimelineRecordOrder);

        taskContext.InitLogger(_mainTimelineId, recordId);

        return taskContext;
    }
}
