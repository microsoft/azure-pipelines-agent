
# Log decorations


Task authors should be able to control how the log output is displayed to the end user.
This outlines different decoration options that are available.

## Special lines
* Errors
  * `##[error] I am an error` 
* Warnings
  * `##[warning] I am a warning` 
* Commands
  * `##[warning] I am a command/a tool` 
* Sections
  * `##[section] I am a section, which is usually whole task step. I am not typically used.` 

## Collapse

>This is a draft spec and is subject to change

>Note that that if you log an error using ```##vso[task.logissue]error/warning message``` command (see [logging commands](https://github.com/Microsoft/azure-pipelines-tasks/blob/master/docs/authoring/commands.md) here) we will surface those errors in build view and when clicked , we will automatically jump to that particular line. If it's already part of a group, we will auto-expand the group.

Task authors can mark any part of the log as a collapsible region using these decorations:

Starting the collapsible region - `##[startgroup<:optionalGroupName>]`

Ending the collapsible region - `##[endgroup<:optionalGroupName>]`

`<optionalGroupName>` is optional.

Note: 
    
*  The first line of region will be taken as group title by default.
*  If there's only one line in the region (including the group title), it will not be considered as a collapsible
*  If there's `##[startgroup]` with out corresponding `##[endgroup]` we will add implicit `##[endgroup]`
   *  This applies to only non-named groups, if group name is specified, no implcit  `##[endgroup]` is considered until the end of the content.

Example 1 -


```
##[startgroup]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-d9e5386068c8.cmd""
Write your commands here
Use the environment variables input below to pass secret variables to this script
##[startgroup:command1]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c8.cmd""
This is command 2
##[endgroup:command1]
##[startgroup:command2]
##[command]"C:\WINDOWS\system32\cmd.exe" /D /E:ON /V:OFF /S /C "CALL "C:\_temp\e51ecc3a-f080-4f7c-9bf5-f9e5386068c9.cmd""
##[endgroup:command2]
##[startgroup:noendgroup]
I started a group with out end
##[startgroup]
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
##[startgroup]
##[command]git version
git version 2.18.0.windows.1
##[startgroup]
##[command]git config --get remote.origin.url
##[startgroup]
##[command]git clean -ffdx
##[startgroup]
##[command]git reset --hard HEAD
##[startgroup]
HEAD is now at cb1adf878a7b update swe
##[startgroup]
##[command]git config gc.auto 0
##[startgroup]
##[command]git config --get-all http.https://repohere
##[startgroup]
##[command]git config --get-all http.proxy
##[startgroup]
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
##[startgroup]
##[command]git checkout --progress --force e48a3009f2a0163d102423eef6ffaf7f4c2a2176
Warning: you are leaving 1 commit behind, not connected to
any of your branches:
cb1adf878a7b Update CloudStore packages to 0.1.0-20190213.7 and Domino packages to 0.1.0-20190213.7
If you want to keep it by creating a new branch, this may be a good time
to do so with:
git branch <new-branch-name> cb1adf878a7b
HEAD is now at e48a3009f2a0 update swe
##[startgroup]
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

Nested grouping (no visual intendation) -
```
Syncing repository: SomeRepo (Git)
Prepending Path environment variable with directory containing 'git.exe'.
##[startgroup:git]
##[startgroup]
##[command]git version
git version 2.18.0.windows.1
##[startgroup]
##[command]git config --get remote.origin.url
##[startgroup]
##[command]git clean -ffdx
##[startgroup]
##[command]git reset --hard HEAD
##[startgroup]
HEAD is now at cb1adf878a7b update swe
##[startgroup]
##[command]git config gc.auto 0
##[startgroup]
##[command]git config --get-all http.https://repohere
##[startgroup]
##[command]git config --get-all http.proxy
##[startgroup]
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
##[startgroup]
##[command]git checkout --progress --force e48a3009f2a0163d102423eef6ffaf7f4c2a2176
Warning: you are leaving 1 commit behind, not connected to
any of your branches:
cb1adf878a7b Update CloudStore packages to 0.1.0-20190213.7 and Domino packages to 0.1.0-20190213.7
If you want to keep it by creating a new branch, this may be a good time
to do so with:
git branch <new-branch-name> cb1adf878a7b
HEAD is now at e48a3009f2a0 update swe
##[startgroup]
##[command]git config http.https://repohere "AUTHORIZATION: bearer ***"
##[endgroup:git]
```

Multiple grouping parsed -
```
Syncing repository: SomeRepo (Git)
Prepending Path environment variable with directory containing 'git.exe'.
> Git commands
```

```
Syncing repository: SomeRepo (Git)
Prepending Path environment variable with directory containing 'git.exe'.
v Git commands
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

### Open questions
* Can our tool runner automatically mark the end of output for a command in a reliable way? This way all of the tools that run will get collapsible regions for free.
