# Counter Expression

## Overview

This feature adds a new expression function, `counter`, which will atomically increment a variable value at the start of each build. The value of this variable will remain the same across phases, regardless of how or where those phases are executed. This expression will be unique to top-level definition variables, and not valid in phase variables.

## Syntax

The UI will provide an editor, but the functionality will be stored as a [variable expression](conditions.md). Users may also enter the expression directly into the current variable field.

*TBD: we should discuss what the user sees when they return to the Variables tab later. Does the UI parse out `counter` variables and only show them in the new UI? Does it show up in both places?*

Example: Produce a variable with a prefix from the `$BuildNumberPrefix` which starts counting from `$BuildNumberSeed`
\
`counter(variables['BuildNumberPrefix'], variables['BuildNumberSeed'])`

Example: On the first build, the variable will have the string value `100` and then increment on each subsequent build
\
`counter('', 100)`

## Technical reference

### Parameters

#### Prefix

The first parameter is the counter prefix. For example, `counter('prefix ', 0)` will create a variable value of `prefix 0` for the first build. The expanded parameter value will be treated as a raw string, and prepended directly to the final counter value (with no spaces or punctuation added). If the expanded value changes, the counter will reset back to the seed value.

#### Seed

The second parameter is the counter seed. On the first build, this is the counter value. Each subsequent build with an unchanged prefix value will increment the counter by one.

### Rules

`counter` may reference other variables through the standard object indexing rules. For example, `counter(variable['prefix'], variable['seed'])` should be a valid expression.

The result of expanding `counter` can be assigned to a phase-level variable. Assignment to another top-level variable is not supported at this time.

In order to be valid, `counter` *must* be defined at the definition level. A phase-level `counter` expression should fail to parse. This restriction may be lifted in the future if we decide on a good definition for how a phase-level `counter` would operate.
