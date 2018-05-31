# YAML getting started - Multiple stages and stage type

## Pipelines and Stages
VSTS CD defines a set of stages that can get executed in sequence or in parallel. For example, you can have an overall pipeline process that comprises a build, followed by several deploy and test stages of CD. 

## Stage

In CD YAML, ‘Stage’ is the equivalent of an environment. Stage is a logical and independent entity that can represent both CI and CD processes of a pipeline.  

A stage:
*	A stage can be explicitly defined or can be implicit
* Has a single phase by default, but can be used to group multiple phases
*	Can process phases in sequence or in parallel
*	Can be configured to be triggered manually or can be triggered automatically upon successful completion of a prior stage. 

For example, a simple process may only define a build section, (one job, one phase). User can add additional release stages to define deploy, test stages including production in the same file. 

### CI and CD in a single file

Example cd flow that is depends on ci in a single file.

```yaml
build:                                              
  phases:
  - phase: default
    steps:
    - script: echo hello from myBuild
release:                                            #implicit CD trigger on completion of CI.
  stages:
  - stage: QA1
    phases:
    - phase:
      steps:
      - script: echo hello from the QA stage
```

## Stage dependencies in CD

Multiple stages can be defined in a CD pipeline. The order in which the stages execute can be controlled by dependencies. i.e., start of a stage, can depend on another stage completing. Stage can have multiple dependencies. Stages can have dependencies and trigger conditions.  

Stage dependencies in CD enables four types of controls.

## Parallel stages

Example of stage that execute in parallel (no dependencies between stages)

```yaml
stages: 
- stage: QA1
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
```

## Sequential stages

Example of stage that execute in sequence

```yaml
stages: 
- stage: QA1
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  dependsOn: QA1
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
```

## Fan out

Example of stages that start in parallel and with a sequential dependency on a stage. 

```yaml
stages: 
- stage: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA1
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
```

## Fan in

Example of stage that has a dependency on multiple stages 

```yaml
stages: 
- stage: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA1
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
- stage: Production
  dependsOn: 
  - QA1
  - QA2
  phases:
    - phase:
      steps:
      - script: echo hello from Production
```


## Manual start for stages

You can specify start type for a stage. If not specified, they are automatically started by default.  Stages that are manually started can be represented as `trigger: none`.

Example of a stage (production) that is triggered with resource filter after the dependencies are met. 

```yaml
stages: 
- stage: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA1
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
- stage: production
  dependsOn: 
  - QA1
  - QA2
  trigger:                             # trigger: none  for manual
    schedule:
    # ...                              scheduling options, future 
    resource:
    - myBuild
        include:  
        - branch: master
          tags: 
          - verified                   # build tags
          - succeeded
          exclude:      
          - branch: releases/old* 
  phases:
    - phase:
      steps:
      - script: echo hello from production
```

## Stage conditions

### Basic stage conditions

You can specify conditions under which stages will run. The following functions can be used to evaluate the result of dependent stages:
*	**succeeded()** or **succeededWithIssues()** - Runs if all previous stages in the dependency graph completed with a result of Succeeded or SucceededWithIssues. Specific stage names may be specified as arguments.
*	**failed()** - Runs if any previous stage in the dependency graph failed. Specific stage names may be specified as arguments.
*	**succeededOrFailed()** - Runs if all previous stages in the dependency graph succeeded or any previous stages failed. Specific stage names may be specified as arguments

If no condition is explictly specified, a default condition of ```succeeded()``` will be used.

```yaml
stages:
- stage: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA
  condition: succeeded('dev')
  phases:
  - phase:
    steps:
    - script: echo hello from QA
```

Example where an artifact is published in ci, and downloaded in cd stage(s):

```yaml
build: 
  phases:
  - phase: A
    steps:
    - script: echo hello > $(system.artifactsDirectory)/hello.txt
      displayName: Stage artifact
    - task: PublishBuildArtifacts@1
      displayName: Upload artifact
      inputs:
        pathtoPublish: $(system.artifactsDirectory)
        artifactName: hello
        artifactType: Container
release: 
  stages:
  - stage: Dev        
    phases:
    - phase: A
      steps:                         
      - script: dir /s /b $(system.artifactsDirectory)                      #build artifacts are implicitly downloaded
        displayName: List artifact (Windows)
        condition: and(succeeded(), eq(variables['agent.os'], 'Windows_NT'))
      - script: find $(system.artifactsDirectory)
        displayName: List artifact (macOS and Linux)
        condition: and(succeeded(), ne(variables['agent.os'], 'Windows_NT'))
```
