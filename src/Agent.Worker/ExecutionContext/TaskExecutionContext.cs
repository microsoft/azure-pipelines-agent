// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Worker;

public interface ITaskExecutionContext : IExecutionContext
{
    public IJobExecutionContext JobContext { get; }

    public void InitLogger(Guid mainTimelineId, Guid recordId);

    public void InitContextProperties(
        IJobExecutionContext jobContext,
        PlanFeatures features,
        Variables variables,
        List<ServiceEndpoint> endpoints,
        List<RepositoryResource> repositories,
        Dictionary<string, string> jobSettings,
        List<SecureFile> secureFiles,
        Variables taskVariables,
        bool writeDebug,
        List<string> prependPath,
        List<ContainerInfo> containers,
        List<ContainerInfo> sidecarContainers,
        bool outputForward,
        ExecutionTargetInfo defaultStepTarget,
        ExecutionTargetInfo currentStepTarget,
        CancellationTokenSource cancellationTokenSource);
}

public sealed class TaskExecutionContext : ExecutionContext, ITaskExecutionContext
{
    public IJobExecutionContext JobContext { get; private set; }

    public TaskExecutionContext() { }

    public void InitContextProperties(
        IJobExecutionContext jobContext,
        PlanFeatures features,
        Variables variables,
        List<ServiceEndpoint> endpoints,
        List<RepositoryResource> repositories,
        Dictionary<string,string> jobSettings,
        List<SecureFile> secureFiles,
        Variables taskVariables,
        bool writeDebug,
        List<string> prependPath,
        List<ContainerInfo> containers,
        List<ContainerInfo> sidecarContainers,
        bool outputForward,
        ExecutionTargetInfo defaultStepTarget,
        ExecutionTargetInfo currentStepTarget,
        CancellationTokenSource cancellationTokenSource)
    {
        JobContext = jobContext;
        _parentExecutionContext = jobContext;
        Features = features;
        Variables = variables;
        Endpoints = endpoints;
        Repositories = repositories;
        JobSettings = jobSettings;
        SecureFiles = secureFiles;
        TaskVariables = taskVariables;
        _cancellationTokenSource = cancellationTokenSource;
        WriteDebug = writeDebug;
        PrependPath = prependPath;
        Containers = containers;
        SidecarContainers = sidecarContainers;
        _outputForward = outputForward;
        _defaultStepTarget = defaultStepTarget;
        _currentStepTarget = currentStepTarget;
    }

    public void InitLogger(Guid mainTimelineId, Guid recordId)
    {
        _logger = HostContext.CreateService<IPagingLogger>();
        _logger.Setup(mainTimelineId, recordId);
    }
}
