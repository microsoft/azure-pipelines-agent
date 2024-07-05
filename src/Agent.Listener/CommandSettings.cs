// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Agent.Listener.CommandLine;
using Agent.Sdk;
using Agent.Sdk.Util;
using CommandLine;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Runtime.Versioning;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    public sealed class CommandSettings
    {
        private readonly Dictionary<string, string> _envArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IPromptManager _promptManager;
        private readonly Tracing _trace;

        // Accepted Commands
        private Type[] verbTypes = new Type[]
        {
            typeof(ConfigureAgent),
            typeof(RunAgent),
            typeof(RemoveAgent),
            typeof(WarmupAgent),
        };

        private string[] verbCommands = new string[]
        {
            Constants.Agent.CommandLine.Commands.Configure,
            Constants.Agent.CommandLine.Commands.Remove,
            Constants.Agent.CommandLine.Commands.Run,
            Constants.Agent.CommandLine.Commands.Warmup,
        };


        // Commands
        private ConfigureAgent Configure { get; set; }
        private RemoveAgent Remove { get; set; }
        private RunAgent Run { get; set; }
        private WarmupAgent Warmup { get; set; }

        public IEnumerable<Error> ParseErrors { get; set; }


        // Constructor.
        public CommandSettings(IHostContext context, string[] args, IScopedEnvironment environmentScope = null)
        {
            ArgUtil.NotNull(context, nameof(context));
            _promptManager = context.GetService<IPromptManager>();
            _trace = context.GetTrace(nameof(CommandSettings));

            ParseArguments(args);

            if (environmentScope == null)
            {
                environmentScope = new SystemEnvironment();
            }

            // Mask secret arguments
            if (Configure != null)
            {
                context.SecretMasker.AddValue(Configure.Password, WellKnownSecretAliases.ConfigurePassword);
                context.SecretMasker.AddValue(Configure.ProxyPassword, WellKnownSecretAliases.ConfigureProxyPassword);
                context.SecretMasker.AddValue(Configure.SslClientCert, WellKnownSecretAliases.ConfigureSslClientCert);
                context.SecretMasker.AddValue(Configure.Token, WellKnownSecretAliases.ConfigureToken);
                context.SecretMasker.AddValue(Configure.WindowsLogonPassword, WellKnownSecretAliases.ConfigureWindowsLogonPassword);
            }

            if (Remove != null)
            {
                context.SecretMasker.AddValue(Remove.Password, WellKnownSecretAliases.RemovePassword);
                context.SecretMasker.AddValue(Remove.Token, WellKnownSecretAliases.RemoveToken);
            }

            PrintArguments();

            // Store and remove any args passed via environment variables.
            var environment = environmentScope.GetEnvironmentVariables();

            string envPrefix = "VSTS_AGENT_INPUT_";
            foreach (DictionaryEntry entry in environment)
            {
                // Test if starts with VSTS_AGENT_INPUT_.
                string fullKey = entry.Key as string ?? string.Empty;
                if (fullKey.StartsWith(envPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string val = (entry.Value as string ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        // Extract the name.
                        string name = fullKey.Substring(envPrefix.Length);

                        // Mask secrets.
                        bool secret = Constants.Agent.CommandLine.Args.Secrets.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                        if (secret)
                        {
                            context.SecretMasker.AddValue(val, $"CommandSettings_{fullKey}");
                        }

                        // Store the value.
                        _envArgs[name] = val;
                    }

                    // Remove from the environment block.
                    _trace.Info($"Removing env var: '{fullKey}'");
                    environmentScope.SetEnvironmentVariable(fullKey, null);
                }
            }
        }

        //
        // Interactive flags.
        //
        public bool GetAcceptTeeEula()
        {
            return TestFlagOrPrompt(
                value: Configure?.AcceptTeeEula,
                name: Constants.Agent.CommandLine.Flags.AcceptTeeEula,
                description: StringUtil.Loc("AcceptTeeEula"),
                defaultValue: false);
        }

        public bool GetAlwaysExtractTask()
        {
            return TestFlag(Configure?.AlwaysExtractTask, Constants.Agent.CommandLine.Flags.AlwaysExtractTask);
        }

        public bool GetReplace()
        {
            return TestFlagOrPrompt(
                value: Configure?.Replace,
                name: Constants.Agent.CommandLine.Flags.Replace,
                description: StringUtil.Loc("Replace"),
                defaultValue: false);
        }

        public bool GetRunAsService()
        {
            return TestFlagOrPrompt(
                value: Configure?.RunAsService,
                name: Constants.Agent.CommandLine.Flags.RunAsService,
                description: StringUtil.Loc("RunAgentAsServiceDescription"),
                defaultValue: false);
        }

        public bool GetPreventServiceStart()
        {
            return TestFlagOrPrompt(
                value: Configure?.PreventServiceStart,
                name: Constants.Agent.CommandLine.Flags.PreventServiceStart,
                description: StringUtil.Loc("PreventServiceStartDescription"),
                defaultValue: false
            );
        }

        public bool GetRunAsAutoLogon()
        {
            return TestFlagOrPrompt(
                value: Configure?.RunAsAutoLogon,
                name: Constants.Agent.CommandLine.Flags.RunAsAutoLogon,
                description: StringUtil.Loc("RunAsAutoLogonDescription"),
                defaultValue: false);
        }

        public bool GetOverwriteAutoLogon(string logonAccount)
        {
            return TestFlagOrPrompt(
                value: Configure?.OverwriteAutoLogon,
                name: Constants.Agent.CommandLine.Flags.OverwriteAutoLogon,
                description: StringUtil.Loc("OverwriteAutoLogon", logonAccount),
                defaultValue: false);
        }

        public bool GetNoRestart()
        {
            return TestFlagOrPrompt(
                value: Configure?.NoRestart,
                name: Constants.Agent.CommandLine.Flags.NoRestart,
                description: StringUtil.Loc("NoRestart"),
                defaultValue: false);
        }

        public bool GetDeploymentGroupTagsRequired()
        {
            return TestFlag(Configure?.AddMachineGroupTags, Constants.Agent.CommandLine.Flags.AddMachineGroupTags)
                   || TestFlagOrPrompt(
                       value: Configure?.AddDeploymentGroupTags,
                       name: Constants.Agent.CommandLine.Flags.AddDeploymentGroupTags,
                       description: StringUtil.Loc("AddDeploymentGroupTagsFlagDescription"),
                       defaultValue: false);
        }

        public bool GetAutoLaunchBrowser()
        {
            return TestFlagOrPrompt(
                value: GetConfigureOrRemoveBase()?.LaunchBrowser,
                name: Constants.Agent.CommandLine.Flags.LaunchBrowser,
                description: StringUtil.Loc("LaunchBrowser"),
                defaultValue: true);
        }

        public string GetClientId()
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase()?.ClientId,
                name: Constants.Agent.CommandLine.Args.ClientId,
                description: StringUtil.Loc("ClientId"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        public string GetClientSecret()
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase()?.ClientSecret,
                name: Constants.Agent.CommandLine.Args.ClientSecret,
                description: StringUtil.Loc("ClientSecret"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        public string GetTenantId()
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase()?.TenantId,
                name: Constants.Agent.CommandLine.Args.TenantId,
                description: StringUtil.Loc("TenantId"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        /// <summary>
        /// Returns EnableServiceSidTypeUnrestricted flag or prompts user to set it up
        /// </summary>
        /// <returns>Parameter value</returns>
        public bool GetEnableServiceSidTypeUnrestricted()
        {
            return TestFlagOrPrompt(
                value: Configure?.EnableServiceSidTypeUnrestricted,
                name: Constants.Agent.CommandLine.Flags.EnableServiceSidTypeUnrestricted,
                description: StringUtil.Loc("EnableServiceSidTypeUnrestricted"),
                defaultValue: false);
        }
        //
        // Args.
        //
        public string GetAgentName()
        {
            return GetArgOrPrompt(
                argValue: Configure?.Agent,
                name: Constants.Agent.CommandLine.Args.Agent,
                description: StringUtil.Loc("AgentName"),
                defaultValue: Environment.MachineName ?? "myagent",
                validator: Validators.NonEmptyValidator);
        }

        public string GetAuth(string defaultValue)
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase().Auth,
                name: Constants.Agent.CommandLine.Args.Auth,
                description: StringUtil.Loc("AuthenticationType"),
                defaultValue: defaultValue,
                validator: Validators.AuthSchemeValidator);
        }

        public string GetPassword()
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase()?.Password,
                name: Constants.Agent.CommandLine.Args.Password,
                description: StringUtil.Loc("Password"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        public string GetPool()
        {
            return GetArgOrPrompt(
                argValue: Configure?.Pool,
                name: Constants.Agent.CommandLine.Args.Pool,
                description: StringUtil.Loc("AgentMachinePoolNameLabel"),
                defaultValue: "default",
                validator: Validators.NonEmptyValidator);
        }

        public string GetToken()
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase()?.Token,
                name: Constants.Agent.CommandLine.Args.Token,
                description: StringUtil.Loc("PersonalAccessToken"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        public string GetUrl(bool suppressPromptIfEmpty = false)
        {
            // Note, GetArg does not consume the arg (like GetArgOrPrompt does).
            if (suppressPromptIfEmpty &&
                string.IsNullOrEmpty(GetArg(Configure?.Url, Constants.Agent.CommandLine.Args.Url)))
            {
                return string.Empty;
            }

            return GetArgOrPrompt(
                argValue: Configure?.Url,
                name: Constants.Agent.CommandLine.Args.Url,
                description: StringUtil.Loc("ServerUrl"),
                defaultValue: string.Empty,
                validator: Validators.ServerUrlValidator);
        }

        public string GetDeploymentGroupName()
        {
            var result = GetArg(Configure?.MachineGroupName, Constants.Agent.CommandLine.Args.MachineGroupName);
            if (string.IsNullOrEmpty(result))
            {
                return GetArgOrPrompt(
                           argValue: Configure?.DeploymentGroupName,
                           name: Constants.Agent.CommandLine.Args.DeploymentGroupName,
                           description: StringUtil.Loc("DeploymentGroupName"),
                           defaultValue: string.Empty,
                           validator: Validators.NonEmptyValidator);
            }
            return result;
        }

        public string GetDeploymentPoolName()
        {
            return GetArgOrPrompt(
                argValue: Configure?.DeploymentPoolName,
                name: Constants.Agent.CommandLine.Args.DeploymentPoolName,
                description: StringUtil.Loc("DeploymentPoolName"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        public string GetProjectName(string defaultValue)
        {
            return GetArgOrPrompt(
                argValue: Configure?.ProjectName,
                name: Constants.Agent.CommandLine.Args.ProjectName,
                description: StringUtil.Loc("ProjectName"),
                defaultValue: defaultValue,
                validator: Validators.NonEmptyValidator);
        }

        public string GetCollectionName()
        {
            return GetArgOrPrompt(
                argValue: Configure?.CollectionName,
                name: Constants.Agent.CommandLine.Args.CollectionName,
                description: StringUtil.Loc("CollectionName"),
                defaultValue: "DefaultCollection",
                validator: Validators.NonEmptyValidator);
        }

        public string GetDeploymentGroupTags()
        {
            var result = GetArg(Configure?.MachineGroupTags, Constants.Agent.CommandLine.Args.MachineGroupTags);
            if (string.IsNullOrEmpty(result))
            {
                return GetArgOrPrompt(
                    argValue: Configure?.DeploymentGroupTags,
                    name: Constants.Agent.CommandLine.Args.DeploymentGroupTags,
                    description: StringUtil.Loc("DeploymentGroupTags"),
                    defaultValue: string.Empty,
                    validator: Validators.NonEmptyValidator);
            }
            return result;
        }

        // Environments

        public string GetEnvironmentName()
        {
            var result = GetArg(Configure?.EnvironmentName, Constants.Agent.CommandLine.Args.EnvironmentName);
            if (string.IsNullOrEmpty(result))
            {
                return GetArgOrPrompt(
                    argValue: Configure?.EnvironmentName,
                    name: Constants.Agent.CommandLine.Args.EnvironmentName,
                    description: StringUtil.Loc("EnvironmentName"),
                    defaultValue: string.Empty,
                    validator: Validators.NonEmptyValidator);
            }
            return result;
        }

        public bool GetEnvironmentVirtualMachineResourceTagsRequired()
        {
            return TestFlag(Configure?.AddEnvironmentVirtualMachineResourceTags, Constants.Agent.CommandLine.Flags.AddEnvironmentVirtualMachineResourceTags)
                   || TestFlagOrPrompt(
                           value: Configure?.AddEnvironmentVirtualMachineResourceTags,
                           name: Constants.Agent.CommandLine.Flags.AddEnvironmentVirtualMachineResourceTags,
                           description: StringUtil.Loc("AddEnvironmentVMResourceTags"),
                           defaultValue: false);
        }

        public string GetEnvironmentVirtualMachineResourceTags()
        {
            var result = GetArg(Configure?.EnvironmentVMResourceTags, Constants.Agent.CommandLine.Args.EnvironmentVMResourceTags);
            if (string.IsNullOrEmpty(result))
            {
                return GetArgOrPrompt(
                    argValue: Configure?.EnvironmentVMResourceTags,
                    name: Constants.Agent.CommandLine.Args.EnvironmentVMResourceTags,
                    description: StringUtil.Loc("EnvironmentVMResourceTags"),
                    defaultValue: string.Empty,
                    validator: Validators.NonEmptyValidator);
            }
            return result;
        }

        public string GetUserName()
        {
            return GetArgOrPrompt(
                argValue: GetConfigureOrRemoveBase()?.UserName,
                name: Constants.Agent.CommandLine.Args.UserName,
                description: StringUtil.Loc("UserName"),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        [SupportedOSPlatform("windows")]
        public string GetWindowsLogonAccount(string defaultValue, string descriptionMsg)
        {
            return GetArgOrPrompt(
                argValue: Configure?.WindowsLogonAccount,
                name: Constants.Agent.CommandLine.Args.WindowsLogonAccount,
                description: descriptionMsg,
                defaultValue: defaultValue,
                validator: Validators.NTAccountValidator);
        }

        public string GetWindowsLogonPassword(string accountName)
        {
            return GetArgOrPrompt(
                argValue: Configure?.WindowsLogonPassword,
                name: Constants.Agent.CommandLine.Args.WindowsLogonPassword,
                description: StringUtil.Loc("WindowsLogonPasswordDescription", accountName),
                defaultValue: string.Empty,
                validator: Validators.NonEmptyValidator);
        }

        public string GetWork()
        {
            return GetArgOrPrompt(
                argValue: Configure?.Work,
                name: Constants.Agent.CommandLine.Args.Work,
                description: StringUtil.Loc("WorkFolderDescription"),
                defaultValue: Constants.Path.WorkDirectory,
                validator: Validators.NonEmptyValidator);
        }

        public string GetMonitorSocketAddress()
        {
            return GetArg(Configure?.MonitorSocketAddress, Constants.Agent.CommandLine.Args.MonitorSocketAddress);
        }

        public string GetNotificationPipeName()
        {
            return GetArg(Configure?.NotificationPipeName, Constants.Agent.CommandLine.Args.NotificationPipeName);
        }

        public string GetNotificationSocketAddress()
        {
            return GetArg(Configure?.NotificationSocketAddress, Constants.Agent.CommandLine.Args.NotificationSocketAddress);
        }

        // This is used to find out the source from where the agent.listener.exe was launched at the time of run
        public string GetStartupType()
        {
            return GetArg(Run?.StartupType, Constants.Agent.CommandLine.Args.StartupType);
        }

        public string GetProxyUrl()
        {
            return GetArg(Configure?.ProxyUrl, Constants.Agent.CommandLine.Args.ProxyUrl);
        }

        public string GetProxyUserName()
        {
            return GetArg(Configure?.ProxyUserName, Constants.Agent.CommandLine.Args.ProxyUserName);
        }

        public string GetProxyPassword()
        {
            return GetArg(Configure?.ProxyPassword, Constants.Agent.CommandLine.Args.ProxyPassword);
        }

        public bool GetSkipCertificateValidation()
        {
            return TestFlag(Configure?.SslSkipCertValidation, Constants.Agent.CommandLine.Flags.SslSkipCertValidation);
        }

        public string GetCACertificate()
        {
            return GetArg(Configure?.SslCACert, Constants.Agent.CommandLine.Args.SslCACert);
        }

        public string GetClientCertificate()
        {
            return GetArg(Configure?.SslClientCert, Constants.Agent.CommandLine.Args.SslClientCert);
        }

        public string GetClientCertificatePrivateKey()
        {
            return GetArg(Configure?.SslClientCertKey, Constants.Agent.CommandLine.Args.SslClientCertKey);
        }

        public string GetClientCertificateArchrive()
        {
            return GetArg(Configure?.SslClientCertArchive, Constants.Agent.CommandLine.Args.SslClientCertArchive);
        }

        public string GetClientCertificatePassword()
        {
            return GetArg(Configure?.SslClientCertPassword, Constants.Agent.CommandLine.Args.SslClientCertPassword);
        }

        public bool GetGitUseSChannel()
        {
            return TestFlag(Configure?.GitUseSChannel, Constants.Agent.CommandLine.Flags.GitUseSChannel);
        }

        public bool GetEnvironmentVMResource()
        {
            return TestFlag(Configure?.EnvironmentVMResource, Constants.Agent.CommandLine.Flags.Environment);
        }

        public bool GetRunOnce()
        {
            return TestFlag(Configure?.RunOnce, Constants.Agent.CommandLine.Flags.Once) ||
                   TestFlag(Run?.RunOnce, Constants.Agent.CommandLine.Flags.Once);
        }

        public bool GetDebugMode()
        {
            return TestFlag(Run?.DebugMode, Constants.Agent.CommandLine.Flags.DebugMode);
        }

        public bool GetDeploymentPool()
        {
            return TestFlag(Configure?.DeploymentPool, Constants.Agent.CommandLine.Flags.DeploymentPool);
        }

        public bool GetDeploymentOrMachineGroup()
        {
            if (TestFlag(Configure?.DeploymentGroup, Constants.Agent.CommandLine.Flags.DeploymentGroup) ||
                (Configure?.MachineGroup == true))
            {
                return true;
            }

            return false;
        }

        public bool GetDisableLogUploads()
        {
            return TestFlag(Configure?.DisableLogUploads, Constants.Agent.CommandLine.Flags.DisableLogUploads);
        }

        public bool Unattended()
        {
            if (TestFlag(GetConfigureOrRemoveBase()?.Unattended, Constants.Agent.CommandLine.Flags.Unattended))
            {
                return true;
            }

            return false;
        }

        //
        // Command Checks
        //
        public bool IsRunCommand()
        {
            if (Run != null)
            {
                return true;
            }

            return false;
        }

        public bool IsVersion()
        {
            if ((Configure?.Version == true) ||
                (Remove?.Version == true) ||
                (Run?.Version == true) ||
                (Warmup?.Version == true))
            {
                return true;
            }

            return false;
        }

        public bool IsHelp()
        {
            if ((Configure?.Help == true) ||
                (Remove?.Help == true) ||
                (Run?.Help == true) ||
                (Warmup?.Help == true))
            {
                return true;
            }

            return false;
        }

        public bool IsCommit()
        {
            return (Run?.Commit == true);
        }

        public bool IsDiagnostics()
        {
            return (Run?.Diagnostics == true);
        }

        public bool IsConfigureCommand()
        {
            if (Configure != null)
            {
                return true;
            }

            return false;
        }

        public bool IsRemoveCommand()
        {
            if (Remove != null)
            {
                return true;
            }

            return false;
        }

        public bool IsWarmupCommand()
        {
            if (Warmup != null)
            {
                return true;
            }

            return false;
        }


        //
        // Private helpers.
        //
        private string GetArg(string value, string envName)
        {
            if (value == null)
            {
                value = GetEnvArg(envName);
            }

            return value;
        }

        private string GetArgOrPrompt(
            string argValue,
            string name,
            string description,
            string defaultValue,
            Func<string, bool> validator)
        {
            // Check for the arg in the command line parser.
            ArgUtil.NotNull(validator, nameof(validator));
            string result = GetArg(argValue, name);

            // Return the arg if it is not empty and is valid.
            _trace.Info($"Arg '{name}': '{result}'");
            if (!string.IsNullOrEmpty(result))
            {
                if (validator(result))
                {
                    return result;
                }

                _trace.Info("Arg is invalid.");
            }

            // Otherwise prompt for the arg.
            return _promptManager.ReadValue(
                argName: name,
                description: description,
                secret: Constants.Agent.CommandLine.Args.Secrets.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)),
                defaultValue: defaultValue,
                validator: validator,
                unattended: Unattended());
        }

        private string GetEnvArg(string name)
        {
            string val;
            if (_envArgs.TryGetValue(name, out val) && !string.IsNullOrEmpty(val))
            {
                _trace.Info($"Env arg '{name}': '{val}'");
                return val;
            }

            return null;
        }

        private bool TestFlag(bool? value, string name)
        {
            bool result = false;

            if (value == null || value == false)
            {
                string envStr = GetEnvArg(name);
                if (!bool.TryParse(envStr, out result))
                {
                    result = false;
                }
            }
            else
            {
                result = true;
            }

            _trace.Info($"Flag '{name}': '{result}'");
            return result;
        }

        private bool TestFlagOrPrompt(
            bool? value,
            string name,
            string description,
            bool defaultValue)
        {
            bool result = TestFlag(value, name);
            if (!result)
            {
                result = _promptManager.ReadBool(
                    argName: name,
                    description: description,
                    defaultValue: defaultValue,
                    unattended: Unattended());
            }

            return result;
        }

        private string[] AddDefaultVerbIfNecessary(string[] args)
        {
            if (args.Length == 0)
            {
                return new string[] { Constants.Agent.CommandLine.Commands.Run };
            }

            // Add default verb "Run" at front if we are given flags / options
            if (!verbCommands.Any(str => str.Contains(args[0])) && args[0].StartsWith("--"))
            {
                string[] newArgs = new string[args.Length + 1];
                newArgs[0] = Constants.Agent.CommandLine.Commands.Run;
                Array.Copy(args, 0, newArgs, 1, args.Length);
                return newArgs;
            }

            return args;
        }

        private void ParseArguments(string[] args)
        {
            // Parse once to record Errors
            ParseArguments(args, false);

            if (ParseErrors != null)
            {
                // Parse a second time to populate objects (even if there are errors)
                ParseArguments(args, true);
            }
        }

        private void ParseArguments(string[] args, bool ignoreErrors)
        {
            // We have custom Help / Version functions
            using (var parser = new Parser(config =>
            {
                config.AutoHelp = false;
                config.AutoVersion = false;
                config.CaseSensitive = false;

                // We should consider making this false, but it will break people adding unknown arguments
                config.IgnoreUnknownArguments = ignoreErrors;
            }))
            {
                // Parse Arugments
                // the parsing library does not allow a mix of verbs and no-verbs per parse (https://github.com/commandlineparser/commandline/issues/174)
                args = AddDefaultVerbIfNecessary(args);

                parser
                    .ParseArguments(args, verbTypes)
                    .WithParsed<ConfigureAgent>(
                        x =>
                        {
                            Configure = x;
                        })
                    .WithParsed<RunAgent>(
                        x =>
                        {
                            Run = x;
                        })
                    .WithParsed<RemoveAgent>(
                        x =>
                        {
                            Remove = x;
                        })
                    .WithParsed<WarmupAgent>(
                        x =>
                        {
                            Warmup = x;
                        })
                    .WithNotParsed(
                        errors =>
                        {
                            ParseErrors = errors;
                        });
            }
        }

        private void PrintArguments()
        {
            if (Configure != null)
            {
                _trace.Info(string.Concat(nameof(Configure), " ", ObjectAsJson(Configure)));
            }

            if (Remove != null)
            {
                _trace.Info(string.Concat(nameof(Remove), " ", ObjectAsJson(Remove)));
            }

            if (Warmup != null)
            {
                _trace.Info(string.Concat(nameof(Warmup), " ", ObjectAsJson(Warmup)));
            }

            if (Run != null)
            {
                _trace.Info(string.Concat(nameof(Run), " ", ObjectAsJson(Run)));
            }
        }

        private string ObjectAsJson(object obj)
        {
            return JsonConvert.SerializeObject(
                    obj, Formatting.Indented,
                    new JsonConverter[] { new StringEnumConverter() });
        }

        private ConfigureOrRemoveBase GetConfigureOrRemoveBase()
        {
            if (Configure != null)
            {
                return Configure as ConfigureOrRemoveBase;
            }

            if (Remove != null)
            {
                return Remove as ConfigureOrRemoveBase;
            }

            return null;
        }
    }
}
