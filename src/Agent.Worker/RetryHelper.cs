using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    internal class RetryHelper
    {
        /// <summary>
        /// Returns exponential delay - depending on number of retry
        /// Considers that retryNumber starts from 0
        /// Initial delay - 1 second
        /// </summary>
        /// <returns></returns>
        public static int ExponentialDelay(int retryNumber)
        {
            return (int)(Math.Pow(retryNumber + 1, 2) * 1000);
        }

        public RetryHelper(IExecutionContext executionContext, int maxRetries = 3)
        {
            Debug = (str) => executionContext.Debug(str);
            Warning = (str) => executionContext.Warning(str);
            MaxRetries = maxRetries;
            ExecutionContext = executionContext;
        }

        public RetryHelper(IAsyncCommandContext commandContext, int maxRetries = 3)
        {
            Debug = (str) => commandContext.Debug(str);
            Warning = (str) => commandContext.Output(str);
            MaxRetries = maxRetries;
        }

        public async Task<T> Retry<T>(Func<Task<T>> action, Func<int, int> timeDelayInterval, Func<Exception, bool> shouldRetryOnException)
        {
            int retryCounter = 0;
            do
            {
                using (new SimpleTimer($"RetryHelper Method:{action.Method} ", Debug))
                {
                    try
                    {
                        Debug($"Invoking Method: {action.Method}. Attempt count: {retryCounter}");
                        return await action();
                    }
                    catch (Exception ex)
                    {
                        if (!shouldRetryOnException(ex) || ExhaustedRetryCount(retryCounter, action.Method))
                        {
                            throw;
                        }

                        Warning($"Intermittent failure attempting to call the method {action.Method}. Retry attempt {retryCounter}. Exception: {ex.Message} ");
                        var delay = timeDelayInterval(retryCounter);
                        await Task.Delay(delay);
                    }
                    retryCounter++;
                }
            } while (true);
        }

        /// <summary>
        /// Runs action with maxRetries number of retries
        /// </summary>
        /// <param name="action">Action to execute with retries</param>
        /// <param name="timeDelayInterval">Function to calculate delay between retries depending on retry number. Should take retry number as argument and consider that it starts from 0.</param>
        /// <returns></returns>
        public async Task RetryStep(Func<Task> action, Func<int, int> timeDelayInterval)
        {
            int retryCounter = 0;
            do
            {
                using (new SimpleTimer($"RetryHelper Method:{action.Method} ", Debug))
                {
                    var delayInterval = timeDelayInterval(retryCounter);
                    try
                    {
                        if (retryCounter > 0)
                        {
                            //ReInitialize _forceCompleted and _forceCompleteCancellationTokenSource
                            ExecutionContext.ReInitializeForceCompleted();
                        }

                        Debug($"Invoking Method: {action.Method}. Attempt count: {retryCounter}");
                        await action();

                        if (ExecutionContext.Result != TaskResult.Failed || ExhaustedRetryCount(retryCounter, action.Method))
                        {
                            return;
                        }
                        else
                        {
                            string exceptionMessage = $"Task result {ExecutionContext.Result}";
                            ExecutionContext.Result = null;
                            Warning($"RetryHelper encountered task failure, will retry (attempt #: {retryCounter + 1} out of {this.MaxRetries}) after {delayInterval} ms");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!ShouldRetryStepOnException(ex) || ExhaustedRetryCount(retryCounter, action.Method))
                        {
                            throw;
                        }
                        Warning($"RetryHelper encountered exception, will retry (attempt #: {retryCounter + 1} {ex.Message}) afer {delayInterval} ms");
                    }
                    //Cancel force task completion before the next attempt
                    ExecutionContext.CancelForceTaskCompletion();

                    await Task.Delay(timeDelayInterval(retryCounter), ExecutionContext.CancellationToken);
                    retryCounter++;
                }
            } while (true);
        }

        private bool ExhaustedRetryCount(int retryCount, MethodInfo methodInfo)
        {
            if (retryCount >= MaxRetries)
            {
                Debug($"Failure attempting to call the {methodInfo} and retry counter is exhausted");
                return true;
            }
            return false;
        }

        private bool ShouldRetryStepOnException(Exception exception)
        {
            return !(exception is TimeoutException) && !(exception is OperationCanceledException);
        }

        private readonly int MaxRetries;
        private readonly Action<string> Debug;
        private readonly Action<string> Warning;
        private readonly IExecutionContext ExecutionContext;
    }
}
