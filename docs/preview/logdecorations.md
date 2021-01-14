
# Log decorations


Task authors should be able to control how the log output is displayed to the end user.
This outlines different decoration options that are available.

## Special lines
* Errors
  * `##[error] I am an error` 
* Warnings
  * `##[warning] I am a warning` 
* Debug
  * `##[debug] I am a debug output` 
* Commands
  * `##[command] I am a command/a tool` 
* Sections
  * `##[section] I am a section, which is usually whole task step. Agent injects this internally.` 

## Collapse

>Note that that if you log an error using ```##vso[task.logissue]error/warning message``` command (see [logging commands](https://github.com/Microsoft/azure-pipelines-tasks/blob/master/docs/authoring/commands.md) here) we will surface those errors in build view and when clicked , we will automatically jump to that particular line. If it's already part of a group, we will auto-expand the group.


Task authors can mark any part of the log as a collapsible region using these decorations:

Starting the collapsible region - `##[group]`

Ending the collapsible region - `##[endgroup]`

### Notes
* Nested groups is out of current scope.
* Our tool runner can start injecting `##[group]` in front of `##[command]`, that will support grouping, if we need much grainer control over grouping, it can also add `##[endgroup]` when the command outputs the whole text.
*  The first line of region will be taken as group title by default.
*  If there's only one line in the region (including the group title), it will not be considered as a collapsible.
*  If there's `##[group]` with out corresponding `##[endgroup]` we will add implicit `##[endgroup]`.
* Decisions on how to we surfaces error/warnings that are part of a group is not covered in this doc.

### Examples
Example 1 -


```
##[group]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
Write your commands here
Use the environment variables input below to pass secret variables to this script
##[group]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
This is command 2
##[endgroup]
##[group]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c9.cmd""
##[endgroup]
##[group:noendgroup]
I started a group with out end
##[group]
I am a group
I am a group
##[endgroup]
I am a part of parent group
```

will be perceived as -

```
> ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
> ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
  ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c9.cmd""
> I started a group with out end
```

```
v ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
    Write your commands here
    Use the environment variables input below to pass secret variables to this script
v ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
    This is command 2
  ##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c9.cmd""
v I started a group with out end
  > I am a group
  I am a part of parent group
```

Example 2 -

Get sources task :

Original task - 
```
Syncing repository: SomeRepo (Git)
Prepending Path environment variable with directory containing 'git.exe'.
##[command]git version
git version 2.18.0.windows.1
##[command]git config --get remote.origin.url
##[command]git clean -ffdx
##[command]git reset --hard HEAD
HEAD is now at cb1adf878a7b update swe
##[command]git config gc.auto 0
##[command]git config --get-all http.https://repohere
##[command]git config --get-all http.proxy
##[command]git -c http.extraheader="AUTHORIZATION: bearer ***" fetch --tags --prune --progress --no-recurse-submodules origin
From https://repohere
- [deleted] (none) -> origin/teams/some
remote: Azure Repos
remote:
remote: Found 1444 objects to send. (1323 ms)
Receiving objects: 0% (1/1444)
...
Resolving deltas: 100% (708/708), completed with 594 local objects.
7d80bdb9d646..5214d0492d27 features/DraggableDashboardGrid -> origin/features/DraggableDashboardGrid
...
...
##[command]git checkout --progress --force e48a3009f2a0163d102423eef6ffaf7f4c2a2176
Warning: you are leaving 1 commit behind, not connected to
any of your branches:
cb1adf878a7b Update CloudStore packages to 0.1.0-20190213.7 and Domino packages to 0.1.0-20190213.7
If you want to keep it by creating a new branch, this may be a good time
to do so with:
git branch <new-branch-name> cb1adf878a7b
HEAD is now at e48a3009f2a0 update swe
##[command]git config http.https://repohere "AUTHORIZATION: bearer ***"
```

Single grouping -
```
Syncing repository: SomeRepo (Git)
Prepending Path environment variable with directory containing 'git.exe'.
##[group]
##[command]git version
git version 2.18.0.windows.1
##[group]
##[command]git config --get remote.origin.url
##[group]
##[command]git clean -ffdx
##[group]
##[command]git reset --hard HEAD
##[group]
HEAD is now at cb1adf878a7b update swe
##[group]
##[command]git config gc.auto 0
##[group]
##[command]git config --get-all http.https://repohere
##[group]
##[command]git config --get-all http.proxy
##[group]
##[command]git -c http.extraheader="AUTHORIZATION: bearer ***" fetch --tags --prune --progress --no-recurse-submodules origin
From https://repohere
- [deleted] (none) -> origin/teams/some
remote: Azure Repos
remote:
remote: Found 1444 objects to send. (1323 ms)
Receiving objects: 0% (1/1444)
...
Resolving deltas: 100% (708/708), completed with 594 local objects.
7d80bdb9d646..5214d0492d27 features/DraggableDashboardGrid -> origin/features/DraggableDashboardGrid
...
...
##[group]
##[command]git checkout --progress --force e48a3009f2a0163d102423eef6ffaf7f4c2a2176
Warning: you are leaving 1 commit behind, not connected to
any of your branches:
cb1adf878a7b Update CloudStore packages to 0.1.0-20190213.7 and Domino packages to 0.1.0-20190213.7
If you want to keep it by creating a new branch, this may be a good time
to do so with:
git branch <new-branch-name> cb1adf878a7b
HEAD is now at e48a3009f2a0 update swe
##[group]
##[command]git config http.https://repohere "AUTHORIZATION: bearer ***"
```

Single grouping parsed -
```
Syncing repository: SomeRepo (Git)
Prepending Path environment variable with directory containing 'git.exe'.
> ##[command]git version
  ##[command]git config --get remote.origin.url
  ##[command]git clean -ffdx
> ##[command]git reset --hard HEAD
  ##[command]git config gc.auto 0
  ##[command]git config --get-all http.https://repohere
  ##[command]git config --get-all http.proxy
> ##[command]git -c http.extraheader="AUTHORIZATION: bearer ***" fetch --tags --prune --progress --no-recurse-submodules origin
> ##[command]git checkout --progress --force e48a3009f2a0163d102423eef6ffaf7f4c2a2176
  ##[command]git config http.https://repohere "AUTHORIZATION: bearer ***"
```
