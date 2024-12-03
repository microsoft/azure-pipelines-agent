// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Agent.Sdk.SecretMasking;
using Agent.Sdk.SecretMasking;

using Microsoft.Security.Utilities;

using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests;

public class RegexSecretL0
{
    [Fact]
    [Trait("Level","L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsTrue_WhenPatternsAreEqual()
    {
        // Arrange
        var secret1 = new RegexPattern("101", "TestRule", 0, "abc");
        var secret2 = new RegexPattern("101", "TestRule", 0, "abc");

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.True(result);
    }
    [Fact]
    [Trait("Level","L0")]
    [Trait("Category", "RegexSecret")]
    public void GetPositions_ReturnsEmpty_WhenNoMatchesExist()
    {
        // Arrange
        var secret = new RegexPattern("101", "TestRule", 0, ("abc"));
        var input = "defdefdef";

        // Act
        var positions = secret.GetDetections(input, generateCrossCompanyCorrelatingIds: false);

        // Assert
        Assert.Empty(positions);
    }
}