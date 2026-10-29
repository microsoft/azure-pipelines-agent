$ErrorActionPreference = 'Stop'

# Bootstrap dependencies and layout for agent builds/tests.
& "$PSScriptRoot\..\src\dev.cmd" layout
