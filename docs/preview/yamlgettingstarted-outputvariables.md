# YAML getting started - Output variables

Output variables enable setting a variable in one job, that can be used in a downstream job.

## Mapping an output variable

Output variables must be mapped into downstream jobs.

They are prefixed with the name of the step that set the variable.

Note, outputs can only be referenced from jobs which listed as direct dependencies.

```yaml
jobs:

# Set an output variable from job A
- job: A
  pool:
    name: Hosted
    image: VS2017
  steps:
  - powershell: echo "##vso[task.setvariable variable=myOutputVar;isOutput=true]this is the value"
    name: setvar
  - script: echo $(setvar.myOutputVar)
    name: echovar

# Map the variable into job B
- job: B
  dependsOn: A
  pool:
    name: Hosted
    image: Ubuntu18
  variables:
    myVarFromJobA: $[ dependencies.A.outputs['setvar.myOutputVar'] ]
  steps:
  - script: echo $(myVarFromJobA)
    name: echovar
```

## Mapping an output variable from a matrix

```yaml
jobs:

# Set an output variable from a job with a matrix
- job: A
  pool:
    name: Hosted
    image: Ubuntu18
  strategy:
    matrix:
      debug:
        configuration: debug
        platform: x64
      release:
        configuration: release
        platform: x64
  steps:
  - script: echo "##vso[task.setvariable variable=myOutputVar;isOutput=true]this is the $(configuration) value"
    name: setvar
  - script: echo $(setvar.myOutputVar)
    name: echovar

# Map the variable from the debug job
- job: B
  dependsOn: A
  pool:
    name: Hosted
    image: Ubuntu18
  variables:
    myVarFromJobADebug: $[ dependencies.A.outputs['debug.setvar.myOutputVar'] ]
  steps:
  - script: echo $(myVarFromJobADebug)
    name: echovar
```

## Mapping an output variable from a slice

```yaml
jobs:

# Set an output variable from a job with slicing
- job: A
  pool:
    name: Hosted
    image: Ubuntu18
  strategy:
    slice: 2
  steps:
  - script: echo "##vso[task.setvariable variable=myOutputVar;isOutput=true]this is the slice $(system.sliceNumber) value"
    name: setvar
  - script: echo $(setvar.myOutputVar)
    name: echovar

# Map the variable from the job for the first slice
- job: B
  dependsOn: A
  pool:
    name: Hosted
    image: Ubuntu18
  variables:
    myVarFromJobA1: $[ dependencies.A.outputs['job1.setvar.myOutputVar'] ]
  steps:
  - script: "echo $(myVarFromJobA1)"
    name: echovar
```
