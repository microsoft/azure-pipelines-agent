// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;
using Agent.Sdk.Util;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(Worker))]
    public interface IWorker : IAgentService
    {
        Task<int> RunAsync(string pipeIn, string pipeOut);
    }

    public sealed class Worker : AgentService, IWorker
    {
        private readonly TimeSpan _workerStartTimeout = TimeSpan.FromSeconds(30);
        private static readonly char[] _quoteLikeChars = new char[] { '\'', '"' };


        public async Task<int> RunAsync(string pipeIn, string pipeOut)
        {
            // Validate args.
            ArgUtil.NotNullOrEmpty(pipeIn, nameof(pipeIn));
            ArgUtil.NotNullOrEmpty(pipeOut, nameof(pipeOut));
            Trace.Entering();
            var agentWebProxy = HostContext.GetService<IVstsAgentWebProxy>();
            var agentCertManager = HostContext.GetService<IAgentCertificateManager>();
            VssUtil.InitializeVssClientSettings(HostContext.UserAgent, agentWebProxy.WebProxy, agentCertManager.VssClientCertificateManager, agentCertManager.SkipServerCertificateValidation);
            Trace.Info("VSS client settings initialized [UserAgent:{0}, ProxyConfigured:{1}, CertValidationSkipped:{2}]", 
                HostContext.UserAgent, agentWebProxy.WebProxy != null, agentCertManager.SkipServerCertificateValidation);

            var jobRunner = HostContext.CreateService<IJobRunner>();
            Trace.Info("JobRunner service created - preparing for IPC channel establishment");

            using (var channel = HostContext.CreateService<IProcessChannel>())
            using (var jobRequestCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(HostContext.AgentShutdownToken))
            using (var channelTokenSource = new CancellationTokenSource())
            {
                // Start the channel.
                Trace.Info("Starting process channel client - establishing IPC communication with listener - pipeIn: {0}, pipeOut: {1}", pipeIn, pipeOut);
                channel.StartClient(pipeIn, pipeOut);
                Trace.Info("IPC channel established successfully - communication link active with listener process");

                // Wait for up to 30 seconds for a message from the channel.
                Trace.Info("Process channel established - waiting for job message from listener process");
                HostContext.WritePerfCounter("WorkerWaitingForJobMessage");
                WorkerMessage channelMessage;
                using (var csChannelMessage = new CancellationTokenSource(_workerStartTimeout))
                {
                    channelMessage = await channel.ReceiveAsync(csChannelMessage.Token);
                }

                // Deserialize the job message.
                Trace.Info("Job message received from listener - beginning deserialization and validation");
                ArgUtil.Equal(MessageType.NewJobRequest, channelMessage.MessageType, nameof(channelMessage.MessageType));
                ArgUtil.NotNullOrEmpty(channelMessage.Body, nameof(channelMessage.Body));
                var jobMessage = JsonUtility.FromString<Pipelines.AgentJobRequestMessage>(channelMessage.Body);
                ArgUtil.NotNull(jobMessage, nameof(jobMessage));
                HostContext.WritePerfCounter($"WorkerJobMessageReceived_{jobMessage.RequestId.ToString()}");

                Trace.Info("Job message deserialized successfully [JobId:{0}, PlanId:{1}, RequestId:{2}]",
                    jobMessage.JobId, jobMessage.Plan.PlanId, jobMessage.RequestId);
                jobMessage = WorkerUtilities.DeactivateVsoCommandsFromJobMessageVariables(jobMessage);

                // Initialize the secret masker and set the thread culture.
                InitializeSecretMasker(jobMessage);
                SetCulture(jobMessage);

                // Start the job.
                Trace.Info("Job preprocessing complete - starting JobRunner execution with detailed message logging");
                Trace.Info($"Job message:{Environment.NewLine} {StringUtil.ConvertToJson(WorkerUtilities.ScrubPiiData(jobMessage))}");
                Task<TaskResult> jobRunnerTask = jobRunner.RunAsync(jobMessage, jobRequestCancellationToken.Token);

                Trace.Info("Entering message monitoring loop - listening for cancellation and shutdown signals");
                bool cancel = false;
                int messageLoopIteration = 0;
                while (!cancel)
                {
                    messageLoopIteration++;
                    // Start listening for a cancel message from the channel.
                    Trace.Info("Starting listener for control messages from listener process [Iteration:{0}]", messageLoopIteration);
                    Task<WorkerMessage> channelTask = channel.ReceiveAsync(channelTokenSource.Token);

                    // Wait for one of the tasks to complete.
                    Trace.Info("Waiting for the job to complete or for a cancel message from the channel.");
                    await Task.WhenAny(jobRunnerTask, channelTask);

                    // Handle if the job completed.
                    if (jobRunnerTask.IsCompleted)
                    {
                        Trace.Info("Worker process termination initiated - Cancelling channel communication");
                        channelTokenSource.Cancel(); // Cancel waiting for a message from the channel.
                        var result = TaskResultUtil.TranslateToReturnCode(await jobRunnerTask);
                        Trace.Info($"JobRunner completion detected - Job execution finished with result: {result}");
                        return result;
                    }

                    // Otherwise a message was received from the channel.
                    channelMessage = await channelTask;
                    Trace.Info("Control message received from listener [Type:{0}, Iteration:{1}]", channelMessage.MessageType, messageLoopIteration);
                    switch (channelMessage.MessageType)
                    {
                        case MessageType.CancelRequest:
                            Trace.Info("Job cancellation request received - initiating graceful job termination");
                            cancel = true;
                            jobRequestCancellationToken.Cancel();   // Expire the host cancellation token.
                            break;
                        case MessageType.AgentShutdown:
                            Trace.Info("Agent shutdown request received - terminating job and shutting down worker");
                            cancel = true;
                            HostContext.ShutdownAgent(ShutdownReason.UserCancelled);
                            break;
                        case MessageType.OperatingSystemShutdown:
                            Trace.Info("Operating system shutdown detected - performing emergency job termination");
                            cancel = true;
                            HostContext.ShutdownAgent(ShutdownReason.OperatingSystemShutdown);
                            break;
                        case MessageType.JobMetadataUpdate:
                            Trace.Info("Metadata update message received - updating job runner metadata, Metadata: {0}", channelMessage.Body);
                            var metadataMessage = JsonUtility.FromString<JobMetadataMessage>(channelMessage.Body);
                            jobRunner.UpdateMetadata(metadataMessage);
                            Trace.Info("Job metadata update processed successfully");
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(channelMessage.MessageType), channelMessage.MessageType, nameof(channelMessage.MessageType));
                    }
                }

                // Await the job.
                var workerResult = TaskResultUtil.TranslateToReturnCode(await jobRunnerTask);
                Trace.Info($"Worker process lifecycle completed successfully - returning with exit code: {workerResult}");
                return workerResult;
            }
        }

        private void AddUserSuppliedSecret(String secret)
        {
            ArgUtil.NotNull(secret, nameof(secret));
            HostContext.SecretMasker.AddValue(secret, WellKnownSecretAliases.UserSuppliedSecret);
            // for variables, it is possible that they are used inside a shell which would strip off surrounding quotes
            // so, if the value is surrounded by quotes, add a quote-timmed version of the secret to our masker as well
            // This addresses issue #2525
            foreach (var quoteChar in _quoteLikeChars)
            {
                if (secret.StartsWith(quoteChar) && secret.EndsWith(quoteChar))
                {
                    HostContext.SecretMasker.AddValue(secret.Trim(quoteChar), WellKnownSecretAliases.UserSuppliedSecret);
                }
            }

            // Here we add a trimmed secret value to the dictionary in case of a possible leak through external tools.
            var trimChars = new char[] { '\r', '\n', ' ' };
            HostContext.SecretMasker.AddValue(secret.Trim(trimChars), WellKnownSecretAliases.UserSuppliedSecret);
        }

        private void InitializeSecretMasker(Pipelines.AgentJobRequestMessage message)
        {
            Trace.Entering();
            Trace.Info("Secret masker initialization initiated - processing job security configuration");
            ArgUtil.NotNull(message, nameof(message));
            ArgUtil.NotNull(message.Resources, nameof(message.Resources));
            int secretCount = 0;
            // Add mask hints for secret variables
            foreach (var variable in (message.Variables ?? new Dictionary<string, VariableValue>()))
            {
                // Skip secrets which are just white spaces.
                if (variable.Value.IsSecret && !string.IsNullOrWhiteSpace(variable.Value.Value))
                {
                    secretCount++;
                    AddUserSuppliedSecret(variable.Value.Value);
                    // also, we escape some characters for variables when we print them out in debug mode. We need to
                    // add the escaped version of these secrets as well
                    var escapedSecret = variable.Value.Value.Replace("%", "%AZP25")
                                                            .Replace("\r", "%0D")
                                                            .Replace("\n", "%0A");
                    AddUserSuppliedSecret(escapedSecret);

                    // Since % escaping may be turned off, also mask a version escaped with just newlines
                    var escapedSecret2 = variable.Value.Value.Replace("\r", "%0D")
                                                             .Replace("\n", "%0A");
                    AddUserSuppliedSecret(escapedSecret2);
                    // We need to mask the base 64 value of the secret as well
                    var base64Secret = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(variable.Value.Value));
                    // Add the base64 secret to the secret masker
                    AddUserSuppliedSecret(base64Secret);
                    // also, we escape some characters for variables when we print them out in debug mode. We need to
                    // add the escaped version of these secrets as well
                    var escapedSecret3 = base64Secret.Replace("%", "%AZP25")
                                                     .Replace("\r", "%0D")
                                                     .Replace("\n", "%0A");
                    AddUserSuppliedSecret(escapedSecret3);
                    // Since % escaping may be turned off, also mask a version escaped with just newlines
                    var escapedSecret4 = base64Secret.Replace("\r", "%0D")
                                                     .Replace("\n", "%0A");
                    AddUserSuppliedSecret(escapedSecret4);
                }
            }

            // Add mask hints
            foreach (MaskHint maskHint in (message.MaskHints ?? new List<MaskHint>()))
            {
                if (maskHint.Type == MaskType.Regex)
                {
                    HostContext.SecretMasker.AddRegex(maskHint.Value, $"Worker_{WellKnownSecretAliases.AddingMaskHint}");

                    // We need this because the worker will print out the job message JSON to diag log
                    // and SecretMasker has JsonEscapeEncoder hook up
                    HostContext.SecretMasker.AddValue(maskHint.Value, WellKnownSecretAliases.AddingMaskHint);
                }
                else
                {
                    // TODO: Should we fail instead? Do any additional pains need to be taken here? Should the job message not be traced?
                    Trace.Warning($"Unsupported mask type '{maskHint.Type}'.");
                }
            }

            // TODO: Avoid adding redundant secrets. If the endpoint auth matches the system connection, then it's added as a value secret and as a regex secret. Once as a value secret b/c of the following code that iterates over each endpoint. Once as a regex secret due to the hint sent down in the job message.

            // Add masks for service endpoints
            int endpointSecretCount = 0;
            foreach (ServiceEndpoint endpoint in message.Resources.Endpoints ?? new List<ServiceEndpoint>())
            {
                foreach (var keyValuePair in endpoint.Authorization?.Parameters ?? new Dictionary<string, string>())
                {
                    if (!string.IsNullOrEmpty(keyValuePair.Value) && MaskingUtil.IsEndpointAuthorizationParametersSecret(keyValuePair.Key))
                    {
                        endpointSecretCount++;
                        HostContext.SecretMasker.AddValue(keyValuePair.Value, $"Worker_EndpointAuthorizationParameters_{keyValuePair.Key}");
                    }
                }
            }

            // Add masks for secure file download tickets
            int secureFileCount = 0;
            foreach (SecureFile file in message.Resources.SecureFiles ?? new List<SecureFile>())
            {
                if (!string.IsNullOrEmpty(file.Ticket))
                {
                    secureFileCount++;
                    HostContext.SecretMasker.AddValue(file.Ticket, WellKnownSecretAliases.SecureFileTicket);
                }
            }
            Trace.Info("Secret masker initialization complete [SecretVariables:{0}, EndpointSecrets:{1}, SecureFiles:{2}]", 
                secretCount, endpointSecretCount, secureFileCount);
        }

        private void SetCulture(Pipelines.AgentJobRequestMessage message)
        {
            // Extract the culture name from the job's variable dictionary.
            // The variable does not exist for TFS 2015 RTM and Update 1.
            // It was introduced in Update 2.
            VariableValue culture;
            ArgUtil.NotNull(message, nameof(message));
            ArgUtil.NotNull(message.Variables, nameof(message.Variables));
            if (message.Variables.TryGetValue(Constants.Variables.System.Culture, out culture))
            {
                // Set the default thread culture.
                HostContext.SetDefaultCulture(culture.Value);
            }
        }
    }
}
