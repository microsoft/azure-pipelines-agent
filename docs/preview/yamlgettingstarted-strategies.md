# YAML getting started - Job strategies

### Matrix

The `matrix` strategy enables a job to be dispatched multiple times, with different variable sets.

For example, a common scenario is to run the same build steps for varying permutations of architecture (x86/x64) and configuration (debug/release).

```yaml
strategy:
  maxParallel: 1 # Limit to one agent at a time. The default is fully parallel.
  matrix:
    x64_debug:
      buildArch: x64
      buildConfig: debug
    x64_release:
      buildArch: x64
      buildConfig: release
    x86_release:
      buildArch: x86
      buildConfig: release
steps:
- script: build arch=$(buildArch) config=$(buildConfig)
```

### Slice

The `slice` setting indicates how many jobs to dispatch. Variables `system.sliceNumber` and `system.sliceCount` are added to each job. The variables can then be used within your scripts to divide work among the jobs.

```yaml
strategy:
  slice: 5
steps:
- script: test slice=$(system.sliceNumber) sliceCount=$(system.sliceCount)
```
