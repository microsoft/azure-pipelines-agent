# VSTS Pipeline YAML Deserialization

This document describes the YAML deserialization process, and how the YAML gets converted into into a pipeline.

## Run local

A goal of the agent is to support "run-local" mode for testing YAML configuration. When running local, all agent calls to the VSTS server is stubbed (and server URL is hardcoded to 127.0.0.1).

When running local, the YAML will be converted into a pipeline, and worker processes invoked for each job.

Note, this is not fully implemented yet. Currently, regardless of the YAML configuration, run-local runs the same hardcoded job every time.

### What-if mode

For debugging YAML configuration, a what-if mode is supported. This will load up the YAML configuration, and dump the deserialized pipeline to the console, and exit.

```
~/vsts-agent/_layout/bin/Agent.Listener --whatif --yaml ~/vsts-agent/src/Agent.Listener/DistributedTask.Pipelines/uses-vsbuild.yaml
```

## YAML deserialization process

1. Preprocess entry file (mustache)
1. Deserialize into a pipeline
1. If pipeline references a template
 1. Preprocess template file (mustache)
 1. Deserialize into a pipeline template
 1. Merge the pipeline with the template (merge resources, overlay hooks)
