// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    /// <summary>
    /// This class is used to keep track of which repositories are being fetched and
    /// where they will be fetched to.
    /// This information is tracked per definition.
    /// </summary>
    public sealed class TrackingConfig : TrackingConfigBase
    {
        public const string FileFormatVersionJsonProperty = "fileFormatVersion";

        // The parameterless constructor is required for deserialization.
        public TrackingConfig()
        {
            RepositoryTrackingInfo = new List<RepositoryTrackingInfo>();
        }

        public TrackingConfig(
            IExecutionContext executionContext,
            LegacyTrackingConfig copy,
            string sourcesDirectoryNameOnly,
            string repositoryType,
            bool useNewArtifactsDirectoryName = false)
            : this()
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(copy, nameof(copy));

            // Set the directories.
            BuildDirectory = Path.GetFileName(copy.BuildDirectory); // Just take the portion after _work folder.
            string artifactsDirectoryNameOnly =
                useNewArtifactsDirectoryName ? Constants.Build.Path.ArtifactsDirectory : Constants.Build.Path.LegacyArtifactsDirectory;
            ArtifactsDirectory = Path.Combine(BuildDirectory, artifactsDirectoryNameOnly);
            SourcesDirectory = Path.Combine(BuildDirectory, sourcesDirectoryNameOnly);
            TestResultsDirectory = Path.Combine(BuildDirectory, Constants.Build.Path.TestResultsDirectory);

            // Set the other properties.
            CollectionId = copy.CollectionId;
            CollectionUrl = executionContext.Variables.System_TFCollectionUrl;
            DefinitionId = copy.DefinitionId;
            HashKey = copy.HashKey;
            RepositoryType = repositoryType;
            RepositoryUrl = copy.RepositoryUrl;
            System = copy.System;
            // Let's make sure this file gets cleaned up by the garbage collector
            LastRunOn = new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        public TrackingConfig Clone()
        {
            TrackingConfig clone = this.MemberwiseClone() as TrackingConfig;
            clone.RepositoryTrackingInfo = new List<RepositoryTrackingInfo>(this.RepositoryTrackingInfo);

            return clone;
        }

        public TrackingConfig(
            IExecutionContext executionContext,
            IList<RepositoryResource> repositories,
            int buildDirectory)
            : this()
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(repositories, nameof(repositories));

            // Get the repo that we are going to checkout first to create the tracking info from.
            var primaryRepository = RepositoryUtil.GetPrimaryRepository(repositories);

            // Set the directories.
            BuildDirectory = buildDirectory.ToString(CultureInfo.InvariantCulture);
            ArtifactsDirectory = Path.Combine(BuildDirectory, Constants.Build.Path.ArtifactsDirectory);
            SourcesDirectory = Path.Combine(BuildDirectory, Constants.Build.Path.SourcesDirectory);
            TestResultsDirectory = Path.Combine(BuildDirectory, Constants.Build.Path.TestResultsDirectory);

            // Set the other properties.
            CollectionId = executionContext.Variables.System_CollectionId;
            DefinitionId = executionContext.Variables.System_DefinitionId;
            RepositoryUrl = primaryRepository?.Url.AbsoluteUri;
            RepositoryType = primaryRepository?.Type;
            System = BuildSystem;
            UpdateJobRunProperties(executionContext);

            foreach (var repo in repositories)
            {
                RepositoryTrackingInfo.Add(new Build.RepositoryTrackingInfo(repo, SourcesDirectory));
            }

            // Now that we have all the repositories set up, we can compute the config hash
            HashKey = TrackingConfigHashAlgorithm.ComputeHash(CollectionId, DefinitionId, RepositoryTrackingInfo, AgentKnobs.UseSha256InComputeHash.GetValue(executionContext).AsBoolean());
        }

        [JsonIgnore]
        public string FileLocation { get; set; }

        [JsonProperty("build_artifactstagingdirectory")]
        public string ArtifactsDirectory { get; set; }

        [JsonProperty("agent_builddirectory")]
        public string BuildDirectory { get; set; }

        public string CollectionUrl { get; set; }

        public string DefinitionName { get; set; }

        public List<RepositoryTrackingInfo> RepositoryTrackingInfo { get; set; }

        // For back compat, we will ignore this property if it's null or empty
        public bool ShouldSerializeRepositoryTrackingInfo()
        {
            return RepositoryTrackingInfo != null && RepositoryTrackingInfo.Count > 0;
        }

        [JsonProperty(FileFormatVersionJsonProperty)]
        public int FileFormatVersion
        {
            get
            {
                // Any time this gets updated, the agent cannot be rolled back.
                // Back compat is guaranteed here, forward compat is not.
                return 3;
            }

            set
            {
                // Version 3 changes:
                //   CollectionName was removed.
                //   CollectionUrl was added.
                switch (value)
                {
                    case 3:
                    case 2:
                        break;
                    default:
                        // Should never reach here.
                        throw new NotSupportedException();
                }
            }
        }

        [JsonIgnore]
        public DateTimeOffset? LastRunOn { get; set; }

        [JsonProperty("lastRunOn")]
        [EditorBrowsableAttribute(EditorBrowsableState.Never)]
        public string LastRunOnString
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}", LastRunOn);
            }

            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    LastRunOn = null;
                    return;
                }

                LastRunOn = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
            }
        }

        public string RepositoryType { get; set; }

        [JsonIgnore]
        public DateTimeOffset? LastMaintenanceAttemptedOn { get; set; }

        [JsonProperty("lastMaintenanceAttemptedOn")]
        [EditorBrowsableAttribute(EditorBrowsableState.Never)]
        public string LastMaintenanceAttemptedOnString
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}", LastMaintenanceAttemptedOn);
            }

            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    LastMaintenanceAttemptedOn = null;
                    return;
                }

                LastMaintenanceAttemptedOn = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
            }
        }

        [JsonIgnore]
        public DateTimeOffset? LastMaintenanceCompletedOn { get; set; }

        [JsonProperty("lastMaintenanceCompletedOn")]
        [EditorBrowsableAttribute(EditorBrowsableState.Never)]
        public string LastMaintenanceCompletedOnString
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}", LastMaintenanceCompletedOn);
            }

            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    LastMaintenanceCompletedOn = null;
                    return;
                }

                LastMaintenanceCompletedOn = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
            }
        }

        [JsonProperty("build_sourcesdirectory")]
        public string SourcesDirectory { get; set; }

        [JsonProperty("common_testresultsdirectory")]
        public string TestResultsDirectory { get; set; }

        public void UpdateJobRunProperties(IExecutionContext executionContext)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));

            CollectionUrl = executionContext.Variables.System_TFCollectionUrl;
            DefinitionName = executionContext.Variables.Build_DefinitionName;
            LastRunOn = DateTimeOffset.Now;
        }
    }

    public class RepositoryTrackingInfo
    {
        public RepositoryTrackingInfo(RepositoryResource repositoryResource, string sourcesDirectoryRoot)
        {
            if (repositoryResource != null)
            {
                Identifier = repositoryResource.Alias;
                RepositoryType = repositoryResource.Type;
                RepositoryUrl = repositoryResource.Url.AbsoluteUri;
                SourcesDirectory = Path.Combine(sourcesDirectoryRoot, RepositoryUtil.GetCloneDirectory(repositoryResource));
            }
        }

        public RepositoryTrackingInfo() { }

        public string Identifier { get; set; }
        public string RepositoryType { get; set; }
        public string RepositoryUrl { get; set; }
        public string SourcesDirectory { get; set; }
    }
}
