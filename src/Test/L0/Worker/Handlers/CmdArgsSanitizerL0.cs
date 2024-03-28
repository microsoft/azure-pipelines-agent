// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Test.L0.Worker.Handlers
{
    public class CmdArgsSanitizerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void EmptyLineTest()
        {
            string argsLine = "";
            string expectedArgs = "";

            var (actualArgs, _) = CmdArgsSanitizer.SanitizeArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Theory]
        [InlineData("1; 2", "1_#removed#_ 2")]
        [InlineData("1 ^^; 2", "1 ^^_#removed#_ 2")]
        [InlineData("1 ; 2 && 3", "1 _#removed#_ 2 _#removed#__#removed#_ 3")]
        [InlineData("; & > < |", "_#removed#_ _#removed#_ _#removed#_ _#removed#_ _#removed#_")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void SanitizeTest(string inputArgs, string expectedArgs)
        {
            var (actualArgs, _) = CmdArgsSanitizer.SanitizeArguments(inputArgs);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Theory]
        [InlineData("1 2")]
        [InlineData("1 ^; 2")]
        [InlineData("1 ^; 2 ^&^& 3 ^< ^> ^| ^^")]
        [InlineData(", / \\ aA zZ 09 ' \" - = : . * + ? ^ %")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void SanitizeSkipTest(string inputArgs)
        {
            var (actualArgs, _) = CmdArgsSanitizer.SanitizeArguments(inputArgs);

            Assert.Equal(inputArgs, actualArgs);
        }

        [Theory]
        [ClassData(typeof(SanitizerTelemetryTestsData))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void Telemetry_BasicTest(string inputArgs, int expectedRemovedSymbolsCount, Dictionary<string, int> expectedRemovedSymbols)
        {
            var (_, resultTelemetry) = CmdArgsSanitizer.SanitizeArguments(inputArgs);

            Assert.NotNull(resultTelemetry);
            Assert.Equal(expectedRemovedSymbolsCount, resultTelemetry.RemovedSymbolsCount);
            Assert.Equal(expectedRemovedSymbols, resultTelemetry.RemovedSymbols);
        }

        public class SanitizerTelemetryTestsData : TheoryData<string, int, Dictionary<string, int>>
        {
            public SanitizerTelemetryTestsData()
            {
                Add("; &&&;; $", 7, new() { [";"] = 3, ["&"] = 3, ["$"] = 1 });
                Add("aA zZ 09;", 1, new() { [";"] = 1 });
                Add("; & > < |", 5, new() { [";"] = 1, ["&"] = 1, [">"] = 1, ["<"] = 1, ["|"] = 1 });
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("123")]
        [InlineData("1 ^; ^&")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void Telemetry_ReturnsNull(string inputArgs)
        {
            var (_, resultTelemetry) = CmdArgsSanitizer.SanitizeArguments(inputArgs);

            Assert.Null(resultTelemetry);
        }
    }
}
