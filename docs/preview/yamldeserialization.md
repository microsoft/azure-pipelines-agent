# VSTS Pipeline YAML Deserialization

This document describes the YAML deserialization process, and how the YAML gets converted into into a pipeline.

## Run local

A goal of the agent is to support "run-local" mode for testing YAML configuration. When running local, all agent calls to the VSTS server is stubbed (and server URL is hardcoded to 127.0.0.1).

When running local, the YAML will be converted into a pipeline, and worker processes invoked for each job.

A definition variable `Agent.RunMode`=`Local` is added to each job.

Note, this is not fully implemented yet. Only task steps are supported. Sync-sources and resource-import/export are not supported yet. Each job is run with syncSources=false.

Example:
```
~/vsts-agent/_layout/bin/Agent.Listener --whatif --yaml ~/vsts-agent/src/Agent.Listener/DistributedTask.Pipelines/cmdline.yaml
```

### What-if mode

A "what-if" mode is supported for debugging the YAML static expansion and deserialization process. What-if mode dumps the constructed pipeline to the console, and exits.

Example:
```
~/vsts-agent/_layout/bin/Agent.Listener --whatif --yaml ~/vsts-agent/src/Agent.Listener/DistributedTask.Pipelines/uses-vsbuild.yaml
```

### Task version resolution and caching

In run-local mode, all referenced tasks must either be pre-cached under \_work/\_tasks, or optionally credentials can be supplied to query and download each referenced task from VSTS/TFS.

VSTS example:
```
~/vsts-agent/_layout/bin/Agent.Listener --url https://contoso.visualstudio.com --auth pat --token <TOKEN> --yaml ~/vsts-agent/src/Agent.Listener/DistributedTask.Pipelines/cmdline.yaml
```

TFS example (defaults to integrated):
```
~/vsts-agent/_layout/bin/Agent.Listener --url http://localhost:8080/tfs --yaml ~/vsts-agent/src/Agent.Listener/DistributedTask.Pipelines/cmdline.yaml
```

TFS example (negotiate, refer `--help` for all auth options):
```
~/vsts-agent/_layout/bin/Agent.Listener --url http://localhost:8080/tfs --auth negotiate --username <USERNAME> --password <PASSWORD> --yaml ~/vsts-agent/src/Agent.Listener/DistributedTask.Pipelines/cmdline.yaml
```

## YAML static expansion and deserialization process

### Outline

1. Preprocess entry file (mustache)
1. Deserialize into a pipeline
1. If pipeline references a template
 1. Preprocess template file (mustache)
 1. Deserialize into a pipeline template
 1. Merge the pipeline with the template (merge resources, overlay hooks)

### Mustache escaping rules

Properties referenced using `{{property}}` will be JSON-string-escaped. Escaping can be omitted by using the triple-brace syntax: `{{{property}}}`.

### Mustache context object

Optional YAML front-matter can be used to define the mustache context object. YAML front-matter must be at the beginning of the document. The front-matter is indicated by the `---` section. For more details, refer to the example below.

Within a template the mustache context object is a composition of the YAML-front matter + parameters from caller overlaid on top.

Example YAML front-matter:

```yaml
---
matrix:
 - buildConfiguration: debug
   buildPlatform: any cpu
 - buildConfiguration: release
   buildPlatform: any cpu
---
jobs:
  {{#each matrix}}
  - name: build-{{buildConfiguration}}-{{buildPlatform}}}
    - task: VSBuild@1.*
      inputs:
        solution: "**/*.sln"
        configuration: "{{buildConfiguration}}"
        platform: "{{buildPlatform}}"
  {{/each}}
```
