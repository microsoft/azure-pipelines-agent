// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Agent.Sdk.SecretMasking;
using Agent.Sdk.SecretMasking;
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
        var secret1 = new RegexSecret("abc");
        var secret2 = new RegexSecret("abc");

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
        var secret = new RegexSecret("abc");
        var input = "defdefdef";

        // Act
        var positions = secret.GetPositions(input);

        // Assert
        Assert.Empty(positions);
    }
}