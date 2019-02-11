
# Log decorations


Task authors should be able to control how the log output is displayed to the end user.
This outlines different decoration options that are available.

## Collapse

>Note that that if you log an error using ```##vso[task.logissue]error/warning message``` command (see [logging commands](https://github.com/Microsoft/azure-pipelines-tasks/blob/master/docs/authoring/commands.md) here) we will surface those errors in build view and when clicked , we will automatically jump to that particular line. If it's already part of a group, we will auto-expand the group.

Task authors can mark any part of the log as a collapsible region using these decorations:

Starting the collapsible region - `##[group:${groupName}:start]`

Ending the collapsible region - `##[group:${groupName}:end]`

Note: The first line of region will be taken as group title by default.

Example -

```
##[group:command1:start]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
Write your commands here
Use the environment variables input below to pass secret variables to this script
##[group:command1:end]
##[group:command2:start]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
This is command 2
##[group:command2:end]
```

will be perceived as -

```
> ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
> ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
```

```
v ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
    Write your commands here
    Use the environment variables input below to pass secret variables to this script
v ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
    This is command 2
```


### Open questions
* Can the tool runner we have automatically mark the end of output for a command in a reliable way? This way all of the tools that run will get collapsible regions for free.
