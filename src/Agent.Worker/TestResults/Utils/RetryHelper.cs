using System;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    internal class RetryHelper
    {
        public RetryHelper(IExecutionContext executionContext, int maxRetries = 3)
        {
            ExecutionContext = executionContext;
            MaxRetries = maxRetries;
        }

        public async Task<T> Retry<T>(Func<Task<T>> action, Func<int, int> timeDelayInterval, Func<Exception, bool> shouldRetryOnException)
        {
            int retryCounter = 0;
            do
            {
                using (new SimpleTimer($"RetryHelper Method:{action.Method} ", ExecutionContext))
                {
                    try
                    {
                        ExecutionContext.Debug($"Invoking Method: {action.Method}. Attempt count: {retryCounter}");
                        return await action();
                    }
                    catch (Exception ex)
                    {
                        if (!shouldRetryOnException(ex) || ExhaustedRetryCount(retryCounter))
                        {
                            throw;
                        }

                        ExecutionContext.Warning($"Intermittent failure attempting to call the restapis {action.Method}. Retry attempt {retryCounter}. Exception: {ex.Message} ");
                        var delay = timeDelayInterval(retryCounter);
                        await Task.Delay(delay);
                    }
                    retryCounter++;
                }
            } while (true);

        }

        private bool ExhaustedRetryCount(int retryCount)
        {
            if (retryCount >= MaxRetries)
            {
                ExecutionContext.Debug($"Failure attempting to call the restapi and retry counter is exhausted");
                return true;
            }
            return false;
        }

        private readonly int MaxRetries;
        private readonly IExecutionContext ExecutionContext;
    }
}
