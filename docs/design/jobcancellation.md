
# Agent jobs cancellation

Agent receives cancellation signal from server - which initiates job cancellation process.

## How agent cancells job execution in details?

When agent receives cancellation signal from server (this usually happens when job execution is timed out, or it was cancelled manually by user) - it sends SIGINT signal to the child process responsible for task execution.

If child process has been successfully stopped by SIGINT signal - agent considers that task has been cancelled; otherwise, the agent will send SIGTERM signal to this process.

You can find relevant source code [here](https://github.com/microsoft/azure-pipelines-agent/blob/master/src/Agent.Sdk/ProcessInvoker.cs#L418).
