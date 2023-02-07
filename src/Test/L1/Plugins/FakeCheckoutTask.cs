// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Plugins.Repository;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeCheckoutTask : CheckoutTask
    {
        public FakeCheckoutTask() : base(new FakeSourceProviderFactory())
        { }

        public FakeCheckoutTask(ISourceProviderFactory SourceProviderFactory) : base(new FakeSourceProviderFactory())
        { }
    }

    public sealed class FakeSourceProviderFactory : SourceProviderFactory
    {
        public override ISourceProvider GetSourceProvider(string repositoryType)
        {
            ISourceProvider sourceProvider = base.GetSourceProvider(repositoryType);
            if (sourceProvider.GetType() == typeof(GitHubSourceProvider))
            {
                return new FakeGitHubSourceProvider();
            }
            else if (sourceProvider.GetType() == typeof(BitbucketGitSourceProvider))
            {
                return new FakeBitbucketGitSourceProvider();
            }
            else if (sourceProvider.GetType() == typeof(ExternalGitSourceProvider))
            {
                return new FakeExternalGitSourceProvider();
            }
            else if (sourceProvider.GetType() == typeof(TfsGitSourceProvider))
            {
                return new FakeTfsGitSourceProvider();
            }
            else
            {
                throw new Exception("Source provider not mocked: " + repositoryType);
            }
        }
    }

    public sealed class FakeGitHubSourceProvider : GitHubSourceProvider
    {
        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return new FakeGitCliManager(gitEnv);
        }
    }

    public sealed class FakeBitbucketGitSourceProvider : BitbucketGitSourceProvider
    {
        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return new FakeGitCliManager(gitEnv);
        }
    }

    public sealed class FakeExternalGitSourceProvider : ExternalGitSourceProvider
    {
        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return new FakeGitCliManager(gitEnv);
        }
    }

    public sealed class FakeTfsGitSourceProvider : TfsGitSourceProvider
    {
        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return new FakeGitCliManager(gitEnv);
        }
    }
}