// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(TaskDecoratorManager))]
    public interface ITaskDecoratorManager : IAgentService
    {
        bool IsInjectedTaskForTarget(string taskName, IExecutionContext executionContext);
        bool IsInjectedInputsContainsSecrets(Dictionary<string, string> inputs, out List<string> inputsWithSecrets);
        string GenerateTaskResultMessage(List<string> inputsWithSecrets);
    }

    public sealed class TaskDecoratorManager : AgentService, ITaskDecoratorManager
    {
        /// <summary>
        /// Checks if current task is injected by decorator with posttargettask or pretargettask target.
        /// TaskName will be null on old versions of TFS 2017, 2015, this version of TFS doesn't support injection of post-target and pre-target decorators,
        /// so we could just return false value in case of null taskName
        /// </summary>
        /// <param name="taskName">Name of the task to check</param>
        /// <returns>Returns `true` if task is injected by decorator for target task, otherwise `false`</returns>
        public bool IsInjectedTaskForTarget(string taskName, IExecutionContext executionContext)
        {
            if (taskName == null)
            {
                executionContext.Debug("The task name is null, check for the target of injected tasks skipped.");
                return false;
            }

            return taskName.StartsWith(InjectedTasksNamesPrefixes.PostTargetTask)
                    || taskName.StartsWith(InjectedTasksNamesPrefixes.PreTargetTask);
        }

        /// <summary>
        /// Verifies that there are inputs with secrets, if secrets were found task will be marked as skipped and won't be executed
        /// </summary>
        /// <param name="inputs">Inputs presented as a dictionary with input name as key and input's value as the value of the corresponding key</param>
        /// <param name="inputsWithSecrets">Out value that will contain the list of task inputs with secrets</param>
        /// <returns> Return `true` if task contains injected inputs with secrets, otherwise `false`</returns>
        public bool IsInjectedInputsContainsSecrets(Dictionary<string, string> inputs, out List<string> inputsWithSecrets)
        {
            inputsWithSecrets = this.GetInputsWithSecrets(inputs);

            return inputsWithSecrets.Count > 0;
        }

        /// <summary>
        /// Generates list of inputs that should be included into task result message
        /// </summary>
        /// <param name="inputsWithSecrets">List of inputs with secrets, that should be included in message</param>
        public string GenerateTaskResultMessage(List<string> inputsWithSecrets)
        {
            string inputsForReport = string.Join(Environment.NewLine,
                inputsWithSecrets.Select(input => string.Join("\n", input)));

            return inputsForReport;
        }

        /// <summary>
        /// Used to check if provided input value contain any secret
        /// </summary>
        /// <param name="inputValue">Value of input to check</param>
        /// <returns>Returns `true` if provided string contain secret, otherwise `false`</returns>
        private bool ContainsSecret(string inputValue)
        {
            string maskedString = HostContext.SecretMasker.MaskSecrets(inputValue);
            return maskedString != inputValue;
        }

        /// <summary>
        /// Used to get list of inputs in injected task that started with target_ prefix and contain secrets,
        /// such inputs are autoinjected from target tasks
        /// </summary>
        /// <param name="inputs">Inputs presented as a dictionary with input name as key and input's value as the value of the corresponding key</param>
        /// <returns>Returns list of inputs' names that contain secret values</returns>
        private List<string> GetInputsWithSecrets(Dictionary<string, string> inputs)
        {
            var inputsWithSecrets = new List<string>();
            foreach (var input in inputs)
            {
                if (input.Key.StartsWith("target_") && this.ContainsSecret(input.Value))
                {
                    inputsWithSecrets.Add(input.Key);
                }
            }

            return inputsWithSecrets;
        }
    }

    internal static class InjectedTasksNamesPrefixes
    {
        public static readonly String PostTargetTask = "__system_posttargettask_";
        public static readonly String PreTargetTask = "__system_pretargettask_";
        public static readonly String InjectedTaskCommon = "__system_";
    }
}
