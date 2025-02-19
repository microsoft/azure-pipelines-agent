// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Build
{   
    public sealed class TrackingConfigHashAlgorithmL0
    {
        // This test is the original test case and is kept to make sure back compat still works.
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ComputeHash_returns_correct_hash()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                // Arrange.
                var collectionId = "7aee6dde-6381-4098-93e7-50a8264cf066";
                var definitionId = "7";
                var executionContext = new Mock<IExecutionContext>();
                List<string> warnings;
                executionContext
                    .Setup(x => x.Variables)
                    .Returns(new Variables(tc, copy: new Dictionary<string, VariableValue>(), warnings: out warnings));
                executionContext.Object.Variables.Set(Constants.Variables.System.CollectionId, collectionId);
                executionContext.Object.Variables.Set(Constants.Variables.System.DefinitionId, definitionId);

                var repoInfo = new RepositoryTrackingInfo
                {
                    RepositoryUrl = new Uri("http://contoso:8080/tfs/DefaultCollection/gitTest/_git/gitTest").AbsoluteUri,
                };

                // Act.
                string hashKey = TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repoInfo }, true);

                // Assert.
                Assert.Equal("55e3171bf43a983b419387b5d952d3ee7dcb195e262fc4c78d47a92763b6b001", hashKey);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ComputeHash_should_throw_when_parameters_invalid()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo = new RepositoryTrackingInfo()
                {
                    Identifier = "MyRepo",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                string collectionId = "866A5D79-7735-49E3-87DA-02E76CF8D03A";
                string definitionId = "123";

                Assert.Throws<ArgumentNullException>(() => TrackingConfigHashAlgorithm.ComputeHash(null, null, null, true));
                Assert.Throws<ArgumentNullException>(() => TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, null, true));
                Assert.Throws<ArgumentNullException>(() => TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { new RepositoryTrackingInfo() }, true));
                Assert.Throws<ArgumentNullException>(() => TrackingConfigHashAlgorithm.ComputeHash(null, null, new[] { repo }, true));
                Assert.Throws<ArgumentNullException>(() => TrackingConfigHashAlgorithm.ComputeHash(null, definitionId, new[] { repo }, true));
                Assert.Throws<ArgumentNullException>(() => TrackingConfigHashAlgorithm.ComputeHash(collectionId, null, new[] { repo }, true));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ComputeHash_with_single_repo_should_return_correct_hash()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo1 = new RepositoryTrackingInfo()
                {
                    Identifier = "alias",
                    RepositoryType = "git",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                var repo2 = new RepositoryTrackingInfo()
                {
                    Identifier = "alias2",
                    RepositoryType = "git2",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                string collectionId = "866A5D79-7735-49E3-87DA-02E76CF8D03A";
                string definitionId = "123";

                // Make sure that only the coll, def, and url are used in the hash
                Assert.Equal("a42b0f8ccd83cec8294b0c861a8d769e4f7fbc53ad3d3c96d2d1b66afdcdcca7", TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1 }, true));
                Assert.Equal("a42b0f8ccd83cec8294b0c861a8d769e4f7fbc53ad3d3c96d2d1b66afdcdcca7", TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo2 }, true));
                Assert.Equal(TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1 }, true), TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1 }, true));

                // Make sure that different coll creates different hash
                Assert.Equal("55437a6c3c12ea17847e33c3d96697833a05519e06acbe90fff74a64734fca1c", TrackingConfigHashAlgorithm.ComputeHash("FFFA5D79-7735-49E3-87DA-02E76CF8D03A", definitionId, new[] { repo1 }, true));

                // Make sure that different def creates different hash
                Assert.Equal("72443dbf31971f84922a6f3a6c58052fc0c60d9f1eb17b83a35e6e099098c179", TrackingConfigHashAlgorithm.ComputeHash(collectionId, "321", new[] { repo1 }, true));

                // Make sure that different url creates different hash
                repo1.RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/new_url";
                Assert.Equal("2b29540cb9d2b68cc068af7afd0593276fc9e0b09af4e5d7b2065cc9021070fc", TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1 }, true));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ComputeHash_with_multi_repos_should_return_correct_hash()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo1 = new RepositoryTrackingInfo()
                {
                    Identifier = "alias",
                    SourcesDirectory = "path/repo1_a",
                    RepositoryType = "git",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                var repo2 = new RepositoryTrackingInfo()
                {
                    Identifier = "alias2",
                    SourcesDirectory = "path/repo1_b",
                    RepositoryType = "git2",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                var repo2_newPath = new RepositoryTrackingInfo()
                {
                    Identifier = "alias2",
                    SourcesDirectory = "path/repo1_c",
                    RepositoryType = "git3",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                var repo1_newUrl = new RepositoryTrackingInfo()
                {
                    Identifier = "alias",
                    SourcesDirectory = "path/repo1_a",
                    RepositoryType = "git",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/new_url",
                };
                var repo1_newAlias = new RepositoryTrackingInfo()
                {
                    Identifier = "alias3",
                    SourcesDirectory = "path/repo1_a",
                    RepositoryType = "git",
                    RepositoryUrl = "https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url",
                };
                string collectionId = "866A5D79-7735-49E3-87DA-02E76CF8D03A";
                string definitionId = "123";

                // Make sure we get the same hash every time
                Assert.Equal("0a7fcd16ea54872456169a3cbf5a7d8e8efda976b755a13278b193fedaeb5784", TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1, repo2 }, true));

                // Make sure that only the coll, def, identifier, and url are used in the hash
                Assert.Equal(
                    TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1, repo2 }, true),
                    TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1, repo2_newPath }, true));

                // Make sure that different coll creates different hash
                Assert.Equal("b3956a6be8dde823bce2373fdef7358e255107bc4de986a61aeaffd11e253118", TrackingConfigHashAlgorithm.ComputeHash("FFFA5D79-7735-49E3-87DA-02E76CF8D03A", definitionId, new[] { repo1, repo2 }, true));

                // Make sure that different def creates different hash
                Assert.Equal("dff47196d014b4373641e33627901f986cde0815de0122fa76f401abd1140701", TrackingConfigHashAlgorithm.ComputeHash(collectionId, "321", new[] { repo1, repo2 }, true));

                // Make sure that different url creates different hash
                Assert.Equal("ce83c8cd4f9b603345d21d2a294f7126e1e37c6d13cf6225516106a69528cc95", TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1_newUrl, repo2 }, true));

                // Make sure that different alias creates different hash
                Assert.Equal("2ca4bc7221eb412db850596fc02dc4e5b61c2125c997ea07f11215bffe605d33", TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1_newAlias, repo2 }, true));

                // Make sure order doesn't change hash
                Assert.Equal(
                    TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo1, repo2 }, true),
                    TrackingConfigHashAlgorithm.ComputeHash(collectionId, definitionId, new[] { repo2, repo1 }, true));

            }
        }

    }
}
