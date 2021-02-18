using System;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    internal class RetryHelper
    {
        public RetryHelper(IExecutionContext executionContext, int maxRetries = 3)
        {
            Debug = (str) => executionContext.Debug(str);
            Warning = (str) => executionContext.Warning(str);
            MaxRetries = maxRetries;
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
                        if (!shouldRetryOnException(ex) || ExhaustedRetryCount(retryCounter))
                        {
                            throw;
                        }

                        Warning($"Intermittent failure attempting to call the restapis {action.Method}. Retry attempt {retryCounter}. Exception: {ex.Message} ");
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
                Debug($"Failure attempting to call the restapi and retry counter is exhausted");
                return true;
            }
            return false;
        }

        private readonly int MaxRetries;
        private readonly Action<string> Debug;
        private readonly Action<string> Warning;
    }
}
