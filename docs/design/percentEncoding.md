# Percent Encoding

### Problem

As the agent currently works, there is no way to pass certain reserved values (%3B, %0D, %0A, and %5D) through the agent without using a custom encoding/decoding scheme. This is hard because you have to control the scheme used by the sender and receiver.

The reason this is impossible is because we escape certain values needed for the ##vso commands to function: `; -> %3B, \r -> %0D, \n -> %0A, ] -> %5D`. The agent then automatically decodes these values. We use `%` to encode these, but don't provide an option for encoding `%` itself

### Solution

We've introduced encoding for `%` which will map to `%25`. This means that any time the agent receives `%25` as part of a command, it will automatically decode it to `%`. So `##vso[task.setvariable variable=test%25]a%25` will now set a variable `test%: a%`.

This behavior will be enabled by default in January 2021. To disable it, you can set an environment variable DECODE_PERCENTS to false in the appropriate step. To avoid getting warnings about it and opt into the behavior early, set an environment variable DECODE_PERCENTS to true in the appropriate step