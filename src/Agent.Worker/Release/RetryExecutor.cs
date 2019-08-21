using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release
{
    public class RetryExecutor
    {
        private const int DefaultMaximumRetryCount = 5;

        private const int DefaultMillisecondsToSleepBetweenRetries = 1000;

        public int MaximumRetryCount { get; set; }

        public int MillisecondsToSleepBetweenRetries { get; set; }

        public Func<Exception, bool> ShouldRetryAction { get; set; }

        protected Func<int, Task> SleepAction { get; set; }

        public RetryExecutor()
        {
            MaximumRetryCount = DefaultMaximumRetryCount;
            MillisecondsToSleepBetweenRetries = DefaultMillisecondsToSleepBetweenRetries;
            ShouldRetryAction = ex => true;
            SleepAction = i => Task.Delay(i);
        }

        public void Execute(Action action)
        {
            ArgUtil.NotNull(action, nameof(action));

            for (var retryCount = 0; retryCount < MaximumRetryCount; retryCount++)
            {
                try
                {
                    action();
                    break;
                }
                catch (Exception ex)
                {
                    if (retryCount == MaximumRetryCount - 1 || !ShouldRetryAction(ex))
                    {
                        throw;
                    }

                    SleepAction(MillisecondsToSleepBetweenRetries * Convert.Toint32(Math.Pow(2, retryCount))).Wait();
                }
            }
        }

        public async Task ExecuteAsync(Func<Task> action)
        {
            ArgUtil.NotNull(action, nameof(action));

            for (var retryCount = 0; retryCount < MaximumRetryCount; retryCount++)
            {
                try
                {
                    await action();
                    break;
                }
                catch (Exception ex)
                {
                    if (retryCount == MaximumRetryCount - 1 || !ShouldRetryAction(ex))
                    {
                        throw;
                    }

                    await SleepAction(MillisecondsToSleepBetweenRetries * Convert.ToInt32(Math.Pow(2, retryCount)));
                }
            }
        }

        public async Task<TResult> ExecuteAsync<T, TResult>(Func<T, Task<TResult>> action, T parameter)
        {
            ArgUtil.NotNull(action, nameof(action));

            var result = default(TResult);
            await ExecuteAsync(async () => result = await action(parameter));
            return result;
        }
    }
}