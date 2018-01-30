# YAML getting started - Multiple stages and stage type

## Pipelines and Stages
VSTS Pipelines defines a set of stages that can get executed in sequence or in parallel. For example, you can have an overall pipeline process that comprises a build stage, followed by several deploy and test stages. 

## Stage

Stages represent build or release process. In CD YAML, ‘Stage’ is the equivalent of an environment. Stage is a logical and independent entity that can represent both CI and CD processes of a pipeline.  

A stage:
*	A stage can be explicitly defined or can be implicit
* Has a single phase by default, but can be used to group multiple phases
*	Can process phases in sequence or in parallel
*	May produce artifacts that can be made available for use by subsequent stage(s). For example, a stage of type ‘build’ can produce artifacts that can be consumed by other stages in the pipeline.
*	Can be configured to be triggered manually or can be triggered automatically upon successful completion of a prior stage. 

For example, a simple process may only define a build stage, (one job, one phase). User can add additional stages to define deploy, test stages including production. Users can also define pipeline sans build stage in the YAML and instead refer to a CI YAML file using templating. 


## Stage dependencies

Multiple stages can be defined in a pipeline. The order in which the stages execute can be controlled by dependencies. i.e., start of a stage, can depend on another stage completing. Stage can have multiple dependencies. Likewise output of one stage can be input to another. Stages can have dependencies and trigger conditions.  An example of stage consuming output from prior stage is – artifacts produced by a build stage or chained build stages consuming artifacts from prior build stage. 

Stage dependencies enables four types of controls.

### Sequential Stage

Example stage that execute sequentially.

```yaml
stages: 
- stage: myBuild
  type: build                            #type can be build | Release
  phases:
  - phase:
    steps:
    - script: echo hello from myBuild
- stage: QA
  type: release
  dependsOn: myBuild
  phases:
  - phase:
    steps:
    - script: echo hello from the QA stage
```

## Parallel stages

Example of stage that execute in parallel (no dependencies)

```yaml
stages: 
- stage: QA1
  type: release 
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  type: release
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
  type: release
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA1
  type: release
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  type: release
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
  type: release
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA1
  type: release
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  type: release
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
- stage: Production
  type: release
  dependsOn: 
  - QA1
  - QA2
  phases:
    - phase:
      steps:
      - script: echo hello from Production
```


## Manual start for stages

You can specify start type for a stage. If not specified, they are automatically started by default.  Stages that are manually started can be represented as `startType: manual`.

Example of a stage (production) that is started manually after the dependencies are met. 

```yaml
stages: 
- stage: Dev
  type: release
  phases:
  - phase:
    steps:
    - script: echo hello from Dev
- stage: QA1
  type: release
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA1
- stage: QA2
  type: release
  dependsOn: Dev
  phases:
  - phase:
    steps:
    - script: echo hello from QA2
- stage: production
  type: release
  startType: manual             #startType: manual | automated
  dependsOn: 
  - QA1
  - QA2
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
- stage: debug
  type: build
  phases:
  - phase:
    steps:
    - script: echo hello from debug build
- stage: QA
  type: release
  dependsOn: debug
  condition: succeeded(‘debug’)
  phases:
  - phase:
    steps:
    - script: echo hello from QA
```

Example where an artifact is published in the build stage, and downloaded in the release stage:

```yaml
stages: 
- stage: myBuild
  type: build
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
- stage: Int
  type: release
  dependsOn: myBuild
  phases:
  - phase: A
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
