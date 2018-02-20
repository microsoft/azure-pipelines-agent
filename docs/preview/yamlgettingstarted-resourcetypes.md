# Resources

An example of a resource can be resources published by another CI/CD definition viz. builds/artifacts, repositories etc. 

Resources in YAML represent sources of type builds, repositories, packages and containers. 
In case of container type resources, there is no explicit download step involved, however the source variables viz. source branch, build definition id, build id etc. are set. 

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

Example with implicit download of resources. Resources can be selectively downloaded using `downloadBuilds` for resources of type build or `checkout` step for repositories type.


```yaml
resources:
  builds:
  - name: mysample-app
    type: build
    project: DevOps
    source: mySampleApp.CI              
    defaultVersion: latest
  - name: mysample-app2
    type: build
  repositories:
  - name: customerService
    type: Git
    source: CoreApps/sample-app
    branch: master
    clean: true
  packages:
  - name: myfeed1
  containers:
  - name: dev1
    image: ubuntu:17.10
    registry: privatedockerhub  
stages: 
- stage: dev
  phases:
  - phase: A                                              
    steps:
    - script: dir /s /b $(system.artifactsDirectory)                      #build, repositories and packages are implicitly downloaded
      displayName: List artifact (Windows)
      condition: and(succeeded(), eq(variables['agent.os'], 'Windows_NT'))    
  - phase: B
    steps:
      - checkout: customerService                     #overrides clean, inherits rest from global resource definition
        clean: false                                      
      - downloadBuilds: none                          #builds are skipped
      - script: echo hello from phase B
  - phase: C
    steps:
      - checkout: none                                    #repository download is skipped
      - downloadBuilds: mysample-app2
        artifact:                                         #selective artifacts download. 
        - drop2
        - drop3
      - script: echo hello from phase B
```


