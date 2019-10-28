// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Plugins.Log.TestFilePublisher
{
    public class TelemetryConstants
    {
        public const string PluginInitialized = "PluginInitialized";

        public const string PluginDisabled = "PluginDisabled";

        public const string Exceptions = "Exceptions";

        public const string FinalizeAsync = "FinalizeAsync";

        public const string FindTestFilesAsync = "FindTestFilesAsync";

        public const string InitializeFailed = "InitializeFailed";

        public const string PublishTestRunDataAsync = "PublishTestRunDataAsync";

        public const string ParseTestResultFiles = "ParseTestResultFiles";

        public const string StageName = "StageName";

        public const string StageAttempt = "StageAttempt";

        public const string PhaseName = "PhaseName";

        public const string PhaseAttempt = "PhaseAttempt";

        public const string JobName = "JobName";

        public const string JobAttempt = "JobAttempt";
    }
}
