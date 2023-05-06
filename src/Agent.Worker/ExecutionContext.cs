// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public class ExecutionContextType
    {
        public static string Job = "Job";
        public static string Task = "Task";
    }

    [ServiceLocator(Default = typeof(ExecutionContext))]
    public interface IExecutionContext : IAgentService, IKnobValueContext
    {
        Guid Id { get; }
        Task ForceCompleted { get; }
        TaskResult? Result { get; set; }
        string ResultCode { get; set; }
        TaskResult? CommandResult { get; set; }
        CancellationToken CancellationToken { get; }
        List<ServiceEndpoint> Endpoints { get; }
        List<SecureFile> SecureFiles { get; }
        List<Pipelines.RepositoryResource> Repositories { get; }
        Dictionary<string, string> JobSettings { get; }

        PlanFeatures Features { get; }
        Variables Variables { get; }
        Variables TaskVariables { get; }
        HashSet<string> OutputVariables { get; }
        List<IAsyncCommandContext> AsyncCommands { get; }
        List<string> PrependPath { get; }
        List<ContainerInfo> Containers { get; }
        List<ContainerInfo> SidecarContainers { get; }
        List<TaskRestrictions> Restrictions { get; }

        // Initialize
        void InitializeJob(Pipelines.AgentJobRequestMessage message, CancellationToken token);
        void CancelToken();
        IExecutionContext CreateChild(Guid recordId, string displayName, string refName, Variables taskVariables = null, bool outputForward = false, List<TaskRestrictions> taskRestrictions = null);

        // logging
        bool WriteDebug { get; }
        long Write(string tag, string message, bool canMaskSecrets = true);
        void QueueAttachFile(string type, string name, string filePath);
        ITraceWriter GetTraceWriter();

        // timeline record update methods
        void Start(string currentOperation = null);
        TaskResult Complete(TaskResult? result = null, string currentOperation = null, string resultCode = null);
        void SetVariable(string name, string value, bool isSecret = false, bool isOutput = false, bool isFilePath = false, bool isReadOnly = false);
        void SetTimeout(TimeSpan? timeout);
        void AddIssue(Issue issue);
        void Progress(int percentage, string currentOperation = null);
        void UpdateDetailTimelineRecord(TimelineRecord record);

        // others
        void ForceTaskComplete();
        string TranslateToHostPath(string path);
        ExecutionTargetInfo StepTarget();
        void SetStepTarget(Pipelines.StepTarget target);
        string TranslatePathForStepTarget(string val);
        IHostContext GetHostContext();
        /// <summary>
        /// Re-initializes force completed - between next retry attempt
        /// </summary>
        /// <returns></returns>
        void ReInitializeForceCompleted();
        /// <summary>
        /// Cancel force task completion between retry attempts
        /// </summary>
        /// <returns></returns>
        void CancelForceTaskCompletion();
    }

    public sealed class ExecutionContext : AgentService, IExecutionContext, IDisposable
    {
        private const int _maxIssueCount = 10;

        private readonly TimelineRecord _record = new TimelineRecord();
        private readonly Dictionary<Guid, TimelineRecord> _detailRecords = new Dictionary<Guid, TimelineRecord>();
        private readonly object _loggerLock = new object();
        private readonly List<IAsyncCommandContext> _asyncCommands = new List<IAsyncCommandContext>();
        private readonly HashSet<string> _outputvariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TaskRestrictions> _restrictions = new List<TaskRestrictions>();
        private readonly string _buildLogsFolderName = "buildlogs";
        private IAgentLogPlugin _logPlugin;
        private IPagingLogger _logger;
        private IJobServerQueue _jobServerQueue;
        private IExecutionContext _parentExecutionContext;
        private bool _outputForward = false;
        private Guid _mainTimelineId;
        private Guid _detailTimelineId;
        private int _childTimelineRecordOrder = 0;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationTokenSource _forceCompleteCancellationTokenSource = new CancellationTokenSource();
        private TaskCompletionSource<int> _forceCompleted = new TaskCompletionSource<int>();
        private bool _throttlingReported = false;
        private ExecutionTargetInfo _defaultStepTarget;
        private ExecutionTargetInfo _currentStepTarget;
        private bool _disableLogUploads;
        private string _buildLogsFolderPath;
        private string _buildLogsFile;
        private FileStream _buildLogsData;
        private StreamWriter _buildLogsWriter;

        // only job level ExecutionContext will track throttling delay.
        private long _totalThrottlingDelayInMilliseconds = 0;

        public Guid Id => _record.Id;
        public Task ForceCompleted => _forceCompleted.Task;
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;
        public CancellationToken ForceCompleteCancellationToken => _forceCompleteCancellationTokenSource.Token;
        public List<ServiceEndpoint> Endpoints { get; private set; }
        public List<SecureFile> SecureFiles { get; private set; }
        public List<Pipelines.RepositoryResource> Repositories { get; private set; }
        public Dictionary<string, string> JobSettings { get; private set; }
        public Variables Variables { get; private set; }
        public Variables TaskVariables { get; private set; }
        public HashSet<string> OutputVariables => _outputvariables;
        public bool WriteDebug { get; private set; }
        public List<string> PrependPath { get; private set; }
        public List<ContainerInfo> Containers { get; private set; }
        public List<ContainerInfo> SidecarContainers { get; private set; }
        public List<TaskRestrictions> Restrictions => _restrictions;
        public List<IAsyncCommandContext> AsyncCommands => _asyncCommands;

        public TaskResult? Result
        {
            get
            {
                return _record.Result;
            }
            set
            {
                _record.Result = value;
            }
        }

        public TaskResult? CommandResult { get; set; }

        private string ContextType => _record.RecordType;

        public string ResultCode
        {
            get
            {
                return _record.ResultCode;
            }
            set
            {
                _record.ResultCode = value;
            }
        }

        public PlanFeatures Features { get; private set; }

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            _disableLogUploads = HostContext.GetService<IConfigurationStore>().GetSettings().DisableLogUploads;

            if (_disableLogUploads)
            {
                _buildLogsFolderPath = Path.Combine(hostContext.GetDiagDirectory(), _buildLogsFolderName);
                Directory.CreateDirectory(_buildLogsFolderPath);
            }

            _jobServerQueue = HostContext.GetService<IJobServerQueue>();
        }

        public void CancelToken()
        {
            _cancellationTokenSource.Cancel();
        }

        public void ForceTaskComplete()
        {
            Trace.Info("Force finish current task in 5 sec.");
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ForceCompleteCancellationToken);
                if (!ForceCompleteCancellationToken.IsCancellationRequested)
                {
                    _forceCompleted?.TrySetResult(1);
                }
            });
        }

        public void CancelForceTaskCompletion()
        {
            Trace.Info($"Forced completion canceled");
            this._forceCompleteCancellationTokenSource.Cancel();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721: Property names should not match get methods")]
        public IHostContext GetHostContext()
        {
            return HostContext;
        }

        public IExecutionContext CreateChild(Guid recordId, string displayName, string refName, Variables taskVariables = null, bool outputForward = false, List<TaskRestrictions> taskRestrictions = null)
        {
            Trace.Entering();

            var child = new ExecutionContext();
            child.Initialize(HostContext);
            child.Features = Features;
            child.Variables = Variables;
            child.Endpoints = Endpoints;
            child.Repositories = Repositories;
            child.JobSettings = JobSettings;
            child.SecureFiles = SecureFiles;
            child.TaskVariables = taskVariables;
            child._cancellationTokenSource = new CancellationTokenSource();
            child.WriteDebug = WriteDebug;
            child._parentExecutionContext = this;
            child.PrependPath = PrependPath;
            child.Containers = Containers;
            child.SidecarContainers = SidecarContainers;
            child._outputForward = outputForward;
            child._defaultStepTarget = _defaultStepTarget;
            child._currentStepTarget = _currentStepTarget;

            if (taskRestrictions != null)
            {
                child.Restrictions.AddRange(taskRestrictions);
            }

            child.InitializeTimelineRecord(_mainTimelineId, recordId, _record.Id, ExecutionContextType.Task, displayName, refName, ++_childTimelineRecordOrder);

            child._logger = HostContext.CreateService<IPagingLogger>();
            child._logger.Setup(_mainTimelineId, recordId);

            return child;
        }

        public void Start(string currentOperation = null)
        {
            _record.CurrentOperation = currentOperation ?? _record.CurrentOperation;
            _record.StartTime = DateTime.UtcNow;
            _record.State = TimelineRecordState.InProgress;

            _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);

            if (_disableLogUploads)
            {
                var buildLogsJobFolder = Path.Combine(_buildLogsFolderPath, _mainTimelineId.ToString());
                Directory.CreateDirectory(buildLogsJobFolder);
                string pattern = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                Regex regex = new Regex(string.Format("[{0}]", Regex.Escape(pattern)));
                var recordName = regex.Replace(_record.Name, string.Empty);

                _buildLogsFile = Path.Combine(buildLogsJobFolder, $"{recordName}-{_record.Id.ToString()}.log");
                _buildLogsData = new FileStream(_buildLogsFile, FileMode.CreateNew);
                _buildLogsWriter = new StreamWriter(_buildLogsData, System.Text.Encoding.UTF8);

                _logger.Write(StringUtil.Loc("BuildLogsMessage", _buildLogsFile));
            }
        }

        public TaskResult Complete(TaskResult? result = null, string currentOperation = null, string resultCode = null)
        {
            if (result != null)
            {
                Result = result;
            }

            if (_disableLogUploads)
            {
                _buildLogsWriter.Flush();
                _buildLogsData.Flush();
                //The StreamWriter object calls Dispose() on the provided Stream object when StreamWriter.Dispose is called.
                _buildLogsWriter.Dispose();
                _buildLogsWriter = null;
                _buildLogsData.Dispose();
                _buildLogsData = null;
            }

            // report total delay caused by server throttling.
            if (_totalThrottlingDelayInMilliseconds > 0)
            {
                this.Warning(StringUtil.Loc("TotalThrottlingDelay", TimeSpan.FromMilliseconds(_totalThrottlingDelayInMilliseconds).TotalSeconds));
            }

            if (!AgentKnobs.DisableDrainQueuesAfterTask.GetValue(this).AsBoolean())
            {
                _jobServerQueue.ForceDrainWebConsoleQueue = true;
                _jobServerQueue.ForceDrainTimelineQueue = true;
            }

            _record.CurrentOperation = currentOperation ?? _record.CurrentOperation;
            _record.ResultCode = resultCode ?? _record.ResultCode;
            _record.FinishTime = DateTime.UtcNow;
            _record.PercentComplete = 100;
            _record.Result = _record.Result ?? TaskResult.Succeeded;
            _record.State = TimelineRecordState.Completed;

            _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);

            // complete all detail timeline records.
            if (_detailTimelineId != Guid.Empty && _detailRecords.Count > 0)
            {
                foreach (var record in _detailRecords)
                {
                    record.Value.FinishTime = record.Value.FinishTime ?? DateTime.UtcNow;
                    record.Value.PercentComplete = record.Value.PercentComplete ?? 100;
                    record.Value.Result = record.Value.Result ?? TaskResult.Succeeded;
                    record.Value.State = TimelineRecordState.Completed;

                    _jobServerQueue.QueueTimelineRecordUpdate(_detailTimelineId, record.Value);
                }
            }

            _cancellationTokenSource?.Dispose();

            _logger.End();

            return Result.Value;
        }

        public void SetVariable(string name, string value, bool isSecret = false, bool isOutput = false, bool isFilePath = false, bool isReadOnly = false)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));

            if (isOutput || OutputVariables.Contains(name))
            {
                _record.Variables[name] = new VariableValue()
                {
                    Value = value,
                    IsSecret = isSecret
                };
                _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);

                ArgUtil.NotNullOrEmpty(_record.RefName, nameof(_record.RefName));
                Variables.Set($"{_record.RefName}.{name}", value, secret: isSecret, readOnly: (isOutput || isReadOnly));
            }
            else
            {
                Variables.Set(name, value, secret: isSecret, readOnly: isReadOnly);
            }
        }

        public void SetTimeout(TimeSpan? timeout)
        {
            if (timeout != null)
            {
                _cancellationTokenSource.CancelAfter(timeout.Value);
            }
        }

        public void Progress(int percentage, string currentOperation = null)
        {
            if (percentage > 100 || percentage < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(percentage));
            }

            _record.CurrentOperation = currentOperation ?? _record.CurrentOperation;
            _record.PercentComplete = Math.Max(percentage, _record.PercentComplete.Value);

            _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);
        }

        // This is not thread safe, the caller need to take lock before calling issue()
        public void AddIssue(Issue issue)
        {
            ArgUtil.NotNull(issue, nameof(issue));
            issue.Message = HostContext.SecretMasker.MaskSecrets(issue.Message);

            if (issue.Type == IssueType.Error)
            {
                // tracking line number for each issue in log file
                // log UI use this to navigate from issue to log
                if (!string.IsNullOrEmpty(issue.Message))
                {
                    long logLineNumber = Write(WellKnownTags.Error, issue.Message);
                    issue.Data["logFileLineNumber"] = logLineNumber.ToString();
                }

                if (_record.ErrorCount < _maxIssueCount)
                {
                    _record.Issues.Add(issue);
                }

                _record.ErrorCount++;
            }
            else if (issue.Type == IssueType.Warning)
            {
                // tracking line number for each issue in log file
                // log UI use this to navigate from issue to log
                if (!string.IsNullOrEmpty(issue.Message))
                {
                    long logLineNumber = Write(WellKnownTags.Warning, issue.Message);
                    issue.Data["logFileLineNumber"] = logLineNumber.ToString();
                }

                if (_record.WarningCount < _maxIssueCount)
                {
                    _record.Issues.Add(issue);
                }

                _record.WarningCount++;
            }

            _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);
        }

        public void UpdateDetailTimelineRecord(TimelineRecord record)
        {
            ArgUtil.NotNull(record, nameof(record));

            if (record.RecordType == ExecutionContextType.Job)
            {
                throw new ArgumentOutOfRangeException(nameof(record));
            }

            if (_detailTimelineId == Guid.Empty)
            {
                // create detail timeline
                _detailTimelineId = Guid.NewGuid();
                _record.Details = new Timeline(_detailTimelineId);

                _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);
            }

            TimelineRecord existRecord;
            if (_detailRecords.TryGetValue(record.Id, out existRecord))
            {
                existRecord.Name = record.Name ?? existRecord.Name;
                existRecord.RecordType = record.RecordType ?? existRecord.RecordType;
                existRecord.Order = record.Order ?? existRecord.Order;
                existRecord.ParentId = record.ParentId ?? existRecord.ParentId;
                existRecord.StartTime = record.StartTime ?? existRecord.StartTime;
                existRecord.FinishTime = record.FinishTime ?? existRecord.FinishTime;
                existRecord.PercentComplete = record.PercentComplete ?? existRecord.PercentComplete;
                existRecord.CurrentOperation = record.CurrentOperation ?? existRecord.CurrentOperation;
                existRecord.Result = record.Result ?? existRecord.Result;
                existRecord.ResultCode = record.ResultCode ?? existRecord.ResultCode;
                existRecord.State = record.State ?? existRecord.State;

                _jobServerQueue.QueueTimelineRecordUpdate(_detailTimelineId, existRecord);
            }
            else
            {
                _detailRecords[record.Id] = record;
                _jobServerQueue.QueueTimelineRecordUpdate(_detailTimelineId, record);
            }
        }

        public void InitializeJob(Pipelines.AgentJobRequestMessage message, CancellationToken token)
        {
            // Validation
            Trace.Entering();
            ArgUtil.NotNull(message, nameof(message));
            ArgUtil.NotNull(message.Resources, nameof(message.Resources));
            ArgUtil.NotNull(message.Variables, nameof(message.Variables));
            ArgUtil.NotNull(message.Plan, nameof(message.Plan));

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

            // Features
            Features = PlanUtil.GetFeatures(message.Plan);

            // Endpoints
            Endpoints = message.Resources.Endpoints;

            // SecureFiles
            SecureFiles = message.Resources.SecureFiles;

            // Repositories
            Repositories = message.Resources.Repositories;

            // JobSettings
            var checkouts = message.Steps?.Where(x => Pipelines.PipelineConstants.IsCheckoutTask(x)).ToList();
            JobSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JobSettings[WellKnownJobSettings.HasMultipleCheckouts] = Boolean.FalseString;
            if (checkouts != null && checkouts.Count > 0)
            {
                JobSettings[WellKnownJobSettings.HasMultipleCheckouts] = checkouts.Count > 1 ? Boolean.TrueString : Boolean.FalseString;
                var firstCheckout = checkouts.First() as Pipelines.TaskStep;
                if (firstCheckout != null && Repositories != null && firstCheckout.Inputs.TryGetValue(Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, out string repoAlias))
                {
                    JobSettings[WellKnownJobSettings.FirstRepositoryCheckedOut] = repoAlias;
                    var repo = Repositories.Find(r => String.Equals(r.Alias, repoAlias, StringComparison.OrdinalIgnoreCase));
                    if (repo != null)
                    {
                        repo.Properties.Set<bool>(RepositoryUtil.IsPrimaryRepository, true);
                    }
                }
            }

            bool canExpandVulnerableVariables = GetExpandVulnerableVariablesValue(message);
            // Variables (constructor performs initial recursive expansion)
            Variables = new Variables(HostContext, message.Variables, out List<string> warnings, canExpandVulnerableVariables)
            {
                StringTranslator = TranslatePathForStepTarget
            };

            if (Variables.GetBoolean("agent.useWorkspaceId") == true)
            {
                try
                {
                    // We need an identifier that represents which repos make up the workspace.
                    // This allows similar jobs in the same definition to reuse that workspace and other jobs to have their own.
                    JobSettings[WellKnownJobSettings.WorkspaceIdentifier] = GetWorkspaceIdentifier(message);
                }
                catch (Exception ex)
                {
                    Trace.Warning($"Unable to generate workspace ID: {ex.Message}");
                }
            }

            // Prepend Path
            PrependPath = new List<string>();

            var minSecretLen = AgentKnobs.MaskedSecretMinLength.GetValue(this).AsInt();
            HostContext.SecretMasker.MinSecretLength = minSecretLen;

            if (HostContext.SecretMasker.MinSecretLength < minSecretLen)
            {
                warnings.Add(StringUtil.Loc("MinSecretsLengtLimitWarning", HostContext.SecretMasker.MinSecretLength));
            }

            HostContext.SecretMasker.RemoveShortSecretsFromDictionary();

            // Docker (JobContainer)
            string imageName = Variables.Get("_PREVIEW_VSTS_DOCKER_IMAGE");
            if (string.IsNullOrEmpty(imageName))
            {
                imageName = Environment.GetEnvironmentVariable("_PREVIEW_VSTS_DOCKER_IMAGE");
            }

            Containers = new List<ContainerInfo>();
            _defaultStepTarget = null;
            _currentStepTarget = null;
            if (!string.IsNullOrEmpty(imageName) &&
                string.IsNullOrEmpty(message.JobContainer))
            {
                var dockerContainer = new Pipelines.ContainerResource()
                {
                    Alias = "vsts_container_preview"
                };
                dockerContainer.Properties.Set("image", imageName);
                var defaultJobContainer = HostContext.CreateContainerInfo(dockerContainer);
                _defaultStepTarget = defaultJobContainer;
                Containers.Add(defaultJobContainer);
            }
            else if (!string.IsNullOrEmpty(message.JobContainer))
            {
                var defaultJobContainer = HostContext.CreateContainerInfo(message.Resources.Containers.Single(x => string.Equals(x.Alias, message.JobContainer, StringComparison.OrdinalIgnoreCase)));
                _defaultStepTarget = defaultJobContainer;
                Containers.Add(defaultJobContainer);
            }
            else
            {
                _defaultStepTarget = new HostInfo();
            }
            // Include other step containers
            var sidecarContainers = new HashSet<string>(message.JobSidecarContainers.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var container in message.Resources.Containers.Where(x =>
                !string.Equals(x.Alias, message.JobContainer, StringComparison.OrdinalIgnoreCase) && !sidecarContainers.Contains(x.Alias)))
            {
                Containers.Add(HostContext.CreateContainerInfo(container));
            }

            // Docker (Sidecar Containers)
            SidecarContainers = new List<ContainerInfo>();
            foreach (var sidecar in message.JobSidecarContainers)
            {
                var networkAlias = sidecar.Key;
                var containerResourceAlias = sidecar.Value;
                var containerResource = message.Resources.Containers.Single(c => string.Equals(c.Alias, containerResourceAlias, StringComparison.OrdinalIgnoreCase));
                ContainerInfo containerInfo = HostContext.CreateContainerInfo(containerResource, isJobContainer: false);
                containerInfo.ContainerNetworkAlias = networkAlias;
                SidecarContainers.Add(containerInfo);
            }

            // Proxy variables
            var agentWebProxy = HostContext.GetService<IVstsAgentWebProxy>();
            if (!string.IsNullOrEmpty(agentWebProxy.ProxyAddress))
            {
                Variables.Set(Constants.Variables.Agent.ProxyUrl, agentWebProxy.ProxyAddress);
                Environment.SetEnvironmentVariable("VSTS_HTTP_PROXY", string.Empty);

                if (!string.IsNullOrEmpty(agentWebProxy.ProxyUsername))
                {
                    Variables.Set(Constants.Variables.Agent.ProxyUsername, agentWebProxy.ProxyUsername);
                    Environment.SetEnvironmentVariable("VSTS_HTTP_PROXY_USERNAME", string.Empty);
                }

                if (!string.IsNullOrEmpty(agentWebProxy.ProxyPassword))
                {
                    Variables.Set(Constants.Variables.Agent.ProxyPassword, agentWebProxy.ProxyPassword, true);
                    Environment.SetEnvironmentVariable("VSTS_HTTP_PROXY_PASSWORD", string.Empty);
                }

                if (agentWebProxy.ProxyBypassList.Count > 0)
                {
                    Variables.Set(Constants.Variables.Agent.ProxyBypassList, JsonUtility.ToString(agentWebProxy.ProxyBypassList));
                }
            }

            // Certificate variables
            var agentCert = HostContext.GetService<IAgentCertificateManager>();
            if (agentCert.SkipServerCertificateValidation)
            {
                Variables.Set(Constants.Variables.Agent.SslSkipCertValidation, bool.TrueString);
            }

            if (!string.IsNullOrEmpty(agentCert.CACertificateFile))
            {
                Variables.Set(Constants.Variables.Agent.SslCAInfo, agentCert.CACertificateFile);
            }

            if (!string.IsNullOrEmpty(agentCert.ClientCertificateFile) &&
                !string.IsNullOrEmpty(agentCert.ClientCertificatePrivateKeyFile) &&
                !string.IsNullOrEmpty(agentCert.ClientCertificateArchiveFile))
            {
                Variables.Set(Constants.Variables.Agent.SslClientCert, agentCert.ClientCertificateFile);
                Variables.Set(Constants.Variables.Agent.SslClientCertKey, agentCert.ClientCertificatePrivateKeyFile);
                Variables.Set(Constants.Variables.Agent.SslClientCertArchive, agentCert.ClientCertificateArchiveFile);

                if (!string.IsNullOrEmpty(agentCert.ClientCertificatePassword))
                {
                    Variables.Set(Constants.Variables.Agent.SslClientCertPassword, agentCert.ClientCertificatePassword, true);
                }
            }

            // Runtime option variables
            var runtimeOptions = HostContext.GetService<IConfigurationStore>().GetAgentRuntimeOptions();
            if (runtimeOptions != null)
            {
                if (PlatformUtil.RunningOnWindows && runtimeOptions.GitUseSecureChannel)
                {
                    Variables.Set(Constants.Variables.Agent.GitUseSChannel, runtimeOptions.GitUseSecureChannel.ToString());
                }
            }

            // Job timeline record.
            InitializeTimelineRecord(
                timelineId: message.Timeline.Id,
                timelineRecordId: message.JobId,
                parentTimelineRecordId: null,
                recordType: ExecutionContextType.Job,
                displayName: message.JobDisplayName,
                refName: message.JobName,
                order: null); // The job timeline record's order is set by server.

            // Logger (must be initialized before writing warnings).
            _logger = HostContext.CreateService<IPagingLogger>();
            _logger.Setup(_mainTimelineId, _record.Id);

            // Log warnings from recursive variable expansion.
            warnings?.ForEach(x => this.Warning(x));

            // Verbosity (from system.debug).
            WriteDebug = Variables.System_Debug ?? false;

            // Hook up JobServerQueueThrottling event, we will log warning on server tarpit.
            _jobServerQueue.JobServerQueueThrottling += JobServerQueueThrottling_EventReceived;
        }

        // TODO: Remove this logic when we will decide to keep it for everyone.
        private bool GetExpandVulnerableVariablesValue(Pipelines.AgentJobRequestMessage message)
        {
            bool canExpandVulnerableVariables;

            message.Variables.TryGetValue("AZP_EXPAND_VULNERABLE_VARIABLES", out var expandVulnerableVariablesRuntimeVariable);

            if (expandVulnerableVariablesRuntimeVariable == null)
            {
                var knobValue = AgentKnobs.ExpandVulnerableVariables.GetValue<EnvironmentKnobSource>(this) ??
                                AgentKnobs.ExpandVulnerableVariables.GetValue<BuiltInDefaultKnobSource>(this);

                canExpandVulnerableVariables = knobValue.AsBooleanStrict();
            }
            else
            {
                if (!bool.TryParse(expandVulnerableVariablesRuntimeVariable.Value, out canExpandVulnerableVariables))
                {
                    var knobValue = AgentKnobs.ExpandVulnerableVariables.GetValue<EnvironmentKnobSource>(this) ??
                                    AgentKnobs.ExpandVulnerableVariables.GetValue<BuiltInDefaultKnobSource>(this);

                    canExpandVulnerableVariables = knobValue.AsBooleanStrict();
                }
            }

            return canExpandVulnerableVariables;
        }

        private string GetWorkspaceIdentifier(Pipelines.AgentJobRequestMessage message)
        {
            Variables.TryGetValue(Constants.Variables.System.CollectionId, out string collectionId);
            Variables.TryGetValue(Constants.Variables.System.DefinitionId, out string definitionId);
            var repoTrackingInfos = message.Resources.Repositories.Select(repo => new Build.RepositoryTrackingInfo(repo, "/")).ToList();
            var workspaceIdentifier = Build.TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, repoTrackingInfos);

            Trace.Info($"WorkspaceIdentifier '{workspaceIdentifier}' created for repos {String.Join(',', repoTrackingInfos)}");
            return workspaceIdentifier;
        }

        // Do not add a format string overload. In general, execution context messages are user facing and
        // therefore should be localized. Use the Loc methods from the StringUtil class. The exception to
        // the rule is command messages - which should be crafted using strongly typed wrapper methods.
        public long Write(string tag, string inputMessage, bool canMaskSecrets = true)
        {
            string message = canMaskSecrets ? HostContext.SecretMasker.MaskSecrets($"{tag}{inputMessage}") : inputMessage;

            long totalLines;
            lock (_loggerLock)
            {
                totalLines = _logger.TotalLines + 1;

                if (_disableLogUploads)
                {
                    _buildLogsWriter.WriteLine(message);
                }
                else
                {
                    _logger.Write(message);
                }
            }

            if (!_disableLogUploads)
            {
                // write to job level execution context's log file.
                var parentContext = _parentExecutionContext as ExecutionContext;
                if (parentContext != null)
                {
                    lock (parentContext._loggerLock)
                    {
                        parentContext._logger.Write(message);
                    }
                }

                _jobServerQueue.QueueWebConsoleLine(_record.Id, message, totalLines);
            }

            // write to plugin daemon,
            if (_outputForward)
            {
                if (_logPlugin == null)
                {
                    _logPlugin = HostContext.GetService<IAgentLogPlugin>();
                }

                _logPlugin.Write(_record.Id, message);
            }

            return totalLines;
        }

        public void QueueAttachFile(string type, string name, string filePath)
        {
            ArgUtil.NotNullOrEmpty(type, nameof(type));
            ArgUtil.NotNullOrEmpty(name, nameof(name));
            ArgUtil.NotNullOrEmpty(filePath, nameof(filePath));

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(StringUtil.Loc("AttachFileNotExist", type, name, filePath));
            }

            _jobServerQueue.QueueFileUpload(_mainTimelineId, _record.Id, type, name, filePath, deleteSource: false);
        }

        public ITraceWriter GetTraceWriter()
        {
            return Trace;
        }

        private void InitializeTimelineRecord(Guid timelineId, Guid timelineRecordId, Guid? parentTimelineRecordId, string recordType, string displayName, string refName, int? order)
        {
            _mainTimelineId = timelineId;
            _record.Id = timelineRecordId;
            _record.RecordType = recordType;
            _record.Name = displayName;
            _record.RefName = refName;
            _record.Order = order;
            _record.PercentComplete = 0;
            _record.State = TimelineRecordState.Pending;
            _record.ErrorCount = 0;
            _record.WarningCount = 0;

            if (parentTimelineRecordId != null && parentTimelineRecordId.Value != Guid.Empty)
            {
                _record.ParentId = parentTimelineRecordId;
            }

            var configuration = HostContext.GetService<IConfigurationStore>();
            _record.WorkerName = configuration.GetSettings().AgentName;

            _jobServerQueue.QueueTimelineRecordUpdate(_mainTimelineId, _record);
        }

        private void JobServerQueueThrottling_EventReceived(object sender, ThrottlingEventArgs data)
        {
            Interlocked.Add(ref _totalThrottlingDelayInMilliseconds, Convert.ToInt64(data.Delay.TotalMilliseconds));

            if (!_throttlingReported)
            {
                this.Warning(StringUtil.Loc("ServerTarpit"));

                if (!String.IsNullOrEmpty(this.Variables.System_TFCollectionUrl))
                {
                    // Construct a URL to the resource utilization page, to aid the user debug throttling issues
                    UriBuilder uriBuilder = new UriBuilder(Variables.System_TFCollectionUrl);
                    NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);
                    DateTime endTime = DateTime.UtcNow;
                    string queryDate = endTime.AddHours(-1).ToString("s") + "," + endTime.ToString("s");

                    uriBuilder.Path += (Variables.System_TFCollectionUrl.EndsWith("/") ? "" : "/") + "_usersSettings/usage";
                    query["tab"] = "pipelines";
                    query["queryDate"] = queryDate;

                    // Global RU link
                    uriBuilder.Query = query.ToString();
                    string global = StringUtil.Loc("ServerTarpitUrl", uriBuilder.ToString());

                    if (!String.IsNullOrEmpty(this.Variables.Build_DefinitionName))
                    {
                        query["keywords"] = this.Variables.Build_Number;
                        query["definition"] = this.Variables.Build_DefinitionName;
                    }
                    else if (!String.IsNullOrEmpty(this.Variables.Release_ReleaseName))
                    {
                        query["keywords"] = this.Variables.Release_ReleaseId;
                        query["definition"] = this.Variables.Release_ReleaseName;
                    }

                    // RU link scoped for the build/release
                    uriBuilder.Query = query.ToString();
                    this.Warning($"{global}\n{StringUtil.Loc("ServerTarpitUrlScoped", uriBuilder.ToString())}");
                }

                _throttlingReported = true;
            }
        }

        public string TranslateToHostPath(string path)
        {
            var stepTarget = StepTarget();
            if (stepTarget != null)
            {
                return stepTarget.TranslateToHostPath(path);
            }
            return path;
        }

        public string TranslatePathForStepTarget(string val)
        {
            var stepTarget = StepTarget();
            var isCheckoutType = Convert.ToBoolean(this.Variables.Get(Constants.Variables.Task.SkipTranslatorForCheckout, true));
            if (stepTarget == null || (isCheckoutType && (_currentStepTarget == null || stepTarget is HostInfo)))
            {
                return val;
            }
            return stepTarget.TranslateContainerPathForImageOS(PlatformUtil.HostOS, stepTarget.TranslateToContainerPath(val));
        }

        public ExecutionTargetInfo StepTarget()
        {
            if (_currentStepTarget != null)
            {
                return _currentStepTarget;
            }

            return _defaultStepTarget;
        }

        public void SetStepTarget(Pipelines.StepTarget target)
        {
            // When step targets are set, we need to take over control for translating paths
            // from the job execution context
            Variables.StringTranslator = TranslatePathForStepTarget;

            if (string.Equals(WellKnownStepTargetStrings.Host, target?.Target, StringComparison.OrdinalIgnoreCase))
            {
                _currentStepTarget = new HostInfo();
            }
            else
            {
                _currentStepTarget = Containers.FirstOrDefault(x => string.Equals(x.ContainerName, target?.Target, StringComparison.OrdinalIgnoreCase));
            }
        }

        public string GetVariableValueOrDefault(string variableName)
        {
            string value = null;
            Variables.TryGetValue(variableName, out value);
            return value;
        }

        public IScopedEnvironment GetScopedEnvironment()
        {
            return new SystemEnvironment();
        }

        public void ReInitializeForceCompleted()
        {
            this._forceCompleted = new TaskCompletionSource<int>();
            this._forceCompleteCancellationTokenSource = new CancellationTokenSource();
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Dispose();
            _forceCompleteCancellationTokenSource?.Dispose();

            _buildLogsWriter?.Dispose();
            _buildLogsWriter = null;
            _buildLogsData?.Dispose();
            _buildLogsData = null;
        }
    }

    // The Error/Warning/etc methods are created as extension methods to simplify unit testing.
    // Otherwise individual overloads would need to be implemented (depending on the unit test).
    public static class ExecutionContextExtension
    {
        public static void Error(this IExecutionContext context, Exception ex)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(ex, nameof(ex));

            context.Error(ex.Message);
            context.Debug(ex.ToString());
        }

        // Do not add a format string overload. See comment on ExecutionContext.Write().
        public static void Error(this IExecutionContext context, string message)
        {
            ArgUtil.NotNull(context, nameof(context));
            context.AddIssue(new Issue() { Type = IssueType.Error, Message = message });
        }

        // Do not add a format string overload. See comment on ExecutionContext.Write().
        public static void Warning(this IExecutionContext context, string message)
        {
            ArgUtil.NotNull(context, nameof(context));
            context.AddIssue(new Issue() { Type = IssueType.Warning, Message = message });
        }

        // Do not add a format string overload. See comment on ExecutionContext.Write().
        public static void Output(this IExecutionContext context, string message, bool canMaskSecrets = true)
        {
            ArgUtil.NotNull(context, nameof(context));
            context.Write(null, message, canMaskSecrets);
        }

        // Do not add a format string overload. See comment on ExecutionContext.Write().
        public static void Command(this IExecutionContext context, string message)
        {
            ArgUtil.NotNull(context, nameof(context));
            context.Write(WellKnownTags.Command, message);
        }

        // Do not add a format string overload. See comment on ExecutionContext.Write().
        public static void Section(this IExecutionContext context, string message)
        {
            ArgUtil.NotNull(context, nameof(context));
            context.Write(WellKnownTags.Section, message);
        }

        //
        // Verbose output is enabled by setting System.Debug
        // It's meant to help the end user debug their definitions.
        // Why are my inputs not working?  It's not meant for dev debugging which is diag
        //
        // Do not add a format string overload. See comment on ExecutionContext.Write().
        public static void Debug(this IExecutionContext context, string message)
        {
            ArgUtil.NotNull(context, nameof(context));
            if (context.WriteDebug)
            {
                context.Write(WellKnownTags.Debug, message);
            }
        }
    }

    public static class WellKnownTags
    {
        public static readonly string Section = "##[section]";
        public static readonly string Command = "##[command]";
        public static readonly string Error = "##[error]";
        public static readonly string Warning = "##[warning]";
        public static readonly string Debug = "##[debug]";
    }

    public static class WellKnownStepTargetStrings
    {
        public static readonly string Host = "host";
        public static readonly string Restricted = "restricted";
    }
}
