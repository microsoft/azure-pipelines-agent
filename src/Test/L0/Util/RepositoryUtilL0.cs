// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public sealed class RepositoryUtilL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void TrimStandardBranchPrefix_should_return_correct_values()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Equal(null, RepositoryUtil.TrimStandardBranchPrefix(null));
                Assert.Equal("", RepositoryUtil.TrimStandardBranchPrefix(""));
                Assert.Equal("refs/branchName", RepositoryUtil.TrimStandardBranchPrefix("refs/branchName"));
                Assert.Equal("branchName", RepositoryUtil.TrimStandardBranchPrefix("refs/heads/branchName"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void HasMultipleCheckouts_should_not_throw()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(null));
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict.Add("x", "y");
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict.Add(WellKnownJobSettings.HasMultipleCheckouts, "burger");
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void HasMultipleCheckouts_should_return_true_when_set_correctly()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(null));
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "true";
                Assert.Equal(true, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "TRUE";
                Assert.Equal(true, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "True";
                Assert.Equal(true, RepositoryUtil.HasMultipleCheckouts(dict));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void HasMultipleCheckouts_should_return_false_when_not_set_correctly()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(null));
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "!true";
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "false";
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "FALSE";
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "False";
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "0";
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
                dict[WellKnownJobSettings.HasMultipleCheckouts] = "1";
                Assert.Equal(false, RepositoryUtil.HasMultipleCheckouts(dict));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void IsPrimaryRepositoryName_should_work_correctly()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Assert.Equal(false, RepositoryUtil.IsPrimaryRepositoryName(null));
                Assert.Equal(false, RepositoryUtil.IsPrimaryRepositoryName(""));
                Assert.Equal(false, RepositoryUtil.IsPrimaryRepositoryName("none"));
                Assert.Equal(false, RepositoryUtil.IsPrimaryRepositoryName("some random string"));
                Assert.Equal(true, RepositoryUtil.IsPrimaryRepositoryName("self"));
                Assert.Equal(true, RepositoryUtil.IsPrimaryRepositoryName("SELF"));
                Assert.Equal(true, RepositoryUtil.IsPrimaryRepositoryName("Self"));
                Assert.Equal(true, RepositoryUtil.IsPrimaryRepositoryName("sELF"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetPrimaryRepository_should_return_correct_value_when_called()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo1 = new RepositoryResource
                {
                    Alias = "repo1",
                    Id = "repo1",
                    Type = "git",
                };

                var repo2 = new RepositoryResource
                {
                    Alias = "repo2",
                    Id = "repo2",
                    Type = "git",
                };

                var repoSelf = new RepositoryResource
                {
                    Alias = "self",
                    Id = "repo3",
                    Type = "git",
                };

                Assert.Equal(null, RepositoryUtil.GetPrimaryRepository(null));
                Assert.Equal(repo1, RepositoryUtil.GetPrimaryRepository(new[] { repo1 }));
                Assert.Equal(repo2, RepositoryUtil.GetPrimaryRepository(new[] { repo2 }));
                Assert.Equal(repoSelf, RepositoryUtil.GetPrimaryRepository(new[] { repoSelf }));
                Assert.Equal(null, RepositoryUtil.GetPrimaryRepository(new[] { repo1, repo2 }));
                Assert.Equal(repoSelf, RepositoryUtil.GetPrimaryRepository(new[] { repoSelf, repo1, repo2 }));
                Assert.Equal(repoSelf, RepositoryUtil.GetPrimaryRepository(new[] { repo1, repoSelf, repo2 }));
                Assert.Equal(repoSelf, RepositoryUtil.GetPrimaryRepository(new[] { repo1, repo2, repoSelf }));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetRepositoryForLocalPath_should_return_correct_values()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo1 = new RepositoryResource
                {
                    Alias = "repo1",
                    Id = "repo1",
                    Type = "git",
                };
                repo1.Properties.Set(RepositoryPropertyNames.Path, Path.Combine("root", "1", "s", "repo1"));

                var repo2 = new RepositoryResource
                {
                    Alias = "repo2",
                    Id = "repo2",
                    Type = "git",
                };
                repo2.Properties.Set(RepositoryPropertyNames.Path, Path.Combine("root", "1", "s", "repo2"));

                var repo3 = new RepositoryResource
                {
                    Alias = "repo3",
                    Id = "repo3",
                    Type = "git",
                };
                // repo3 has no path

                // Make sure null is returned if nothing matches or inputs are invalid
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(null, null));
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(null, Path.Combine("root", "1", "s", "not_a_repo")));
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, null));
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, "not a path"));
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s", "not_a_repo")));
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s")));
                Assert.Equal(null, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s", "repo3")));

                // Make sure the first repo is returned if there is only one
                Assert.Equal(repo1, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1 }, Path.Combine("root", "1", "s", "not_a_repo")));
                Assert.Equal(repo2, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo2 }, "not a path"));
                Assert.Equal(repo3, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo3 }, "not a path"));

                // Make sure the matching repo is returned if there is more than one
                Assert.Equal(repo1, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s", "repo1")));
                Assert.Equal(repo1, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s", "repo1", "sub", "path", "file.txt")));
                Assert.Equal(repo2, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s", "repo2")));
                Assert.Equal(repo2, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo1, repo2, repo3 }, Path.Combine("root", "1", "s", "repo2", "sub", "path", "file.txt")));
                Assert.Equal(repo2, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo3, repo1, repo2 }, Path.Combine("root", "1", "s", "repo2")));
                Assert.Equal(repo2, RepositoryUtil.GetRepositoryForLocalPath(new[] { repo3, repo1, repo2 }, Path.Combine("root", "1", "s", "repo2", "sub", "path", "file.txt")));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetRepository_should_return_correct_value_when_called()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo1 = new RepositoryResource
                {
                    Alias = "repo1",
                    Id = "repo1",
                    Type = "git",
                };

                var repo2 = new RepositoryResource
                {
                    Alias = "repo2",
                    Id = "repo2",
                    Type = "git",
                };

                var repoSelf = new RepositoryResource
                {
                    Alias = "self",
                    Id = "repo3",
                    Type = "git",
                };

                Assert.Equal(null, RepositoryUtil.GetRepository(null, null));
                Assert.Equal(null, RepositoryUtil.GetRepository(null, "repo1"));
                Assert.Equal(null, RepositoryUtil.GetRepository(new[] { repoSelf, repo1, repo2 }, null));
                Assert.Equal(null, RepositoryUtil.GetRepository(new[] { repoSelf, repo1, repo2 }, "unknown"));
                Assert.Equal(repo1, RepositoryUtil.GetRepository(new[] { repo1, repo2 }, "repo1"));
                Assert.Equal(repo2, RepositoryUtil.GetRepository(new[] { repo1, repo2 }, "repo2"));
                Assert.Equal(repo1, RepositoryUtil.GetRepository(new[] { repoSelf, repo1, repo2 }, "repo1"));
                Assert.Equal(repo2, RepositoryUtil.GetRepository(new[] { repoSelf, repo1, repo2 }, "repo2"));
                Assert.Equal(repoSelf, RepositoryUtil.GetRepository(new[] { repoSelf, repo1, repo2 }, "self"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GuessRepositoryType_should_return_correct_values_when_called()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType(null));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType(""));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType("garbage"));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType("github"));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType("azuredevops"));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType("https://githubenterprise.com/microsoft/somerepo.git"));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType("https://almost.visual.studio.com/microsoft/somerepo.git"));
                Assert.Equal(string.Empty, RepositoryUtil.GuessRepositoryType("https://almost.dev2.azure.com/microsoft/somerepo.git"));
                Assert.Equal(RepositoryTypes.GitHub, RepositoryUtil.GuessRepositoryType("https://github.com/microsoft/somerepo.git"));
                Assert.Equal(RepositoryTypes.Git, RepositoryUtil.GuessRepositoryType("https://user1@dev.azure.com/org/project/_git/reponame"));
                Assert.Equal(RepositoryTypes.Git, RepositoryUtil.GuessRepositoryType("https://user1@myorg.visualstudio.com/project/_git/reponame"));
                Assert.Equal(RepositoryTypes.Tfvc, RepositoryUtil.GuessRepositoryType("https://user1@myorg.visualstudio.com/project"));
                Assert.Equal(RepositoryTypes.Tfvc, RepositoryUtil.GuessRepositoryType("https://user1@dev.azure.com/org/project"));
                Assert.Equal(RepositoryTypes.Bitbucket, RepositoryUtil.GuessRepositoryType("https://user1@bitbucket.org/user1/mybucket.git"));

            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCloneDirectory_REPO_should_throw_on_null()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Throws<ArgumentNullException>(() => RepositoryUtil.GetCloneDirectory((RepositoryResource)null));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCloneDirectory_REPO_should_return_proper_value_when_called()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                var repo = new RepositoryResource()
                {
                    Alias = "alias",
                    Id = "repo1",
                    Type = "git",
                    Url = null,
                };

                // If name is not set and url is not set, then it should use alias
                Assert.Equal("alias", RepositoryUtil.GetCloneDirectory(repo));

                // If url is set, it should choose url over alias
                repo.Url = new Uri("https://jpricket@codedev.ms/jpricket/MyFirstProject/_git/repo1_url");
                Assert.Equal("repo1_url", RepositoryUtil.GetCloneDirectory(repo));

                // If name is set, it should choose name over alias or url
                repo.Properties.Set(RepositoryPropertyNames.Name, "MyFirstProject/repo1_name");
                Assert.Equal("repo1_name", RepositoryUtil.GetCloneDirectory(repo));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCloneDirectory_STRING_should_throw_on_null()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                Assert.Throws<ArgumentNullException>(() => RepositoryUtil.GetCloneDirectory((string)null));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCloneDirectory_STRING_should_return_proper_value_when_called()
        {
            // These test cases were inspired by the test cases that git.exe uses
            // see https://github.com/git/git/blob/53f9a3e157dbbc901a02ac2c73346d375e24978c/t/t5603-clone-dirname.sh#L21

            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // basic syntax with bare and non-bare variants
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo.git"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo/.git"));

                // similar, but using ssh URL rather than host:path syntax
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo.git"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo/.git"));

                // we should remove trailing slashes and .git suffixes
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo///"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo/.git/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo.git/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo.git///"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo///.git/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("ssh://host/foo/.git///"));

                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo///"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo.git/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo/.git/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo.git///"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo///.git/"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo/.git///"));
                Assert.Equal("foo", RepositoryUtil.GetCloneDirectory("host:foo/.git///"));
                Assert.Equal("repo", RepositoryUtil.GetCloneDirectory("host:foo/repo"));

                // omitting the path should default to the hostname
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("ssh://host/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("ssh://host:1234/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("ssh://user@host/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("host:/"));

                // auth materials should be redacted
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("ssh://user:password@host/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("ssh://user:password@host:1234/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("ssh://user:passw@rd@host:1234/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("user@host:/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("user:password@host:/"));
                Assert.Equal("host", RepositoryUtil.GetCloneDirectory("user:passw@rd@host:/"));

                // trailing port-like numbers should not be stripped for paths
                Assert.Equal("1234", RepositoryUtil.GetCloneDirectory("ssh://user:password@host/test:1234"));
                Assert.Equal("1234", RepositoryUtil.GetCloneDirectory("ssh://user:password@host/test:1234.git"));
            }
        }

    }
}
