# Resources

Global resources in a YAML are available to all the stages. An example of a resource can be resources published by another CI/CD definition viz. builds/artifacts, repositories etc. 

Resources in YAML represent sources of type builds, repositories, packages and containers. 
Assumption: 
* Variable Group is not part of resources. Variable groups can be modeled as 'Queues' and can be discovered and authorized against the stages.
* In case of container type resources, there is no explicit download step involved, however the source variables viz. source branch, build definition id, build id etc. are set. 

Example resource with builds

```yaml
resources:                         # types: builds | repositories | packages | containers
  builds:
  - name: mysample-app
    type: build
    project: DevOps
    source: mySampleApp.CI              
    defaultVersion: latest
  - name: sample-app               
    type: Jenkins
    source: sampleAppJob
    connection: myJenkisConnection
```

Example resources with repositories
```yaml
resources:
  repositories:
  - name: customerService
    type: Git
    source: CoreApps/sample-app
    branch: master
    clean: true | false
    fetchDepth: number
    lfs: true | false
    sync: true | false
    reportStatus: true | false
  - name: sampleService
    type: GitHub
    connection: myGitHubConnection
    source: Microsoft/sample-service
    branch: master
```

Example resources with packages and containers

```yaml
resources:
  packages:
  - name: feedSampleApp
    type: package
    feed: feed-CI
    package: feedSampleApp
    defaultVersion: latest
  containers:
  - name: adventworks-sample
    type: container
    connection: myConnection
    registry: adventworks
    containerRepository: adventworks/sample-app
```

Example with opt-in model download of resources, and selective download of resources.

Resources can be selectively downloaded using `getResources` and it will translate to equivalent tasks at runtime based on the resource type. i.e, either `downloadArtifact` step or `checkout` step.

```yaml
resources:
  builds:
  - name: mysample-app
    type: build
    project: DevOps
    source: mySampleApp.CI              
    defaultVersion: latest
  repositories:
  - name: customerService
    type: Git
    source: CoreApps/sample-app
    branch: master
    clean: true
  packages:
  - name: feed1
  containers:
  - name: foo
stages: 
- stage: dev
  type: release
  phases:
  - phase: A                                              # resource download is skipped.
    steps:
    - script: echo hello from phase A
  - phase: B
    steps:
    - getResources:                                       #opt-in to download individual resources.
      - name: customerService
        clean: false                                      #overrides clean, inherits rest from global resource definition
      - name: mysample-app
        artifact:                                         #selective artifacts download. 
        - drop2
        - drop3
      - script: echo hello from phase B
```


