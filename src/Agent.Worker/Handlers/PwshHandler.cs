// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Handlers
{
    [ServiceLocator(Default = typeof(PwshHandler))]
    public interface IPwshHandler : IHandler
    {
        PwshHandlerData Data { get; set; }
    }

    public sealed class PwshHandler : Handler, IPwshHandler
    {
        public PwshHandlerData Data { get; set; }

        public async Task RunAsync()
        {
            Trace.Entering();
            ArgUtil.NotNull(Data, nameof(Data));
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));
            ArgUtil.NotNull(Inputs, nameof(Inputs));
            ArgUtil.Directory(TaskDirectory, nameof(TaskDirectory));

            AddInputsToEnvironment();
            AddEndpointsToEnvironment();
            AddSecureFilesToEnvironment();
            AddVariablesToEnvironment();
            AddTaskVariablesToEnvironment();
            AddPrependPathToEnvironment();
            if (PlatformUtil.RunningOnWindows)
            {
                RemovePSModulePathFromEnvironment();
            }

            string scriptFile = ResolveScriptFile();
            string scriptDirectory = Path.GetDirectoryName(scriptFile);
            string moduleFile = ResolveModuleFile(scriptDirectory);
            string pwshArgs = BuildPwshArguments(moduleFile, scriptFile);
            string pwsh = ResolvePwshExecutable();

            StepHost.OutputDataReceived += OnDataReceived;
            StepHost.ErrorDataReceived += OnDataReceived;

            var sigintTimeout = TimeSpan.FromMilliseconds(AgentKnobs.ProccessSigintTimeout.GetValue(ExecutionContext).AsInt());
            var sigtermTimeout = TimeSpan.FromMilliseconds(AgentKnobs.ProccessSigtermTimeout.GetValue(ExecutionContext).AsInt());
            var useGracefulShutdown = AgentKnobs.UseGracefulProcessShutdown.GetValue(ExecutionContext).AsBoolean();

            try
            {
                await StepHost.ExecuteAsync(
                    workingDirectory: StepHost.ResolvePathForStepHost(scriptDirectory),
                    fileName: pwsh,
                    arguments: pwshArgs,
                    environment: Environment,
                    requireExitCodeZero: true,
                    outputEncoding: null,
                    killProcessOnCancel: false,
                    inheritConsoleHandler: !ExecutionContext.Variables.Retain_Default_Encoding,
                    continueAfterCancelProcessTreeKillAttempt: _continueAfterCancelProcessTreeKillAttempt,
                    sigintTimeout: sigintTimeout,
                    sigtermTimeout: sigtermTimeout,
                    useGracefulShutdown: useGracefulShutdown,
                    cancellationToken: ExecutionContext.CancellationToken);
            }
            finally
            {
                StepHost.OutputDataReceived -= OnDataReceived;
                StepHost.ErrorDataReceived -= OnDataReceived;
            }
        }

        private void OnDataReceived(object sender, ProcessDataReceivedEventArgs e)
        {
            if (!CommandManager.TryProcessCommand(ExecutionContext, e.Data))
            {
                ExecutionContext.Output(e.Data);
            }
        }

        private string ResolveScriptFile()
        {
            ArgUtil.NotNullOrEmpty(Data.Target, nameof(Data.Target));
            string scriptFile = Path.Combine(TaskDirectory, Data.Target);
            ArgUtil.File(scriptFile, nameof(scriptFile));
            return scriptFile;
        }

        private string ResolveModuleFile(string scriptDirectory)
        {
            string moduleFile = Path.Combine(scriptDirectory, "ps_modules", "VstsTaskSdk", "VstsTaskSdk.psd1");
            ArgUtil.File(moduleFile, nameof(moduleFile));
            return moduleFile;
        }

        private string BuildPwshArguments(string moduleFile, string scriptFile)
        {
            if (AgentKnobs.UsePSScriptWrapper.GetValue(ExecutionContext).AsBoolean())
            {
                return BuildWrapperArguments(moduleFile, scriptFile);
            }

            return BuildDirectInvocationArguments(moduleFile, scriptFile);
        }

        private string BuildWrapperArguments(string moduleFile, string scriptFile)
        {
            return StringUtil.Format(
                @"-NoLogo -NoProfile -ExecutionPolicy Unrestricted -Command ""{3}"" -VstsSdkPath {0} -DebugOption {1} -ScriptBlockString ""{2}""",
                StepHost.ResolvePathForStepHost(moduleFile).Replace("'", "''"),
                ExecutionContext.Variables.System_Debug == true ? "Continue" : "SilentlyContinue",
                StepHost.ResolvePathForStepHost(scriptFile).Replace("'", "''''"),
                Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), "powershell", "Start-AzpTask.ps1"));
        }

        private string BuildDirectInvocationArguments(string moduleFile, string scriptFile)
        {
            return StringUtil.Format(
                @"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Unrestricted -Command "". ([scriptblock]::Create('if ([Console]::InputEncoding -is [Text.UTF8Encoding] -and [Console]::InputEncoding.GetPreamble().Length -ne 0) {{ [Console]::InputEncoding = New-Object Text.UTF8Encoding $false }}')) 2>&1 | ForEach-Object {{ Write-Verbose $_.Exception.Message -Verbose }} ; Import-Module -Name '{0}' -ArgumentList @{{ NonInteractive = $true }} -ErrorAction Stop ; $VerbosePreference = '{1}' ; $DebugPreference = '{1}' ; Invoke-VstsTaskScript -ScriptBlock ([scriptblock]::Create('. ''{2}'''))""",
                StepHost.ResolvePathForStepHost(moduleFile).Replace("'", "''"),
                ExecutionContext.Variables.System_Debug == true ? "Continue" : "SilentlyContinue",
                StepHost.ResolvePathForStepHost(scriptFile).Replace("'", "''''"));
        }

        private string ResolvePwshExecutable()
        {
            string pwsh = "pwsh";
            if (StepHost is DefaultStepHost)
            {
                pwsh = HostContext.GetService<IPwshExeUtil>().GetPath();
            }

            ArgUtil.NotNullOrEmpty(pwsh, nameof(pwsh));
            return pwsh;
        }
    }
}
