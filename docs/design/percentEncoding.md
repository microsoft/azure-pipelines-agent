# Percent Encoding

### Problem

As the agent currently works, there is no way to pass certain reserved values (%3B, %0D, %0A, and %5D) through the agent without using a custom encoding/decoding scheme. This is hard because you have to control the scheme used by the sender and receiver.

The reason this is impossible is because we escape certain values needed for the ##vso commands to function: `; -> %3B, \r -> %0D, \n -> %0A, ] -> %5D`. The agent then automatically decodes these values. We use `%` to encode these, but don't provide an option for encoding `%` itself

### Solution

We've introduced encoding for `%` which will map to `%AZP25`. This means that any time the agent receives `%AZP25` as part of a command, it will automatically decode it to `%`. So `##vso[task.setvariable variable=test%AZP25]a%AZP25` will now set a variable `test%: a%`.

NOTE: This was previously designed to use %25 instead of %AZP25 as the escape sequence. We decided to go with %AZP25 instead since %25 was used somewhat often
because of its role in url encoding. Some agents may continue to emit warnings for %25 as this change rolls out (or if you haven't updated to the most recent agent).
These warnings are safe to ignore.

This behavior will be enabled by default in March 2021. To disable it, you can set a job level variable DECODE_PERCENTS to false. To avoid getting warnings about it and opt into the behavior early, set a job level variable DECODE_PERCENTS to true.

```
jobs:
- job:
  variables:
  - name: DECODE_PERCENTS
    value: true

  steps:
  - powershell: Write-Host '##vso[task.setvariable variable=test]a%AZP25'
    displayName: 'Set Variable'

  # This will print the a% correctly as the value of test
  - powershell: 'Get-ChildItem env:'
    displayName: 'printenv'
```
