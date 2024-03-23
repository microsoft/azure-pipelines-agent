// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Agent.Worker.Audit;

[ServiceLocator(Default = typeof(TaskAuditLogsService))]
public interface ITaskAuditLogsService : IAgentService
{
    void Initialize(Pipelines.AgentJobRequestMessage jobRequest, VssConnection connection);
    Task SendLog(TaskAuditLog logs, CancellationToken ct = default);
}

public sealed class TaskAuditLogsService : AgentService, ITaskAuditLogsService
{
    private bool _isInitialized;
    private TaskHttpClient _taskClient;

    // Job message information
    private Guid _scopeIdentifier;
    private string _hubName;
    private Guid _planId;
    private Guid _jobTimelineRecordId;

    public void Initialize(Pipelines.AgentJobRequestMessage jobRequest, VssConnection connection)
    {
        ArgUtil.NotNull(connection, nameof(connection));
        ArgUtil.NotNull(jobRequest, nameof(jobRequest));
        ArgUtil.NotNull(jobRequest.Plan, nameof(jobRequest.Plan));

        if (!connection.HasAuthenticated)
        {
            throw new InvalidOperationException($"VssConnection has not been authenticated for {typeof(TaskAuditLogsService)}.");
        }

        _scopeIdentifier = jobRequest.Plan.ScopeIdentifier;
        _hubName = jobRequest.Plan.PlanType;
        _planId = jobRequest.Plan.PlanId;
        _jobTimelineRecordId = jobRequest.JobId;

        _taskClient = connection.GetClient<TaskHttpClient>();
        _isInitialized = true;
    }

    public async Task SendLog(TaskAuditLog log, CancellationToken ct)
    {
        EnsureInitialized();
        ArgUtil.NotNull(log, nameof(log));

        Trace.Info($"Sending audit log for task {log.TaskId} in job jobId of plan {_planId} in scope identifier {_scopeIdentifier}.");
        try
        {
            await _taskClient.SendTaskAuditLogAsync(
                                scopeIdentifier: _scopeIdentifier,
                                hubName: _hubName,
                                planId: _planId,
                                jobId: _jobTimelineRecordId,
                                log: log,
                                cancellationToken: ct);
        }
        catch (VssServiceResponseException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Trace.Info("Task audit endpoint is not available.");
            }
            else
            {
                Trace.Error(ex);
                throw;
            }
        }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("The service has not been initialized.");
        }
    }
}