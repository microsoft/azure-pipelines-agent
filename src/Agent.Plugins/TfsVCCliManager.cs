// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Agent.Sdk;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using System.IO;
using Agent.Sdk.Knob;
using System.Linq;

namespace Agent.Plugins.Repository
{
    public abstract class TfsVCCliManager
    {
        public readonly Dictionary<string, string> AdditionalEnvironmentVariables = new Dictionary<string, string>();

        public CancellationToken CancellationToken { protected get; set; }

        public ServiceEndpoint Endpoint { protected get; set; }

        public Pipelines.RepositoryResource Repository { protected get; set; }

        public AgentTaskPluginExecutionContext ExecutionContext { protected get; set; }

        public abstract TfsVCFeatures Features { get; }

        public abstract Task<bool> TryWorkspaceDeleteAsync(ITfsVCWorkspace workspace);
        public abstract Task WorkspacesRemoveAsync(ITfsVCWorkspace workspace);

        protected virtual Encoding OutputEncoding => null;

        protected string SourceVersion
        {
            get
            {
                string version = Repository.Version;
                ArgUtil.NotNullOrEmpty(version, nameof(version));
                return version;
            }
        }

        protected string SourcesDirectory
        {
            get
            {
                string sourcesDirectory = Repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                ArgUtil.NotNullOrEmpty(sourcesDirectory, nameof(sourcesDirectory));
                return sourcesDirectory;
            }
        }

        protected abstract string Switch { get; }

        protected string WorkspaceName
        {
            get
            {
                string workspace = ExecutionContext.Variables.GetValueOrDefault("build.repository.tfvc.workspace")?.Value;
                ArgUtil.NotNullOrEmpty(workspace, nameof(workspace));
                return workspace;
            }
        }

        protected Task RunCommandAsync(params string[] args)
        {
            return RunCommandAsync(FormatTags.None, args);
        }

        protected Task RunCommandAsync(int retriesOnFailure, params string[] args)
        {
            return RunCommandAsync(FormatTags.None, false, retriesOnFailure, args);
        }

        protected Task RunCommandAsync(FormatTags formatFlags, params string[] args)
        {
            return RunCommandAsync(formatFlags, false, args);
        }

        protected Task RunCommandAsync(FormatTags formatFlags, bool quiet, params string[] args)
        {
            return RunCommandAsync(formatFlags, quiet, 0, args);
        }

        protected async Task RunCommandAsync(FormatTags formatFlags, bool quiet, int retriesOnFailure, params string[] args)
        {
            for (int attempt = 0; attempt < retriesOnFailure - 1; attempt++)
            {
                int exitCode = await RunCommandAsync(formatFlags, quiet, false, args);

                if (exitCode == 0)
                {
                    return;
                }

                int sleep = Math.Min(200 * (int)Math.Pow(5, attempt), 30000);
                ExecutionContext.Output($"Sleeping for {sleep} ms");
                await Task.Delay(sleep);

                // Use attempt+2 since we're using 0 based indexing and we're displaying this for the next attempt.
                ExecutionContext.Output($@"Retrying. Attempt {attempt + 2}/{retriesOnFailure}");
            }

            // Perform one last try and fail on non-zero exit code
            await RunCommandAsync(formatFlags, quiet, true, args);
        }

        private string WriteCommandToFile(string command)
        {
            Guid guid = Guid.NewGuid();
            string temporaryName = $"tfs_cmd_{guid}.txt";
            using StreamWriter sw = new StreamWriter(Path.Combine(this.SourcesDirectory, temporaryName));
            sw.WriteLine(command);
            return temporaryName;
        }

        protected async Task<int> RunCommandAsync(FormatTags formatFlags, bool quiet, bool failOnNonZeroExitCode, params string[] args)
        {
            // Validation.
            ArgUtil.NotNull(args, nameof(args));
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));

            // Invoke tf.
            using (var processInvoker = new ProcessInvoker(ExecutionContext))
            {
                var outputLock = new object();
                processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        if (quiet)
                        {
                            ExecutionContext.Debug(e.Data);
                        }
                        else
                        {
                            ExecutionContext.Output(e.Data);
                        }
                    }
                };
                processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        ExecutionContext.Output(e.Data);
                    }
                };
                string arguments = FormatArguments(formatFlags, args);
                bool useSecureParameterPassing = AgentKnobs.TfVCUseSecureParameterPassing.GetValue(ExecutionContext).AsBoolean();
                string temporaryFileWithCommand = "";
                if (useSecureParameterPassing)
                {
                    temporaryFileWithCommand = WriteCommandToFile(arguments);
                    arguments = $"@{temporaryFileWithCommand}";
                    ExecutionContext.Debug($"{AgentKnobs.TfVCUseSecureParameterPassing.Name} is enabled, passing command via file");
                }
                ExecutionContext.Command($@"tf {arguments}");

                var result = await processInvoker.ExecuteAsync(
                    workingDirectory: SourcesDirectory,
                    fileName: "tf",
                    arguments: arguments,
                    environment: AdditionalEnvironmentVariables,
                    requireExitCodeZero: failOnNonZeroExitCode,
                    outputEncoding: OutputEncoding,
                    cancellationToken: CancellationToken);

                if (useSecureParameterPassing)
                {
                    File.Delete(Path.Combine(this.SourcesDirectory, temporaryFileWithCommand));
                }

                return result;
            }
        }

        protected Task<string> RunPorcelainCommandAsync(FormatTags formatFlags, params string[] args)
        {
            return RunPorcelainCommandAsync(formatFlags, 0, args);
        }

        protected Task<string> RunPorcelainCommandAsync(params string[] args)
        {
            return RunPorcelainCommandAsync(FormatTags.None, 0, args);
        }

        protected Task<string> RunPorcelainCommandAsync(int retriesOnFailure, params string[] args)
        {
            return RunPorcelainCommandAsync(FormatTags.None, retriesOnFailure, args);
        }

        protected async Task<string> RunPorcelainCommandAsync(FormatTags formatFlags, int retriesOnFailure, params string[] args)
        {
            // Run the command.
            TfsVCPorcelainCommandResult result = await TryRunPorcelainCommandAsync(formatFlags, retriesOnFailure, args);
            ArgUtil.NotNull(result, nameof(result));
            if (result.Exception != null)
            {
                // The command failed. Dump the output and throw.
                result.Output?.ForEach(x => ExecutionContext.Output(x ?? string.Empty));
                throw result.Exception;
            }

            // Return the output.
            // Note, string.join gracefully handles a null element within the IEnumerable<string>.
            return string.Join(Environment.NewLine, result.Output ?? new List<string>());
        }

        protected async Task<TfsVCPorcelainCommandResult> TryRunPorcelainCommandAsync(FormatTags formatFlags, int retriesOnFailure, params string[] args)
        {
            var result = await TryRunPorcelainCommandAsync(formatFlags, args);
            for (int attempt = 0; attempt < retriesOnFailure && result.Exception != null && result.Exception?.ExitCode != 1; attempt++)
            {
                ExecutionContext.Warning($"{result.Exception.Message}");
                int sleep = Math.Min(200 * (int)Math.Pow(5, attempt), 30000);
                ExecutionContext.Output($"Sleeping for {sleep} ms before starting {attempt + 1}/{retriesOnFailure} retry");
                await Task.Delay(sleep);
                result = await TryRunPorcelainCommandAsync(formatFlags, args);
            }
            return result;
        }

        protected async Task<TfsVCPorcelainCommandResult> TryRunPorcelainCommandAsync(FormatTags formatFlags, params string[] args)
        {
            // Validation.
            ArgUtil.NotNull(args, nameof(args));
            ArgUtil.NotNull(ExecutionContext, nameof(ExecutionContext));

            // Invoke tf.
            using (var processInvoker = new ProcessInvoker(ExecutionContext))
            {
                var result = new TfsVCPorcelainCommandResult();
                var outputLock = new object();
                processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        ExecutionContext.Debug(e.Data);
                        result.Output.Add(e.Data);
                    }
                };
                processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        ExecutionContext.Debug(e.Data);
                        result.Output.Add(e.Data);
                    }
                };
                string formattedArguments = FormatArguments(formatFlags, args);
                string arguments = "";
                string cmdFileName = "";
                bool useSecretParameterPassing = AgentKnobs.TfVCUseSecureParameterPassing.GetValue(ExecutionContext).AsBoolean();
                if (useSecretParameterPassing)
                {
                    cmdFileName = WriteCommandToFile(formattedArguments);
                    arguments = $"@{cmdFileName}";
                }
                else
                {
                    arguments = formattedArguments;
                }

                ExecutionContext.Debug($@"tf {arguments}");
                // TODO: Test whether the output encoding needs to be specified on a non-Latin OS.
                try
                {
                    await processInvoker.ExecuteAsync(
                        workingDirectory: SourcesDirectory,
                        fileName: "tf",
                        arguments: arguments,
                        environment: AdditionalEnvironmentVariables,
                        requireExitCodeZero: true,
                        outputEncoding: OutputEncoding,
                        cancellationToken: CancellationToken);
                }
                catch (ProcessExitCodeException ex)
                {
                    result.Exception = ex;
                }

                if (useSecretParameterPassing)
                {
                    CleanupTfsVCOutput(ref result, formattedArguments);
                    File.Delete(Path.Combine(this.SourcesDirectory, cmdFileName));
                }

                return result;
            }
        }

        private void CleanupTfsVCOutput(ref TfsVCPorcelainCommandResult command, string executedCommand)
        {
            // tf.exe removes double quotes from the output, we also replace it in the input command to correctly find the extra output
            List<string> stringsToRemove = command
                .Output
                .Where(item => item.Contains(executedCommand.Replace("\"", "")))
                .ToList();
            command.Output.RemoveAll(item => stringsToRemove.Contains(item));
        }

        private string FormatArguments(FormatTags formatFlags, params string[] args)
        {
            // Validation.
            ArgUtil.NotNull(args, nameof(args));
            ArgUtil.NotNull(Endpoint, nameof(Endpoint));
            ArgUtil.NotNull(Endpoint.Authorization, nameof(Endpoint.Authorization));
            ArgUtil.NotNull(Endpoint.Authorization.Parameters, nameof(Endpoint.Authorization.Parameters));
            ArgUtil.Equal(EndpointAuthorizationSchemes.OAuth, Endpoint.Authorization.Scheme, nameof(Endpoint.Authorization.Scheme));
            string accessToken = Endpoint.Authorization.Parameters.TryGetValue(EndpointAuthorizationParameters.AccessToken, out accessToken) ? accessToken : null;
            ArgUtil.NotNullOrEmpty(accessToken, EndpointAuthorizationParameters.AccessToken);
            ArgUtil.NotNull(Repository.Url, nameof(Repository.Url));

            // Format each arg.
            var formattedArgs = new List<string>();
            foreach (string arg in args ?? new string[0])
            {
                // Validate the arg.
                if (!string.IsNullOrEmpty(arg) && arg.IndexOfAny(new char[] { '"', '\r', '\n' }) >= 0)
                {
                    throw new Exception(StringUtil.Loc("InvalidCommandArg", arg));
                }

                // Add the arg.
                formattedArgs.Add(arg != null && arg.Contains(" ") ? $@"""{arg}""" : $"{arg}");
            }

            // Add the common parameters.
            if (!formatFlags.HasFlag(FormatTags.OmitCollectionUrl))
            {
                if (Features.HasFlag(TfsVCFeatures.EscapedUrl))
                {
                    formattedArgs.Add($"{Switch}collection:{Repository.Url.AbsoluteUri}");
                }
                else
                {
                    // TEE CLC expects the URL in unescaped form.
                    string url;
                    try
                    {
                        url = Uri.UnescapeDataString(Repository.Url.AbsoluteUri);
                    }
                    catch (Exception ex)
                    {
                        // Unlikely (impossible?), but don't fail if encountered. If we don't hear complaints
                        // about this warning then it is likely OK to remove the try/catch altogether and have
                        // faith that UnescapeDataString won't throw for this scenario.
                        url = Repository.Url.AbsoluteUri;
                        ExecutionContext.Warning($"{ex.Message} ({url})");
                    }

                    formattedArgs.Add($"\"{Switch}collection:{url}\"");
                }
            }

            if (!formatFlags.HasFlag(FormatTags.OmitLogin))
            {
                if (Features.HasFlag(TfsVCFeatures.LoginType))
                {
                    formattedArgs.Add($"{Switch}loginType:OAuth");
                    formattedArgs.Add($"{Switch}login:.,{accessToken}");
                }
                else
                {
                    formattedArgs.Add($"{Switch}jwt:{accessToken}");
                }
            }

            if (!formatFlags.HasFlag(FormatTags.OmitNoPrompt))
            {
                formattedArgs.Add($"{Switch}noprompt");
            }

            return string.Join(" ", formattedArgs);
        }

        [Flags]
        protected enum FormatTags
        {
            None = 0,
            OmitCollectionUrl = 1,
            OmitLogin = 2,
            OmitNoPrompt = 4,
            All = OmitCollectionUrl | OmitLogin | OmitNoPrompt,
        }
    }

    [Flags]
    public enum TfsVCFeatures
    {
        None = 0,

        // Indicates whether "workspace /new" adds a default mapping.
        DefaultWorkfoldMap = 1,

        // Indicates whether the CLI accepts the collection URL in escaped form.
        EscapedUrl = 2,

        // Indicates whether the "eula" subcommand is supported.
        Eula = 4,

        // Indicates whether the "get" and "undo" subcommands will correctly resolve
        // the workspace from an unmapped root folder. For example, if a workspace
        // contains only two mappings, $/foo -> $(build.sourcesDirectory)\foo and
        // $/bar -> $(build.sourcesDirectory)\bar, then "tf get $(build.sourcesDirectory)"
        // will not be able to resolve the workspace unless this feature is supported.
        GetFromUnmappedRoot = 8,

        // Indicates whether the "loginType" parameter is supported.
        LoginType = 16,

        // Indicates whether the "scorch" subcommand is supported.
        Scorch = 32,
    }

    public sealed class TfsVCPorcelainCommandResult
    {
        public TfsVCPorcelainCommandResult()
        {
            Output = new List<string>();
        }

        public ProcessExitCodeException Exception { get; set; }

        public List<string> Output { get; }
    }

    ////////////////////////////////////////////////////////////////////////////////
    // tf shelvesets interfaces.
    ////////////////////////////////////////////////////////////////////////////////
    public interface ITfsVCShelveset
    {
        string Comment { get; }
    }

    ////////////////////////////////////////////////////////////////////////////////
    // tf status interfaces.
    ////////////////////////////////////////////////////////////////////////////////
    public interface ITfsVCStatus
    {
        IEnumerable<ITfsVCPendingChange> AllAdds { get; }
        bool HasPendingChanges { get; }
    }

    public interface ITfsVCPendingChange
    {
        string LocalItem { get; }
    }

    ////////////////////////////////////////////////////////////////////////////////
    // tf workspaces interfaces.
    ////////////////////////////////////////////////////////////////////////////////
    public interface ITfsVCWorkspace
    {
        string Computer { get; set; }

        string Name { get; }

        string Owner { get; }

        ITfsVCMapping[] Mappings { get; }
    }

    public interface ITfsVCMapping
    {
        bool Cloak { get; }

        string LocalPath { get; }

        bool Recursive { get; }

        string ServerPath { get; }
    }
}
