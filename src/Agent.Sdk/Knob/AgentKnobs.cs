// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public class AgentKnobs
    {
        // Containers
        public static readonly Knob PreferPowershellHandlerOnContainers = new Knob(
            nameof(PreferPowershellHandlerOnContainers),
            "If true, prefer using the PowerShell handler on Windows containers for tasks that provide both a Node and PowerShell handler version.",
            new RuntimeKnobSource("agent.preferPowerShellOnContainers"),
            new EnvironmentKnobSource("AGENT_PREFER_POWERSHELL_ON_CONTAINERS"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob SetupDockerGroup = new Knob(
            nameof(SetupDockerGroup),
            "If true, allows the user to run docker commands without sudo",
            new RuntimeKnobSource("VSTS_SETUP_DOCKERGROUP"),
            new EnvironmentKnobSource("VSTS_SETUP_DOCKERGROUP"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob AllowMountTasksReadonlyOnWindows = new Knob(
            nameof(AllowMountTasksReadonlyOnWindows),
            "If true, allows the user to mount 'tasks' volume read-only on Windows OS",
            new RuntimeKnobSource("VSTS_SETUP_ALLOW_MOUNT_TASKS_READONLY"),
            new EnvironmentKnobSource("VSTS_SETUP_ALLOW_MOUNT_TASKS_READONLY"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob SkipPostExeceutionIfTargetContainerStopped = new Knob(
            nameof(SkipPostExeceutionIfTargetContainerStopped),
            "If true, skips post-execution step for tasks in case the target container has been stopped",
            new RuntimeKnobSource("AGENT_SKIP_POST_EXECUTION_IF_CONTAINER_STOPPED"),
            new EnvironmentKnobSource("AGENT_SKIP_POST_EXECUTION_IF_CONTAINER_STOPPED"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob MTUValueForContainerJobs = new Knob(
            nameof(MTUValueForContainerJobs),
            "Allow to specify MTU value for networks used by container jobs (useful for docker-in-docker scenarios in k8s cluster).",
            new EnvironmentKnobSource("AGENT_DOCKER_MTU_VALUE"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob DockerNetworkCreateDriver = new Knob(
            nameof(DockerNetworkCreateDriver),
            "Allow to specify which driver will be used when creating docker network",
            new RuntimeKnobSource("agent.DockerNetworkCreateDriver"),
            new EnvironmentKnobSource("AZP_AGENT_DOCKER_NETWORK_CREATE_DRIVER"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob DockerAdditionalNetworkOptions = new Knob(
            nameof(DockerAdditionalNetworkOptions),
            "Allow to specify additional command line options to 'docker network' command when creating network for new containers",
            new RuntimeKnobSource("agent.DockerAdditionalNetworkOptions"),
            new EnvironmentKnobSource("AZP_AGENT_DOCKER_ADDITIONAL_NETWORK_OPTIONS"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob UseHostGroupId = new Knob(
            nameof(UseHostGroupId),
            "If true, use the same group ID (GID) as the user on the host on which the agent is running",
            new RuntimeKnobSource("agent.UseHostGroupId"),
            new EnvironmentKnobSource("AZP_AGENT_USE_HOST_GROUP_ID"),
            new BuiltInDefaultKnobSource("true"));

        public const string DockerActionRetriesVariableName = "VSTSAGENT_DOCKER_ACTION_RETRIES";

        public static readonly Knob DockerActionRetries = new Knob(
            nameof(DockerActionRetries),
            "When enabled, the agent retries docker steps if failed",
            new RuntimeKnobSource(DockerActionRetriesVariableName),
            new EnvironmentKnobSource(DockerActionRetriesVariableName),
            new BuiltInDefaultKnobSource("false"));

        // Directory structure
        public static readonly Knob AgentToolsDirectory = new Knob(
            nameof(AgentToolsDirectory),
            "The location to look for/create the agents tool cache",
            new EnvironmentKnobSource("AGENT_TOOLSDIRECTORY"),
            new EnvironmentKnobSource("agent.ToolsDirectory"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob OverwriteTemp = new Knob(
            nameof(OverwriteTemp),
            "If true, the system temp variable will be overriden to point to the agent's temp directory.",
            new RuntimeKnobSource("VSTS_OVERWRITE_TEMP"),
            new EnvironmentKnobSource("VSTS_OVERWRITE_TEMP"),
            new BuiltInDefaultKnobSource("false"));

        // Tool configuration
        public static readonly Knob DisableFetchByCommit = new Knob(
            nameof(DisableFetchByCommit),
            "If true and server supports it, fetch the target branch by commit. Otherwise, fetch all branches and pull request ref to get the target branch.",
            new RuntimeKnobSource("VSTS.DisableFetchByCommit"),
            new EnvironmentKnobSource("VSTS_DISABLEFETCHBYCOMMIT"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableFetchPruneTags = new Knob(
            nameof(DisableFetchPruneTags),
            "If true, disable --prune-tags in the fetches.",
            new RuntimeKnobSource("VSTS.DisableFetchPruneTags"),
            new EnvironmentKnobSource("VSTS_DISABLEFETCHPRUNETAGS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob PreferGitFromPath = new Knob(
            nameof(PreferGitFromPath),
            "Determines which Git we will use on Windows. By default, we prefer the built-in portable git in the agent's externals folder, setting this to true makes the agent find git.exe from %PATH% if possible.",
            new RuntimeKnobSource("system.prefergitfrompath"),
            new EnvironmentKnobSource("system.prefergitfrompath"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableGitPrompt = new Knob(
            nameof(DisableGitPrompt),
            "If true, git will not prompt on the terminal (e.g., when asking for HTTP authentication).",
            new RuntimeKnobSource("VSTS_DISABLE_GIT_PROMPT"),
            new EnvironmentKnobSource("VSTS_DISABLE_GIT_PROMPT"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob GitUseSecureParameterPassing = new Knob(
            nameof(GitUseSecureParameterPassing),
            "If true, don't pass auth token in git parameters",
            new RuntimeKnobSource("agent.GitUseSecureParameterPassing"),
            new EnvironmentKnobSource("AGENT_GIT_USE_SECURE_PARAMETER_PASSING"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob FixPossibleGitOutOfMemoryProblem = new Knob(
            nameof(FixPossibleGitOutOfMemoryProblem),
            "When true, set config git properties to fix possible out of memory problem",
            new RuntimeKnobSource("FIX_POSSIBLE_GIT_OUT_OF_MEMORY_PROBLEM"),
            new EnvironmentKnobSource("FIX_POSSIBLE_GIT_OUT_OF_MEMORY_PROBLEM"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseGitLongPaths = new Knob(
            nameof(UseGitLongPaths),
            "When true, set core.longpaths to true",
            new RuntimeKnobSource("USE_GIT_LONG_PATHS"),
            new EnvironmentKnobSource("USE_GIT_LONG_PATHS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseGitSingleThread = new Knob(
            nameof(UseGitSingleThread),
            "When true, spawn only one thread searching for best delta matches",
            new RuntimeKnobSource("USE_GIT_SINGLE_THREAD"),
            new EnvironmentKnobSource("USE_GIT_SINGLE_THREAD"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AgentTerminalEncoding = new Knob(
            nameof(AgentTerminalEncoding),
            "If the correct encoding name is specified, the encoding from the environment will be used instead of default UTF-8",
            new EnvironmentKnobSource("AGENT_TERMINAL_ENCODING"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob TfVCUseSecureParameterPassing = new Knob(
            nameof(TfVCUseSecureParameterPassing),
            "If true, don't pass auth token in TFVC parameters",
            new RuntimeKnobSource("agent.TfVCUseSecureParameterPassing"),
            new EnvironmentKnobSource("AGENT_TFVC_USE_SECURE_PARAMETER_PASSING"),
            new BuiltInDefaultKnobSource("true"));

        public const string QuietCheckoutRuntimeVarName = "agent.source.checkout.quiet";
        public const string QuietCheckoutEnvVarName = "AGENT_SOURCE_CHECKOUT_QUIET";

        public static readonly Knob QuietCheckout = new Knob(
            nameof(QuietCheckout),
            "Aggressively reduce what gets logged to the console when checking out source.",
            new RuntimeKnobSource(QuietCheckoutRuntimeVarName),
            new EnvironmentKnobSource(QuietCheckoutEnvVarName),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseNode10 = new Knob(
            nameof(UseNode10),
            "Forces the agent to use Node 10 handler for all Node-based tasks",
            new RuntimeKnobSource("AGENT_USE_NODE10"),
            new EnvironmentKnobSource("AGENT_USE_NODE10"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseNode20_1 = new Knob(
            nameof(UseNode20_1),
            "Forces the agent to use Node 20 handler for all Node-based tasks",
            new RuntimeKnobSource("AGENT_USE_NODE20_1"),
            new EnvironmentKnobSource("AGENT_USE_NODE20_1"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseNode20InUnsupportedSystem = new Knob(
            nameof(UseNode20InUnsupportedSystem),
            "Forces the agent to use Node 20 handler for all Node-based tasks, even if it's in an unsupported system",
            new RuntimeKnobSource("AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"),
            new EnvironmentKnobSource("AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM"),
            new BuiltInDefaultKnobSource("false"));

        // Agent logging
        public static readonly Knob AgentPerflog = new Knob(
            nameof(AgentPerflog),
            "If set, writes a perf counter trace for the agent. Writes to the location set in this variable.",
            new EnvironmentKnobSource("VSTS_AGENT_PERFLOG"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob TraceVerbose = new Knob(
            nameof(TraceVerbose),
            "If set to anything, trace level will be verbose",
            new EnvironmentKnobSource("VSTSAGENT_TRACE"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob DumpJobEventLogs = new Knob(
            nameof(DumpJobEventLogs),
            "If true, dump event viewer logs",
            new RuntimeKnobSource("VSTSAGENT_DUMP_JOB_EVENT_LOGS"),
            new EnvironmentKnobSource("VSTSAGENT_DUMP_JOB_EVENT_LOGS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableTestsMetadata = new Knob(
            nameof(DisableTestsMetadata),
            "If true, publishing tests metadata to evidence store will be disabled.",
            new RuntimeKnobSource("AZP_AGENT_DISABLE_TESTS_METADATA"),
            new EnvironmentKnobSource("AZP_AGENT_DISABLE_TESTS_METADATA"),
            new BuiltInDefaultKnobSource("false"));

        // Diag logging
        public static readonly Knob AgentDiagLogPath = new Knob(
            nameof(AgentDiagLogPath),
            "If set to anything, the folder containing the agent diag log will be created here.",
            new EnvironmentKnobSource("AGENT_DIAGLOGPATH"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob WorkerDiagLogPath = new Knob(
            nameof(WorkerDiagLogPath),
            "If set to anything, the folder containing the agent worker diag log will be created here.",
            new EnvironmentKnobSource("WORKER_DIAGLOGPATH"),
            new BuiltInDefaultKnobSource(string.Empty));

        // Timeouts
        public static readonly Knob AgentChannelTimeout = new Knob(
            nameof(AgentChannelTimeout),
            "Timeout for channel communication between agent listener and worker processes.",
            new EnvironmentKnobSource("VSTS_AGENT_CHANNEL_TIMEOUT"),
            new BuiltInDefaultKnobSource("30"));

        public static readonly Knob AgentDownloadTimeout = new Knob(
            nameof(AgentDownloadTimeout),
            "Amount of time in seconds to wait for the agent to download a new version when updating",
            new EnvironmentKnobSource("AZP_AGENT_DOWNLOAD_TIMEOUT"),
            new BuiltInDefaultKnobSource("1500")); // 25*60

        public static readonly Knob TaskDownloadTimeout = new Knob(
            nameof(TaskDownloadTimeout),
            "Amount of time in seconds to wait for the agent to download a task when starting a job",
            new EnvironmentKnobSource("VSTS_TASK_DOWNLOAD_TIMEOUT"),
            new BuiltInDefaultKnobSource("1200")); // 20*60

        public static readonly Knob TaskDownloadRetryLimit = new Knob(
            nameof(TaskDownloadRetryLimit),
            "Attempts to download a task when starting a job",
            new EnvironmentKnobSource("VSTS_TASK_DOWNLOAD_RETRY_LIMIT"),
            new BuiltInDefaultKnobSource("3"));

        public static readonly Knob ProccessSigintTimeout = new Knob(
            nameof(ProccessSigintTimeout),
            "Timeout for SIGINT signal during a process cancelation",
            new RuntimeKnobSource("PROCESS_SIGINT_TIMEOUT"),
            new EnvironmentKnobSource("PROCESS_SIGINT_TIMEOUT"),
            new BuiltInDefaultKnobSource("7500"));

        public static readonly Knob ProccessSigtermTimeout = new Knob(
            nameof(ProccessSigtermTimeout),
            "Timeout for SIGTERM signal during a process cancelation",
            new RuntimeKnobSource("PROCESS_SIGTERM_TIMEOUT"),
            new EnvironmentKnobSource("PROCESS_SIGTERM_TIMEOUT"),
            new BuiltInDefaultKnobSource("2500"));

        public static readonly Knob UseGracefulProcessShutdown = new Knob(
            nameof(UseGracefulProcessShutdown),
            "Attemts to use only graceful process shutdown unless hard required",
            new RuntimeKnobSource("USE_GRACEFUL_PROCESS_SHUTDOWN"),
            new EnvironmentKnobSource("USE_GRACEFUL_PROCESS_SHUTDOWN"),
            new BuiltInDefaultKnobSource("false"));

        // HTTP
        public const string LegacyHttpVariableName = "AZP_AGENT_USE_LEGACY_HTTP";
        public static readonly Knob UseLegacyHttpHandler = new DeprecatedKnob(
            nameof(UseLegacyHttpHandler),
            "Use the libcurl-based HTTP handler rather than .NET's native HTTP handler, as we did on .NET Core 2.1",
            "Legacy http handler will be removed in one of the next agent releases with migration to .Net Core 6. We are highly recommend to not use it.",
            new EnvironmentKnobSource(LegacyHttpVariableName),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob HttpRetryCount = new Knob(
            nameof(HttpRetryCount),
            "Number of times to retry Http requests",
            new EnvironmentKnobSource("VSTS_HTTP_RETRY"),
            new BuiltInDefaultKnobSource("3"));

        public static readonly Knob HttpTimeout = new Knob(
            nameof(HttpTimeout),
            "Timeout for Http requests",
            new EnvironmentKnobSource("VSTS_HTTP_TIMEOUT"),
            new BuiltInDefaultKnobSource("100"));

        public static readonly Knob HttpTrace = new Knob(
            nameof(HttpTrace),
            "Enable http trace if true",
            new EnvironmentKnobSource("VSTS_AGENT_HTTPTRACE"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob NoProxy = new Knob(
            nameof(NoProxy),
            "Proxy bypass list if one exists. Should be comma seperated",
            new EnvironmentKnobSource("no_proxy"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob ProxyAddress = new Knob(
            nameof(ProxyAddress),
            "Proxy server address if one exists",
            new EnvironmentKnobSource("VSTS_HTTP_PROXY"),
            new EnvironmentKnobSource("http_proxy"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob ProxyPassword = new SecretKnob(
            nameof(ProxyPassword),
            "Proxy password if one exists",
            new EnvironmentKnobSource("VSTS_HTTP_PROXY_PASSWORD"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob ProxyUsername = new SecretKnob(
            nameof(ProxyUsername),
            "Proxy username if one exists",
            new EnvironmentKnobSource("VSTS_HTTP_PROXY_USERNAME"),
            new BuiltInDefaultKnobSource(string.Empty));

        // Secrets masking
        public static readonly Knob AllowUnsafeMultilineSecret = new Knob(
            nameof(AllowUnsafeMultilineSecret),
            "WARNING: enabling this may allow secrets to leak. Allows multi-line secrets to be set. Unsafe because it is possible for log lines to get dropped in agent failure cases, causing the secret to not get correctly masked. We recommend leaving this option off.",
            new RuntimeKnobSource("SYSTEM_UNSAFEALLOWMULTILINESECRET"),
            new EnvironmentKnobSource("SYSTEM_UNSAFEALLOWMULTILINESECRET"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob MaskedSecretMinLength = new Knob(
            nameof(MaskedSecretMinLength),
            "Specify the length of the secrets, which, if shorter, will be ignored in the logs.",
            new RuntimeKnobSource("AZP_IGNORE_SECRETS_SHORTER_THAN"),
            new EnvironmentKnobSource("AZP_IGNORE_SECRETS_SHORTER_THAN"),
            new BuiltInDefaultKnobSource("0"));

        // Misc
        public static readonly Knob EnableIssueSourceValidation = new Knob(
            nameof(EnableIssueSourceValidation),
            "When true, enable issue source validation for the task.issue command.",
            new RuntimeKnobSource("ENABLE_ISSUE_SOURCE_VALIDATION"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableAgentDowngrade = new Knob(
            nameof(DisableAgentDowngrade),
            "Disable agent downgrades. Upgrades will still be allowed.",
            new EnvironmentKnobSource("AZP_AGENT_DOWNGRADE_DISABLED"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AcknowledgeNoUpdates = new Knob(
            nameof(AcknowledgeNoUpdates),
            "Opt-in to continue using agent without updates on unsopperted OS",
            new EnvironmentKnobSource("AGENT_ACKNOWLEDGE_NO_UPDATES"),
            new RuntimeKnobSource("AGENT_ACKNOWLEDGE_NO_UPDATES"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AgentFailOnIncompatibleOS = new Knob(
            nameof(AgentFailOnIncompatibleOS),
            "Allow agent to fail pipelines on incampatible OS",
            new EnvironmentKnobSource("AGENT_FAIL_ON_INCOMPATIBLE_OS"),
            new RuntimeKnobSource("AGENT_FAIL_ON_INCOMPATIBLE_OS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AgentEnablePipelineArtifactLargeChunkSize = new Knob(
            nameof(AgentEnablePipelineArtifactLargeChunkSize),
            "Enables large chunk size for pipeline artifacts.",
            new EnvironmentKnobSource("AGENT_ENABLE_PIPELINEARTIFACT_LARGE_CHUNK_SIZE"),
            new RuntimeKnobSource("AGENT_ENABLE_PIPELINEARTIFACT_LARGE_CHUNK_SIZE"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob PermissionsCheckFailsafe = new Knob(
            nameof(PermissionsCheckFailsafe),
            "Maximum depth of file permitted in directory hierarchy when checking permissions. Check to avoid accidentally entering infinite loops.",
            new EnvironmentKnobSource("AGENT_TEST_VALIDATE_EXECUTE_PERMISSIONS_FAILSAFE"),
            new BuiltInDefaultKnobSource("100"));

        public static readonly Knob DisableInputTrimming = new Knob(
            nameof(DisableInputTrimming),
            "By default, the agent trims whitespace and new line characters from all task inputs. Setting this to true disables this behavior.",
            new EnvironmentKnobSource("DISABLE_INPUT_TRIMMING"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DecodePercents = new Knob(
            nameof(DecodePercents),
            "By default, the agent does not decodes %AZP25 as % which may be needed to allow users to work around reserved values. Setting this to true enables this behavior.",
            new RuntimeKnobSource("DECODE_PERCENTS"),
            new EnvironmentKnobSource("DECODE_PERCENTS"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob AllowTfvcUnshelveErrors = new Knob(
            nameof(AllowTfvcUnshelveErrors),
            "By default, the TFVC unshelve command does not throw errors e.g. when there's no mapping for one or more files shelved. Setting this to true enables this behavior.",
            new RuntimeKnobSource("ALLOW_TFVC_UNSHELVE_ERRORS"),
            new EnvironmentKnobSource("ALLOW_TFVC_UNSHELVE_ERRORS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob EnableFCSItemPathFix = new Knob(
            nameof(EnableFCSItemPathFix),
            "If true, enable the fix for the path of the item when associating or uploading to the file container server.",
            new RuntimeKnobSource("ENABLE_FCS_ITEM_PATH_FIX"),
            new EnvironmentKnobSource("ENABLE_FCS_ITEM_PATH_FIX"),
            new BuiltInDefaultKnobSource("false"));

        // Set DISABLE_JAVA_CAPABILITY_HIGHER_THAN_9 variable with any value
        // to disable recognition of Java higher than 9
        public static readonly Knob DisableRecognitionOfJDKHigherThen9 = new Knob(
            nameof(DisableRecognitionOfJDKHigherThen9),
            "Recognize JDK and JRE >= 9 installed on the machine as agent capability. Setting any value to DISABLE_JAVA_CAPABILITY_HIGHER_THAN_9 is disabling this behavior",
            new EnvironmentKnobSource("DISABLE_JAVA_CAPABILITY_HIGHER_THAN_9"),
            new BuiltInDefaultKnobSource(string.Empty));

        // TODO: Added 5/27/21. Please remove within a month or two
        public static readonly Knob DisableBuildArtifactsToBlob = new Knob(
            nameof(DisableBuildArtifactsToBlob),
            "By default, the agent will upload build artifacts to Blobstore. Setting this to true will disable that integration. This variable is temporary and will be removed.",
            new RuntimeKnobSource("DISABLE_BUILD_ARTIFACTS_TO_BLOB"),
            new EnvironmentKnobSource("DISABLE_BUILD_ARTIFACTS_TO_BLOB"),
            new BuiltInDefaultKnobSource("false"));
        public static readonly Knob SendBuildArtifactsToBlobstoreDomain = new Knob(
            nameof(SendBuildArtifactsToBlobstoreDomain),
            "When set, defines the domain to use to send Build artifacts to.",
            new RuntimeKnobSource("SEND_BUILD_ARTIFACTS_TO_BLOBSTORE_DOMAIN"),
            new EnvironmentKnobSource("SEND_BUILD_ARTIFACT_ARTIFACTS_TO_BLOBSTORE_DOMAIN"),
            new BuiltInDefaultKnobSource(string.Empty));
        public static readonly Knob SendPipelineArtifactsToBlobstoreDomain = new Knob(
            nameof(SendPipelineArtifactsToBlobstoreDomain),
            "When set, defines the domain to use to send Pipeline artifacts to.",
            new RuntimeKnobSource("SEND_PIPELINE_ARTIFACTS_TO_BLOBSTORE_DOMAIN"),
            new EnvironmentKnobSource("SEND_PIPELINE_ARTIFACT_ARTIFACTS_TO_BLOBSTORE_DOMAIN"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob EnableIncompatibleBuildArtifactsPathResolution = new Knob(
            nameof(EnableIncompatibleBuildArtifactsPathResolution),
            "Return DownloadBuildArtifactsV1 target path resolution behavior back to how it was originally implemented. This breaks back compatibility with DownloadBuildArtifactsV0.",
            new RuntimeKnobSource("EnableIncompatibleBuildArtifactsPathResolution"),
            new EnvironmentKnobSource("EnableIncompatibleBuildArtifactsPathResolution"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableAuthenticodeValidation = new Knob(
               nameof(DisableAuthenticodeValidation),
               "Disables authenticode validation for agent package during self update. Set this to any non-empty value to disable.",
               new EnvironmentKnobSource("DISABLE_AUTHENTICODE_VALIDATION"),
               new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob DisableHashValidation = new Knob(
            nameof(DisableHashValidation),
            "If true, the agent will skip package hash validation during self-updating.",
            new EnvironmentKnobSource("DISABLE_HASH_VALIDATION"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob EnableVSPreReleaseVersions = new Knob(
            nameof(EnableVSPreReleaseVersions),
            "If true, the agent will include to seach VisualStudio prerelease versions to capabilities.",
            new EnvironmentKnobSource("ENABLE_VS_PRERELEASE_VERSIONS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableOverrideTfvcBuildDirectory = new Knob(
            nameof(DisableOverrideTfvcBuildDirectory),
            "Disables override of Tfvc build directory name by agentId on hosted agents (one tfvc repo used).",
            new RuntimeKnobSource("DISABLE_OVERRIDE_TFVC_BUILD_DIRECTORY"),
            new EnvironmentKnobSource("DISABLE_OVERRIDE_TFVC_BUILD_DIRECTORY"),
            new BuiltInDefaultKnobSource("false"));

        /// <remarks>We need to remove this knob - once Node 6 handler is dropped</remarks>
        public static readonly Knob DisableNode6DeprecationWarning = new Knob(
            nameof(DisableNode6DeprecationWarning),
            "Disables Node 6 deprecation warnings.",
            new RuntimeKnobSource("DISABLE_NODE6_DEPRECATION_WARNING"),
            new EnvironmentKnobSource("DISABLE_NODE6_DEPRECATION_WARNING"),
            new BuiltInDefaultKnobSource("true"));

        public static readonly Knob DisableNode6Tasks = new Knob(
            nameof(DisableNode6Tasks),
            "Disables Node 6 tasks and Node 6 runner.",
            new RuntimeKnobSource("AGENT_DISABLE_NODE6_TASKS"),
            new EnvironmentKnobSource("AGENT_DISABLE_NODE6_TASKS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableTeePluginRemoval = new Knob(
            nameof(DisableTeePluginRemoval),
            "Disables removing TEE plugin after using it during checkout.",
            new RuntimeKnobSource("DISABLE_TEE_PLUGIN_REMOVAL"),
            new EnvironmentKnobSource("DISABLE_TEE_PLUGIN_REMOVAL"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob TeePluginDownloadRetryCount = new Knob(
            nameof(TeePluginDownloadRetryCount),
            "Number of times to retry downloading TEE plugin",
            new RuntimeKnobSource("TEE_PLUGIN_DOWNLOAD_RETRY_COUNT"),
            new EnvironmentKnobSource("TEE_PLUGIN_DOWNLOAD_RETRY_COUNT"),
            new BuiltInDefaultKnobSource("3"));

        public static readonly Knob DumpPackagesVerificationResult = new Knob(
            nameof(DumpPackagesVerificationResult),
            "If true, dumps info about invalid MD5 sums of installed packages",
            new RuntimeKnobSource("VSTSAGENT_DUMP_PACKAGES_VERIFICATION_RESULTS"),
            new EnvironmentKnobSource("VSTSAGENT_DUMP_PACKAGES_VERIFICATION_RESULTS"),
            new BuiltInDefaultKnobSource("false"));

        public const string ContinueAfterCancelProcessTreeKillAttemptVariableName = "VSTSAGENT_CONTINUE_AFTER_CANCEL_PROCESSTREEKILL_ATTEMPT";

        public static readonly Knob ContinueAfterCancelProcessTreeKillAttempt = new Knob(
            nameof(ContinueAfterCancelProcessTreeKillAttempt),
            "If true, continue cancellation after attempt to KillProcessTree",
            new RuntimeKnobSource(ContinueAfterCancelProcessTreeKillAttemptVariableName),
            new EnvironmentKnobSource(ContinueAfterCancelProcessTreeKillAttemptVariableName),
            new BuiltInDefaultKnobSource("false"));

        public const string VstsAgentNodeWarningsVariableName = "VSTSAGENT_ENABLE_NODE_WARNINGS";

        public static readonly Knob AgentDeprecatedNodeWarnings = new Knob(
            nameof(AgentDeprecatedNodeWarnings),
            "If true shows warning on depricated node (6) tasks",
            new RuntimeKnobSource(VstsAgentNodeWarningsVariableName),
            new EnvironmentKnobSource(VstsAgentNodeWarningsVariableName),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseNode = new Knob(
            nameof(UseNode),
            "Forces the agent to use different version of Node if when configured runner is not available. Possible values: LTS - make agent use latest LTS version of Node; UPGRADE - make agent use next available version of Node",
            new RuntimeKnobSource("AGENT_USE_NODE"),
            new EnvironmentKnobSource("AGENT_USE_NODE"),
            new BuiltInDefaultKnobSource(string.Empty));

        public static readonly Knob ProcessHandlerSecureArguments = new Knob(
            nameof(ProcessHandlerSecureArguments),
            "Enables passing arguments for process handler secure way",
            new RuntimeKnobSource("AZP_75787_ENABLE_NEW_LOGIC"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob ProcessHandlerSecureArgumentsAudit = new Knob(
            nameof(ProcessHandlerSecureArguments),
            "Enables logging of passing arguments for process handler secure way",
            new RuntimeKnobSource("AZP_75787_ENABLE_NEW_LOGIC_LOG"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob ProcessHandlerTelemetry = new Knob(
            nameof(ProcessHandlerTelemetry),
            "Enables publishing telemetry about processing of arguments for Process Handler",
            new RuntimeKnobSource("AZP_75787_ENABLE_COLLECT"),
            new EnvironmentKnobSource("AZP_75787_ENABLE_COLLECT"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseNewNodeHandlerTelemetry = new Knob(
            nameof(UseNewNodeHandlerTelemetry),
            "Enables new approach to publish node handler information to the telemetry",
            new RuntimeKnobSource("AZP_USENEWNODEHANDLERTELEMETRY"),
            new EnvironmentKnobSource("AZP_USENEWNODEHANDLERTELEMETRY"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob ProcessHandlerEnableNewLogic = new Knob(
            nameof(ProcessHandlerEnableNewLogic),
            "Enables new args protect logic for process handler",
            new RuntimeKnobSource("AZP_75787_ENABLE_NEW_PH_LOGIC"),
            new EnvironmentKnobSource("AZP_75787_ENABLE_NEW_PH_LOGIC"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseProcessHandlerV2 = new Knob(
            nameof(UseProcessHandlerV2),
            "Enables new Process handler (v2)",
            new RuntimeKnobSource("AGENT_USE_PROCESS_HANDLER_V2"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableDrainQueuesAfterTask = new Knob(
            nameof(DisableDrainQueuesAfterTask),
            "Forces the agent to disable draining queues after each task",
            new RuntimeKnobSource("AGENT_DISABLE_DRAIN_QUEUES_AFTER_TASK"),
            new EnvironmentKnobSource("AGENT_DISABLE_DRAIN_QUEUES_AFTER_TASK"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob EnableFetchingNet6List = new Knob(
            nameof(EnableFetchingNet6List),
            "Forces the agent to fetch list of .NET 6 supporting systems from server",
            new EnvironmentKnobSource("AGENT_ENABLE_FETCHING_NET6_LIST"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob EnableResourceUtilizationWarnings = new Knob(
            nameof(EnableResourceUtilizationWarnings),
            "If true, the agent will throw the resource utilization warnings",
            new RuntimeKnobSource("AZP_ENABLE_RESOURCE_UTILIZATION_WARNINGS"),
            new EnvironmentKnobSource("AZP_ENABLE_RESOURCE_UTILIZATION_WARNINGS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob ForceCreateTasksDirectory = new Knob(
            nameof(ForceCreateTasksDirectory),
            "Forces the agent to create _tasks folder for tasks.",
            new RuntimeKnobSource("AGENT_FORCE_CREATE_TASKS_DIRECTORY"),
            new EnvironmentKnobSource("AGENT_FORCE_CREATE_TASKS_DIRECTORY"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob CleanupPSModules = new Knob(
            nameof(CleanupPSModules),
            "Removes the PSModulePath environment variable if the agent is running in PowerShell.",
            new RuntimeKnobSource("AZP_AGENT_CLEANUP_PSMODULES_IN_POWERSHELL"),
            new EnvironmentKnobSource("AZP_AGENT_CLEANUP_PSMODULES_IN_POWERSHELL"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableCleanRepoDefaultValue = new DeprecatedKnob(
            nameof(DisableCleanRepoDefaultValue),
            "Avoid to set default value if build.repository.clean variable is not set on Trigger Yaml UI or in checkout steps yaml config",
            new EnvironmentKnobSource("AGENT_DISABLE_CLEAN_REPO_DEFAULT_VALUE"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob IgnoreVSTSTaskLib = new Knob(
            nameof(IgnoreVSTSTaskLib),
            "Ignores the VSTSTaskLib folder when copying tasks.",
            new RuntimeKnobSource("AZP_AGENT_IGNORE_VSTSTASKLIB"),
            new EnvironmentKnobSource("AZP_AGENT_IGNORE_VSTSTASKLIB"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob FailJobWhenAgentDies = new Knob(
            nameof(FailJobWhenAgentDies),
            "Mark the Job as Failed instead of Canceled when the Agent dies due to User Cancellation or Shutdown",
            new RuntimeKnobSource("FAIL_JOB_WHEN_AGENT_DIES"),
            new EnvironmentKnobSource("FAIL_JOB_WHEN_AGENT_DIES"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AllowWorkDirectoryRepositories = new Knob(
            nameof(AllowWorkDirectoryRepositories),
            "Allows repositories to be checked out below work directory level on self hosted agents.",
            new RuntimeKnobSource("AZP_AGENT_ALLOW_WORK_DIRECTORY_REPOSITORIES"),
            new EnvironmentKnobSource("AZP_AGENT_ALLOW_WORK_DIRECTORY_REPOSITORIES"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob CheckForTaskDeprecation = new Knob(
            nameof(CheckForTaskDeprecation),
            "If true, the agent will check in the 'Initialize job' step each task used in the job for task deprecation.",
            new RuntimeKnobSource("AZP_AGENT_CHECK_FOR_TASK_DEPRECATION"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob CheckIfTaskNodeRunnerIsDeprecated = new Knob(
            nameof(CheckIfTaskNodeRunnerIsDeprecated),
            "If true, the agent will check in the 'Initialize job' step each task used in the job if this task has node handlers, and all of them are deprecated.",
            new RuntimeKnobSource("AZP_AGENT_CHECK_IF_TASK_NODE_RUNNER_IS_DEPRECATED"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob MountWorkspace = new Knob(
            nameof(MountWorkspace),
            "If true, the agent will mount the Pipeline.Workspace directory instead of the Working directory for steps which target a Docker container.",
            new RuntimeKnobSource("AZP_AGENT_MOUNT_WORKSPACE"),
            new EnvironmentKnobSource("AZP_AGENT_MOUNT_WORKSPACE"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob EnableNewSecretMasker = new Knob(
            nameof(EnableNewSecretMasker),
            "If true, the agent will use new SecretMasker with additional filters & performance enhancements",
            new EnvironmentKnobSource("AZP_ENABLE_NEW_SECRET_MASKER"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AddDockerInitOption = new Knob(
            nameof(AddDockerInitOption),
            "If true, the agent will create docker container with the --init option.",
            new RuntimeKnobSource("AZP_AGENT_DOCKER_INIT_OPTION"),
            new EnvironmentKnobSource("AZP_AGENT_DOCKER_INIT_OPTION"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob LogTaskNameInUserAgent = new Knob(
            nameof(LogTaskNameInUserAgent),
            "If true, agent will log the task name in user agent.",
            new RuntimeKnobSource("AZP_AGENT_LOG_TASKNAME_IN_USERAGENT"),
            new EnvironmentKnobSource("AZP_AGENT_LOG_TASKNAME_IN_USERAGENT"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob UseFetchFilterInCheckoutTask = new Knob(
            nameof(UseFetchFilterInCheckoutTask),
            "If true, agent will use fetch filter in checkout task.",
            new RuntimeKnobSource("AGENT_USE_FETCH_FILTER_IN_CHECKOUT_TASK"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob StoreAgentKeyInCSPContainer = new Knob(
            nameof(StoreAgentKeyInCSPContainer),
            "Store agent key in named container (Windows).",
            new EnvironmentKnobSource("STORE_AGENT_KEY_IN_CSP_CONTAINER"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob AgentKeyUseCng = new Knob(
            nameof(AgentKeyUseCng),
            "Use CNG API to store agent key.  Note: Uses Private User Storage",
            new EnvironmentKnobSource("AGENT_KEY_USE_CNG"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob RsaKeyGetConfigFromFF = new Knob(
            nameof(RsaKeyGetConfigFromFF),
            "Get config from FF.",
            new EnvironmentKnobSource("RSAKEYGETCONFIGFROMFF"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableResourceUtilizationWarnings = new Knob(
            nameof(DisableResourceUtilizationWarnings),
            "If true, agent will not throw warnings related to high resource utilization",
            new RuntimeKnobSource("DISABLE_RESOURCE_UTILIZATION_WARNINGS"),
            new EnvironmentKnobSource("DISABLE_RESOURCE_UTILIZATION_WARNINGS"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob Rosetta2Warning = new Knob(
            nameof(Rosetta2Warning),
            "Log warning when X64 Agent is used on a Apple Silicon device.",
            new RuntimeKnobSource("ROSETTA2_WARNING"),
            new EnvironmentKnobSource("ROSETTA2_WARNING"),
            new PipelineFeatureSource("Rosetta2Warning"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob CheckPsModulesLocations = new Knob(
            nameof(CheckPsModulesLocations),
            "Checks if the PSModulePath environment variable contains locations specific to PowerShell Core.",
            new EnvironmentKnobSource("AZP_AGENT_CHECK_PSMODULES_LOCATIONS"),
            new BuiltInDefaultKnobSource("false"));
    }
}
