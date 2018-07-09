# YAML getting started - Pipeline overview

## Pipeline

When a pipeline is started, the execution plan is created first.

A pipeline contains stages. Stages contain jobs. Jobs contain steps.

```
---------------------------------------------
|                 Pipeline                  |
|                                           |
|    -----------------------------------    |
|    |             Stages              |    |
|    |                                 |    |
|    |    -------------------------    |    |
|    |    |         Jobs          |    |    |
|    |    |                       |    |    |
|    |    |    ---------------    |    |    |
|    |    |    |    Steps    |    |    |    |
|    |    |    ---------------    |    |    |
|    |    |                       |    |    |
|    |    -------------------------    |    |
|    |                                 |    |
|    -----------------------------------    |
|                                           |
---------------------------------------------
```

## Stages

Stages provide a logical boundary within the pipeline.

The stage boundary allows:
- Manual checkpoints or approvals between stages
- Reporting on high level results (email notifications, build badges)

## Jobs

A job is a group of steps, and is assigned to a specific pool.

For example, when a job targets an agent pool, the job will be assigned to one of the agents running within the pool.

## Steps

Steps are the individual units of execution within a job. For example, run a script.
