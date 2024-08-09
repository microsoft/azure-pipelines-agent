// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Agent.Sdk.SecretMasking;
using Agent.Sdk.SecretMasking;

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests;

public class RegexSecretL0
{
    [Fact]
    [Trait("Level", "L0")]
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
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsFalse_WhenPatternsDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc");
        var secret2 = new RegexSecret("def");

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsTrue_WhenMonikersAreEqual()
    {
        // Arrange
        var moniker = "TST1001.TestRule";
        var secret1 = new RegexSecret("abc", moniker);
        var secret2 = new RegexSecret("abc", moniker);

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsFalse_WhenMonikersDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc", "TST1001.TestRule");
        var secret2 = new RegexSecret("abc", "TST1002.TestRule");

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsTrue_WhenSniffLiteralsAreEqual()
    {
        // Arrange
        var sniffLiterals = new HashSet<string>(new[] { "sniff" });
        var secret1 = new RegexSecret("abc", sniffLiterals: sniffLiterals);
        var secret2 = new RegexSecret("abc", sniffLiterals: sniffLiterals);

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsFalse_WhenSniffLiteralsDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc", sniffLiterals: new HashSet<string>(new[] { "sniff1" }));
        var secret2 = new RegexSecret("abc", sniffLiterals: new HashSet<string>(new[] { "sniff2", "sniff3" }));

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsTrue_WhenRegexOptionsAreEqual()
    {
        // Arrange
        RegexOptions regexOptions = RegexOptions.IgnoreCase;
        var secret1 = new RegexSecret("abc", regexOptions: regexOptions);
        var secret2 = new RegexSecret("abc", regexOptions: regexOptions);

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsFalse_WhenRegexOptionsDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc", regexOptions: RegexOptions.Multiline);
        var secret2 = new RegexSecret("abc", regexOptions: RegexOptions.IgnoreCase);

        // Act
        var result = secret1.Equals(secret2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void Equals_ReturnsFalse_WhenComparedToNull()
    {
        // Arrange
        var secret1 = new RegexSecret("abc");

        // Act
        var result = secret1.Equals(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetHashCode_ReturnsUniqueValue_WhenPatternsDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc");
        var secret2 = new RegexSecret("def");

        // Act
        var hashCodeDiffers = secret1.GetHashCode() == secret2.GetHashCode();

        // Assert
        Assert.False(hashCodeDiffers);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetHashCode_ReturnsUniqueValue_WhenMonikersDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc", "TST1001.TestRule");
        var secret2 = new RegexSecret("abc", "TST1002.TestRule");
        var set = new HashSet<RegexSecret>(new[] { secret1 });

        // Act
        var hashCodeDiffers = secret1.GetHashCode() == secret2.GetHashCode();

        // Assert
        Assert.False(hashCodeDiffers);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetHashCode_ReturnsUniqueValue_WhenSniffLiteralsDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc", sniffLiterals: new HashSet<string>(new[] { "sniff1" }));
        var secret2 = new RegexSecret("abc", sniffLiterals: new HashSet<string>(new[] { "sniff2" }));
        var set = new HashSet<RegexSecret>(new[] { secret1 });

        // Act
        var hashCodeDiffers = secret1.GetHashCode() == secret2.GetHashCode();

        // Assert
        Assert.False(hashCodeDiffers);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetHashCode_ReturnsUniqueValue_WhenRegexOptionsDiffer()
    {
        // Arrange
        var secret1 = new RegexSecret("abc", regexOptions: RegexOptions.Multiline);
        var secret2 = new RegexSecret("abc", regexOptions: RegexOptions.IgnoreCase);
        var set = new HashSet<RegexSecret>(new[] { secret1 });

        // Act
        var hashCodeDiffers = secret1.GetHashCode() == secret2.GetHashCode();

        // Assert
        Assert.False(hashCodeDiffers);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetReplacements_ReturnsEmpty_WhenNoMatchesExist()
    {
        // Arrange
        var secret = new RegexSecret("abc");
        var input = "defdefdef";

        // Act
        var replacements = secret.GetReplacements(input);

        // Assert
        Assert.Empty(replacements);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetReplacements_Returns_DefaultRedactionToken()
    {
        // Arrange
        var secret = new RegexSecret("abc");
        var input = "abc";

        // Act
        var replacements = secret.GetReplacements(input);

        // Assert
        Assert.NotEmpty(replacements);
        Assert.True(replacements.Count()  == 1);
        Assert.True(replacements.First().Token == "+++");
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "RegexSecret")]
    public void GetReplacements_Returns_SecureTelemetryTokenValue_WhenMonikerSpecified()
    {
        // Arrange
        var ruleMoniker = "TST00.TestRule";
        var secret = new RegexSecret("abc", "TST00.TestRule");
        var input = "abc";

        var hash = RegexSecret.HashString(input);

        // Act
        var replacements = secret.GetReplacements(input);

        // Assert
        Assert.NotEmpty(replacements);
        Assert.True(replacements.Count() == 1);
        Assert.True(replacements.First().Token == $"{ruleMoniker}:{hash}");
    }

}