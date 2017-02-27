# VSTS Pipeline Static Expansion

The goal of this document is to discuss the planned static expansion mechanisms for pipeline yaml files. A large remaining question is whether to use custom mechanisms only, or additionally leverage mustache. If mustache is leveraged, it will eliminate the need for some of the custom constructs. Heavy emphasis of this document is evaluating the pros/cons of both directions.

## Templating

To start, templates can be checked-in to source alongside the entry yaml file.

### Parameters and defaults

It is unclear whether creating a custom mechanism to deal with parameters, has any advantage/disadvantage over mustache. For example, additional validation metadata, such as expected type info or a *required* flag, may fit more cleanly with a custom mechanism. Although it's hard to say whether it would fit cleanly without limiting the structure of data that could be passed as parameters.

#### Custom mechanism for parameters and defaults

```yaml
parameters:
  projects: "**/project.json"
  configuration: release
  dotnet: 1.1
jobs:
  - name: "build-{{configuration}}-{{dotnet}}"
    steps:
      - task: dotnet@1.*
        inputs:
          command: build
          project: s/src/dirs.proj
          configuration: "{{configuration}}"
          dotnet: "{{dotnet}}"
```

#### Mustache parameters and defaults

When using mustache preprocessing, we need a different technique to load default parameter values for the template. *Yaml front matter* is a common technique to initialize user-defined context prior to mustache replacement. Using yaml front matter, the default values can be defined in a separate section, distinguished by a starting line `---` and ending line `---`.

```yaml
---
projects: "**/project.json"
configuration: release
dotnet: 1.1
---
jobs:
  - name: "build-{{configuration}}-{{dotnet}}"
    steps:
      - task: dotnet@1.*
        inputs:
          command: build
          project: s/src/dirs.proj
          configuration: "{{configuration}}"
          dotnet: "{{dotnet}}"
```

### Ability to specify additional job-variables

In addition to passing parameters to templates, the ability to specify additional job-variables will likely be required.

#### Custom addtiaional job-variables

A way needs to be defined to inject a dictionary parameter into a job's variable dictionary. The cleanest custom way is probably to specify a separate property on the job object, that takes an array of dictionaries.

```yaml
parameters:
  projects: "**/project.json"
  configuration: release
  dotnet: 1.1
  additionalVariables: {}
jobs:
  - name: "build-{{configuration}}-{{dotnet}}"
    variables:
      projects: "{{projects}}"
      configuration: "{{configuration}}"
      dotnet: "1.1"
    variableMaps:
      - "{{additionalVariables}}"
    steps:
      - task: dotnet@1.*
        inputs:
          command: build
          project: s/src/dirs.proj
          configuration: "{{configuration}}"
          dotnet: "{{dotnet}}"
```

#### Mustache additional job-variables

```yaml
---
projects: "**/project.json"
configuration: release
dotnet: 1.1
additionalVariables: {}
---
jobs:
  - name: "build-{{configuration}}-{{dotnet}}"
    variables:
      configuration: {{configuration}}
      dotnet: {{dotnet}}
      projects: "{{projects}}"
{{#each additionalVariables}}
      "{{@key}}": "{{this}}"
{{/each}}
    steps:
      - task: dotnet@1.*
        inputs:
          command: build
          project: s/src/dirs.proj
          configuration: "{{configuration}}"
          dotnet: "{{dotnet}}"
```

### Step-list extensibility (and overrides)

Custom vs mustache does not come into play wrt this this proposed mechanism. This is purely a custom construct. See pipeline.md for more details.

## Job looping

The goal is to define a more precise job looping mechanism over the current *multipliers* mechanism. The current *multipliers* mechanism will not be carried over into yaml.

### Custom job looping

A job can define an array of items to use, to explode the job into multiple jobs.

The looping scenarios are more interesting when combined with templating, and the value-to-loop-over is passed as a parameter to the template. The point here is to illustrate the looping construct itself.

Sample 1, array of strings:

```yaml
jobs:
  - name: "build-{{item}}-release"
    with_items:
      - x86
      - x64
    steps:
      - task: msbuild@1.*
        inputs:
          project: s/src/dirs.proj
          platform: "{{item}}"
          configuration: release
```

Sample 2, array of dictionaries:
```yaml
jobs:
  - name: "build-{{item.platform}}-{{item.configuration}}"
    with_items:
      - platform: x86
        configuration: release
      - platform: x64
        configuration: release
    steps:
      - task: msbuild@1.*
        inputs:
          project: s/src/dirs.proj
          platform: "{{item.platform}}"
          configuration: "{{item.configuration}}"
```

Note, the goal here is limited to being able to explode a single job into multiple jobs. It is not a requirement to be able to define a loop surrounding multiple jobs - that can be accomplished by multiple loops each surrounding a single job.

### Mustache job looping

Sample 1, array of strings:

```yaml
---
platforms:
  - x86
  - x64
---
jobs:
{{#platforms}}
  - name: "build-{{this}}-release"
    steps:
      - task: msbuild@1.*
        inputs:
          project: s/src/dirs.proj
          platform: "{{this}}"
          configuration: release
{{/platforms}}
```

Sample 2, array of dictionaries:
```yaml
---
matrix:
  - platform: x86
    configuration: release
  - platform: x64
    configuration: release
---
jobs:
{{#matrix}}
  - name: "build-{{platform}}-{{configuration}}"
    steps:
      - task: msbuild@1.*
        inputs:
          project: s/src/dirs.proj
          platform: "{{platform}}"
          configuration: "{{configuration}}"
{{/matrix}}
```

## General custom mechanisms vs mustache pros/cons

### Custom mechanisms pros

* Simpler than learning mustache. However, learning mustache would largely be required by template authors only. And even then, template authors would have samples to leverage as a go-by.

### Custom mechanisms cons

### Mustache pros

* Flexibility.
 - Can leverage mustache anywhere within the document, not just for job looping. For example, exploding tasks based on inputs.
 - Can leverage other mustache functions (if, equals, etc), although task conditions may suffice to solve this problem.

* Popular standard (familiarity for some).

* Reduces work (offload to mustache, we already have the parser). Affected areas: parameters, injecting additional variables, job looping.

### Mustache cons

* Additional layer of complexity, and place for things to go wrong. We can offset this to some degree by adding a --whatif switch to the agent, to dump the statically exploded yaml. The challenge will be error messages, to convey where the problem occurred (preprocessing drastically changes the user input). Nick K. may have some helpful advice here.

## Open issues

* Modify mustache parser to json-escape by default? Or add a custom j-escape function?