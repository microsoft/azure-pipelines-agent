// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Collection definition for ALL NodeHandler tests to control parallel execution.
    /// 
    /// This collection prevents environment variable contamination between:
    /// - Legacy tests (NodeHandlerL0) that use Environment.SetEnvironmentVariable
    /// - Unified tests (NodeHandlerL0AllSpecs, etc.) that use the new strategy
    /// 
    /// All NodeHandler tests run sequentially within this collection to prevent
    /// global environment variable conflicts that cause test inconsistencies.
    /// </summary>

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