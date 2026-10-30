// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;

namespace Test.L0.Plugin.TestGitSourceProvider
{
    // The commands that run against an existing workspace before fetch - reset, and sparse-checkout set or
    // disable - can lazily fetch missing blobs on a partial clone, so they need the same auth header as checkout.
    public sealed class PreSyncPartialCloneCredentialsL0
    {
        private const string RepoUrl = "https://dev.azure.com/contoso/proj/_git/repo";

        private static async Task<MockPreSyncGitSourceProvider> RunGetSourceAsync(
            TestHostContext hc,
            bool partialClone,
            bool enhancedKnob,
            string sparseCheckoutDirectories = null,
            IEnumerable<string> leftoverExtraheaders = null)
        {
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());
            string target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                // An existing workspace left by an earlier job on the same agent.
                Directory.CreateDirectory(Path.Combine(target, ".git"));

                var endpoint = new ServiceEndpoint()
                {
                    Name = WellKnownServiceEndpointNames.SystemVssConnection,
                    Id = Guid.NewGuid(),
                    Url = new Uri("https://dev.azure.com/contoso/"),
                    Authorization = new EndpointAuthorization()
                    {
                        Scheme = EndpointAuthorizationSchemes.OAuth,
                        Parameters = { { EndpointAuthorizationParameters.AccessToken, "TOKEN" } }
                    }
                };
                tc.Endpoints.Add(endpoint);

                var repository = new Pipelines.RepositoryResource()
                {
                    Alias = "self",
                    Type = Pipelines.RepositoryTypes.Git,
                    Url = new Uri(RepoUrl),
                    Endpoint = new Pipelines.ServiceEndpointReference() { Id = endpoint.Id }
                };
                repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, target);
                tc.Repositories.Add(repository);

                tc.Variables.Add("system.collectionuri", "https://dev.azure.com/contoso/");
                tc.Variables.Add("AGENT_USE_SPARSE_CHECKOUT_IN_CHECKOUT_TASK", "true");
                tc.Variables.Add("ADD_FORCE_CREDENTIALS_TO_GIT_CHECKOUT_ENHANCED", enhancedKnob.ToString());

                tc.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean] = "true";
                tc.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchFilter] = "blob:none";
                if (sparseCheckoutDirectories != null)
                {
                    tc.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.SparseCheckoutDirectories] = sparseCheckoutDirectories;
                }

                var provider = new MockPreSyncGitSourceProvider();
                provider.CliManager.RemoteUrl = RepoUrl;
                if (partialClone)
                {
                    provider.CliManager.Config["remote.origin.promisor"] = new List<string> { "true" };
                    provider.CliManager.Config["remote.origin.partialclonefilter"] = new List<string> { "blob:none" };
                }

                if (leftoverExtraheaders != null)
                {
                    provider.CliManager.Config["http.extraheader"] = leftoverExtraheaders.ToList();
                }

                await provider.GetSourceAsync(tc, repository, CancellationToken.None);
                return provider;
            }
            finally
            {
                if (Directory.Exists(target)) { Directory.Delete(target, true); }
            }
        }

        private static MockPreSyncGitCliManager.ExecutedCommand Single(MockPreSyncGitSourceProvider provider, string command)
            => Assert.Single(provider.CliManager.ExecutedCommands, c => c.Command == command);

        private static void AssertCarriesAuthHeader(MockPreSyncGitCliManager.ExecutedCommand command)
        {
            Assert.NotNull(command.AdditionalCommandLine);
            Assert.Contains("http.extraheader", command.AdditionalCommandLine);
            Assert.Contains("AUTHORIZATION: ", command.AdditionalCommandLine);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PartialClone_NonSparseJob_ResetAndSparseDisableCarryAuthHeader()
        {
            using TestHostContext hc = new(this);
            var provider = await RunGetSourceAsync(hc, partialClone: true, enhancedKnob: true);

            AssertCarriesAuthHeader(Single(provider, "reset"));
            AssertCarriesAuthHeader(Single(provider, "sparse-checkout disable"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PartialClone_SparseJob_SparseInitAndSetCarryAuthHeader()
        {
            using TestHostContext hc = new(this);
            var provider = await RunGetSourceAsync(hc, partialClone: true, enhancedKnob: true, sparseCheckoutDirectories: "src");

            AssertCarriesAuthHeader(Single(provider, "sparse-checkout init"));
            AssertCarriesAuthHeader(Single(provider, "sparse-checkout set"));
            Assert.DoesNotContain(provider.CliManager.ExecutedCommands, c => c.Command == "sparse-checkout disable");
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PartialClone_CleanDoesNotCarryAuthHeader()
        {
            // git clean only removes untracked files and never reads an object, so it is given no credentials.
            using TestHostContext hc = new(this);
            var provider = await RunGetSourceAsync(hc, partialClone: true, enhancedKnob: true);

            Assert.Null(Single(provider, "clean").AdditionalCommandLine);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PartialClone_EnhancedKnobOff_PreSyncCommandsCarryNoAuthHeader()
        {
            using TestHostContext hc = new(this);
            var provider = await RunGetSourceAsync(hc, partialClone: true, enhancedKnob: false);

            Assert.Null(Single(provider, "reset").AdditionalCommandLine);
            Assert.Null(Single(provider, "sparse-checkout disable").AdditionalCommandLine);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task FullClone_PreSyncCommandsCarryNoAuthHeader()
        {
            using TestHostContext hc = new(this);
            var provider = await RunGetSourceAsync(hc, partialClone: false, enhancedKnob: true, sparseCheckoutDirectories: "src");

            Assert.Null(Single(provider, "reset").AdditionalCommandLine);
            Assert.Null(Single(provider, "sparse-checkout init").AdditionalCommandLine);
            Assert.Null(Single(provider, "sparse-checkout set").AdditionalCommandLine);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PartialClone_LeftoverExtraheaderIsRemovedBeforeFirstAuthenticatedPreSyncCommand()
        {
            // http.extraheader is multi-valued: a header persisted by an earlier job would otherwise be sent
            // alongside the one passed on the command line.
            using TestHostContext hc = new(this);
            var provider = await RunGetSourceAsync(hc, partialClone: true, enhancedKnob: true,
                leftoverExtraheaders: new[] { "AUTHORIZATION: bearer STALE" });

            var commands = provider.CliManager.ExecutedCommands;
            int unset = commands.FindIndex(c => c.Command == "config" && c.Options == "--unset-all http.extraheader");
            int reset = commands.FindIndex(c => c.Command == "reset");

            Assert.True(unset >= 0, "the leftover http.extraheader was never unset");
            Assert.True(unset < reset, "the leftover http.extraheader was unset only after git reset ran");
        }
    }
}
