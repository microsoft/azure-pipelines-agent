// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(AsyncCommandContext))]
    public interface IAsyncCommandContext : IAgentService, IKnobValueContext
    {
        string Name { get; }
        Task Task { get; set; }
        void InitializeCommandContext(IExecutionContext context, string name);
        void Output(string message);
        void Debug(string message);
        void Warn(string message);
        Task WaitAsync();
        IHostContext GetHostContext();
    }

    public class AsyncCommandContext : AgentService, IAsyncCommandContext
    {
        private class OutputMessage
        {
            public OutputMessage(OutputType type, string message)
            {
                Type = type;
                Message = message;
            }

            public OutputType Type { get; }
            public String Message { get; }
        }

        private enum OutputType
        {
            Info,
            Debug,
            Warning
        }

        private IExecutionContext _executionContext;
        private readonly ConcurrentQueue<OutputMessage> _outputQueue = new ConcurrentQueue<OutputMessage>();

        public string Name { get; private set; }
        public Task Task { get; set; }

        public void InitializeCommandContext(IExecutionContext context, string name)
        {
            _executionContext = context;
            Name = name;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721: Property names should not match get methods")]
        public IHostContext GetHostContext()
        {
            return _executionContext.GetHostContext();
        }

        public void Output(string message)
        {
            _outputQueue.Enqueue(new OutputMessage(OutputType.Info, message));
        }

        public void Debug(string message)
        {
            _outputQueue.Enqueue(new OutputMessage(OutputType.Debug, message));
        }

        public void Warn(string message)
        {
            _outputQueue.Enqueue(new OutputMessage(OutputType.Warning, message));
        }

        public async Task WaitAsync()
        {
            Trace.Entering();
            // start flushing output queue
            Trace.Info("Start flush buffered output.");
            _executionContext.Section($"Async Command Start: {Name}");
            OutputMessage output;
            while (!this.Task.IsCompleted)
            {
                while (_outputQueue.TryDequeue(out output))
                {
                    switch (output.Type)
                    {
                        case OutputType.Info:
                            _executionContext.Output(output.Message);
                            break;
                        case OutputType.Debug:
                            _executionContext.Debug(output.Message);
                            break;
                        case OutputType.Warning:
                            _executionContext.Warning(output.Message);
                            break;
                    }
                }

                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(500)), this.Task);
            }

            // Dequeue one more time make sure all outputs been flush out.
            Trace.Verbose("Command task has finished, flush out all remaining buffered output.");
            while (_outputQueue.TryDequeue(out output))
            {
                switch (output.Type)
                {
                    case OutputType.Info:
                        _executionContext.Output(output.Message);
                        break;
                    case OutputType.Debug:
                        _executionContext.Debug(output.Message);
                        break;
                    case OutputType.Warning:
                        _executionContext.Warning(output.Message);
                        break;
                }
            }

            _executionContext.Section($"Async Command End: {Name}");
            Trace.Info("Finsh flush buffered output.");

            // wait for the async command task
            Trace.Info("Wait till async command task to finish.");
            await Task;
        }

        string IKnobValueContext.GetVariableValueOrDefault(string variableName)
        {
            return _executionContext.Variables.Get(variableName);
        }

        IScopedEnvironment IKnobValueContext.GetScopedEnvironment()
        {
            return new SystemEnvironment();
        }
    }
}
