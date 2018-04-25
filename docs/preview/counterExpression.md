# Counter Expression

## Overview

This feature adds a new expression function, `counter`, which will atomically increment a variable value at the start of each build. The value of this variable will remain the same across phases, regardless of how or where those phases are executed.

`counter` values are identified by their "prefix" which acts as a lookup key, meaning the same `counter` can be used in each phase or as a top-level variable, and it will be incremented exactly once for the build. The value of `counter` is unique to the build definition in which it is defined, regardless of the prefix.

### Example

This example increases the Semantic Versioning patch number on each build by using a `counter` expression to increment the patch digit when the major and minor version values have not changed.

```yaml
variables:
  majorMinorVersion: 1.0.
  semanticVersion: $[ counter(variables['majorMinorVersion'], 0) ]
steps:
- powershell: |
    ./updateVersion.ps1 -version $(semanticVersion)
- task: MSBuild@1
  inputs:
    solution: '**/*.sln'
    configuration: '$(build.config)'
```

## Syntax

The UI will provide an editor, but the functionality will be stored as a [variable expression](conditions.md). Users may also enter the expression directly into the current variable field.

*TBD: we should discuss what the user sees when they return to the Variables tab later. Does the UI parse out `counter` variables and only show them in the new UI? Does it show up in both places?*

`counter(prefix[, seed])`\
`counter()`

Example: Produce a variable with a prefix from the `$BuildNumberPrefix` which starts counting from `$BuildNumberSeed`
\
`counter(variables['BuildNumberPrefix'], variables['BuildNumberSeed'])`

Example: On the first build, the variable will have the string value `100` which then increments on each subsequent build (`101`, `102`, ...)
\
`counter('', 100)`

## Technical reference

### Parameters

#### Prefix

The first parameter is the counter prefix. For example, `counter('prefix ', 0)` will create a variable value of `prefix 0` for the first build. The expanded parameter value will be treated as a raw string, and prepended directly to the final counter value (with no spaces or punctuation added). If the expanded value changes, the integer portion of the counter will be reset to the seed value.

#### Seed

The optional second parameter is the counter seed. It defaults to `0`. On the first build, this is the counter value. Each subsequent build with an unchanged prefix value will increment the counter by one. Should the prefix string change, the integer portion of the counter will be reset back to the seed.

### Overload

There is an overload of `counter` that takes no parameters. It is equivalent to `counter('', 0)`.

### Evaluation Rules

#### Variables and expressions

`counter` may reference other variables through the standard object indexing rules. For example, `counter(variable['prefix'], variable['seed'])` should be a valid expression.

If the counter expression variable is a top-level variable, it may be assigned to any phase-level variable. Assignment to another top-level variable is not supported at this time. If the counter expression variable is a phase-level variable, it may be assigned to any other phase-level variable _for this phase only_. This includes output variables, which will allow the value to be passed to subsequent phases.

#### Multiple references

Two more more variables may be assigned the same counter prefix across the whole build definition. The first one to be evaluated will increment the counter value, and all other variables will be assigned that new value. For example,

```yaml
phases:
- phase: A
  variables:
    var_a: $[ counter('id: ', 100) ]
- phase: B
  variables:
    var_b: $[ counter('id: ', 0) ]
```

When phase `A` executes, it will assign `"id: 100"` to `var_a`. When phase `B` executes, the counter with prefix `"id: "` has already been incremented for this build and it will also be assigned `"id: 100"`.
