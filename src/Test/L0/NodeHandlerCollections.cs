// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Single collection for ALL NodeHandler tests (legacy and unified).
    /// This ensures sequential execution to prevent environment variable conflicts.
    /// </summary>
    [CollectionDefinition("Unified NodeHandler Tests")]
    public class UnifiedNodeHandlerTestFixture : ICollectionFixture<UnifiedNodeHandlerTestFixture>
    {
        // This class is never instantiated, it's just a collection marker
    }
}