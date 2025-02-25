# Contribute (Dev)

## Dev Dependencies

![Win](res/win_sm.png) Git for Windows [Install Here](https://git-scm.com/downloads) (needed for dev sh script)

## Build, Test, Layout 

From src:

![Win](res/win_sm.png) `dev {command}`  

![*nix](res/linux_sm.png) `./dev.sh {command}`
  
**Commands:**  

`layout` (`l`):  Run first time to create a full agent layout in {root}/{runtime_id}/_layout  

`build` (`b`):   build everything and update agent layout folder  

`test` (`t`):    build agent binaries, run unit tests applicable to the current platform

Normal dev flow:
```bash
git clone https://github.com/microsoft/azure-pipelines-agent
cd ./src
./dev.(sh/cmd) layout # the agent that build from source is in {root}/{runtime_id}/_layout
<make code changes>
./dev.(sh/cmd) build # {root}/{runtime_id}/_layout will get updated
./dev.(sh/cmd) test # run unit tests before git commit/push
```

To test the agent in a pipeline, follow the [self-hosted agent installation steps](https://learn.microsoft.com/en-us/azure/devops/pipelines/agents/windows-agent?view=azure-devops). You will use the agent built from source in the `_layout` folder at the repository root to run the `config` and `run` commands.

## Debugging

The agent can be run in debug mode by providing the parameter `--debug` to the `run` command.
This will make the agent recognize the following environment variables:

- `VSTSAGENT_DEBUG_TASK` - for remote debugging node-based pipeline tasks

Note that all of these variables need to be defined on the node that is used to run the agent.
Also, do not run production agents with this mode as it can cause pipelines to appear stuck.

### `VSTSAGENT_DEBUG_TASK` environment variable

When enabled, the agent will start the Node process with specific parameters. These parameters cause the process to wait for the debugger to attach before continuing with the execution of the pipeline task script. The value must be set to either:
- Task `id`, which is an unique GUID identifier to be found in `task.json` definition of the task
- Task `name` and major `version`, e.g. AzureCLIV2

Only one task can be debugged at one time and all other tasks in the same pipeline will proceed as usual.
If you wish to stop debugging this task either restart that agent without `--debug` option, or unset the variables from above.

## Editors

[Using Visual Studio 2017](https://www.visualstudio.com/vs/)  
[Using Visual Studio Code](https://code.visualstudio.com/)

## Styling

We use the dotnet foundation and CoreCLR style guidelines [located here](
https://github.com/dotnet/corefx/blob/master/Documentation/coding-guidelines/coding-style.md)

## Troubleshooting build or test problems

'unzip' not found
- if you see this while building or testing on Windows, you need to install unzip for the Windows bash shell
- open a command window, run bash, and run `sudo apt install unzip` to get that tool installed


