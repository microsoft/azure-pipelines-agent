# YAML getting started - Job options

## Continue on error

When `continueOnError` is set to true and the job fails, the job result will be \"Succeeded with issues\" instead of "Failed\".

## Timeout

The `timeoutInMinutes` allows a limit to be set for the job execution time. When not specified, the default is 60 minutes.

The `cancelTimeoutInMinutes` allows a limit to be set for the job cancel time. When not specified, the default is 5 minutes.

## Variables

Variables can be specified on a job. Refer [here](yamlgettingstarted.md#Variables) for more information about variables.
