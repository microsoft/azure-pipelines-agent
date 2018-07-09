# YAML getting started - Multiple jobs

## Job dependencies

Multiple jobs can be defined within a stage. The order in which the jobs are executed, can be controlled by defining dependencies. The start of one job, can depend on another job completing. And a job can have more than one dependency.

Job dependencies enables four types of controls.

### Sequential jobs

Example jobs that execute sequentially:

```yaml
jobs:
- job: Debug
  steps:
  - script: echo hello from the Debug build

- job: Release
  dependsOn: Debug # After Debug completes
  steps:
  - script: echo hello from the Release build
```

Example where an artifact is published in the first job, and downloaded in the second job:

```yaml
jobs:
- job: A
  steps:
  - script: echo hello > $(system.artifactsDirectory)/hello.txt
    displayName: Stage artifact

  - task: PublishBuildArtifacts@1
    displayName: Upload artifact
    inputs:
      pathtoPublish: $(system.artifactsDirectory)
      artifactName: hello
      artifactType: Container

- job: B
  dependsOn: A
  steps:
  - task: DownloadBuildArtifacts@0
    displayName: Download artifact
    inputs:
      artifactName: hello

  - script: dir /s /b $(system.artifactsDirectory)
    displayName: List artifact (Windows)
    condition: and(succeeded(), eq(variables['agent.os'], 'Windows_NT'))

  - script: find $(system.artifactsDirectory)
    displayName: List artifact (macOS and Linux)
    condition: and(succeeded(), ne(variables['agent.os'], 'Windows_NT'))
```

## Parallel jobs

Example jobs that execute in parallel (no dependencies):

```yaml
jobs:
- job: Windows
  pool:
    name: Hosted
    image: VS2017
  steps:
  - script: echo hello from Windows

- job: macOS
  pool:
    name: Hosted
    image: Xcode9
  steps:
  - script: echo hello from macOS

- job: Linux
  pool:
    name: Hosted
    image: Ubuntu18
  steps:
  - script: echo hello from Linux
```

## Fan out

Example fan out

```yaml
jobs:
- job: InitialJob
  steps:
  - script: echo hello from initial job

- job: SubsequentA
  dependsOn: InitialJob
  steps:
  - script: echo hello from subsequent A

- job: SubsequentB
  dependsOn: InitialJob
  steps:
  - script: echo hello from subsequent B
```

## Fan in

```yaml
jobs:
- job: InitialA
  steps:
  - script: echo hello from initial A

- job: InitialB
  steps:
  - script: echo hello from initial B

- job: Subsequent
  dependsOn:
  - InitialA
  - InitialB
  steps:
  - script: echo hello from subsequent
```

## Job conditions

### Basic job conditions

You can specify conditions under which jobs will run. The following functions can be used to evaluate the result of dependent jobs:

* **succeeded()** - Runs if all previous jobs in the dependency graph completed with a result of Succeeded or SucceededWithIssues. Specific job names may be specified as arguments.
* **failed()** - Runs if any previous job in the dependency graph failed. Specific jobs names may be specified as arguments.
* **succeededOrFailed()** - Runs if all previous jobs in the dependency graph succeeded or any previous job failed. Specific job names may be specified as arguments.
<!-- * **canceled()** - Runs if the orchestration plan has been canceled. 
* **always()** - Runs always. -->

If no condition is explictly specified, a default condition of ```succeeded()``` will be used.

Example - Using the result functions in the expression:

```yaml
jobs:
- job: A
  steps:
  - script: exit 1

- job: B
  dependsOn: A
  condition: failed()
  steps:
  - script: echo this will run when A fails

- job: C
  dependsOn:
  - A
  - B
  condition: succeeded('B')
  steps:
  - script: echo this will run when B runs and succeeds
```

### Custom job condition, with a variable

[Variables](https://docs.microsoft.com/en-us/vsts/build-release/concepts/definitions/build/variables) and all general functions of [task conditions](https://go.microsoft.com/fwlink/?linkid=842996) are also available in job conditions.

Example - Using a variable in the expression:

```yaml
jobs:
- job: A
  steps:
  - script: echo hello

- job: B
  dependsOn: A
  condition: and(succeeded(), eq(variables['build.sourceBranch'], 'refs/heads/master'))
  steps:
  - script: echo this only runs for master
```

### Custom job condition, with an output variable

Output variables from previous jobs can also be used within conditions.

Only jobs which are referenced as direct dependencies are available for use.

Example - Using an output variable in the expression:

```yaml
jobs:
- job: A
  steps:
  - script: "echo ##vso[task.setvariable variable=skipsubsequent;isOutput=true]false"
    name: printvar

- job: B
  condition: and(succeeded(), ne(dependencies.A.outputs['printvar.skipsubsequent'], 'true'))
  dependsOn: A
  steps:
  - script: echo hello from B
```

For details about output variables, refer [here](https://github.com/Microsoft/vsts-agent/blob/master/docs/preview/outputvariable.md#for-ad-hoc-script).

## Expression context

Job-level expressions may use the following context:

* **variables** - all variables which are available in the root orchestration environment, including input variables, definition variables, linked variable groups, etc.
* **dependencies** - a property for each job exists as the name of the job.

Structure of the dependencies object:

```yaml
dependencies:
  <JOB_NAME>:
    result: (Succeeded|SucceededWithIssues|Skipped|Failed|Canceled)
    outputs:
      variable1: value1
      variable2: value2
```
