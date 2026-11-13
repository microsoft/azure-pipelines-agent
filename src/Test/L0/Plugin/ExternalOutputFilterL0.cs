// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Plugin
{
    public sealed class ExternalOutputFilterL0
    {
        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData("", "")]
        [InlineData("ordinary output", "ordinary output")]
        [InlineData("##vso[task.setvariable variable=x]value", "##_vso[task.setvariable variable=x]value")]
        [InlineData("prefix ##vso[task.complete]x suffix", "prefix ##_vso[task.complete]x suffix")]
        [InlineData("##vso[task.debug]a ##vso[task.complete]b", "##_vso[task.debug]a ##_vso[task.complete]b")]
        [InlineData("##VSO[task.complete]x", "##VSO[task.complete]x")]
        [InlineData("##vso", "##vso")]
        public void BlocksCommandsByDefault(string input, string expected)
        {
            var options = new ExternalOutputOptions();

            Assert.Equal(expected, ExternalOutputFilter.Filter(input, options));
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData("##vso[task.debug]message", "##vso[task.debug]message")]
        [InlineData("##vso[task.setprogress value=50]message", "##vso[task.setprogress value=50]message")]
        [InlineData("##vso[TASK.Debug]message", "##vso[TASK.Debug]message")]
        [InlineData("##vso[task..debug]message", "##vso[task..debug]message")]
        [InlineData("##vso[task.setvariable variable=x]value", "##_vso[task.setvariable variable=x]value")]
        [InlineData("##vso[task.complete result=Failed]done", "##_vso[task.complete result=Failed]done")]
        [InlineData("##vso[task]message", "##_vso[task]message")]
        [InlineData("##vso[a.b.c]message", "##_vso[a.b.c]message")]
        [InlineData("##vso[task.debug", "##_vso[task.debug")]
        [InlineData("##vso[task.debug\n]message", "##_vso[task.debug\n]message")]
        public void UsesDefaultAllowlist(string input, string expected)
        {
            var options = new ExternalOutputOptions
            {
                EnableVsoCommands = true
            };

            Assert.Equal(expected, ExternalOutputFilter.Filter(input, options));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ExplicitAllowlistReplacesDefaults()
        {
            var options = new ExternalOutputOptions
            {
                EnableVsoCommands = true,
                AllowedVsoCommands = new[] { "task.logissue" }
            };

            string input = "##vso[task.debug]a ##vso[task.logissue type=warning]b";
            string expected = "##_vso[task.debug]a ##vso[task.logissue type=warning]b";

            Assert.Equal(expected, ExternalOutputFilter.Filter(input, options));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void EmptyAllowlistBlocksAllCommands()
        {
            var options = new ExternalOutputOptions
            {
                EnableVsoCommands = true,
                AllowedVsoCommands = Array.Empty<string>()
            };

            Assert.Equal(
                "##_vso[task.debug]message",
                ExternalOutputFilter.Filter("##vso[task.debug]message", options));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void OversizedHeaderIsBlocked()
        {
            string command = $"task.{"d".PadLeft(252, 'd')}";
            var options = new ExternalOutputOptions
            {
                EnableVsoCommands = true,
                AllowedVsoCommands = new[] { command }
            };

            Assert.Equal(
                $"##_vso[{command}]message",
                ExternalOutputFilter.Filter($"##vso[{command}]message", options));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void NullArgumentsFailClosed()
        {
            var options = new ExternalOutputOptions();

            Assert.Equal(string.Empty, ExternalOutputFilter.Filter(null, options));
            Assert.Equal(
                "##_vso[task.debug]message",
                ExternalOutputFilter.Filter("##vso[task.debug]message", null));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void OutputExternalFiltersBeforeWriting()
        {
            var messages = new List<string>();
            var trace = new Mock<ITraceWriter>();
            trace
                .Setup(x => x.Info(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string message, string operation) => messages.Add(message));
            var context = new AgentTaskPluginExecutionContext(trace.Object);
            context.Variables["AZP_AGENT_ENABLE_PLUGIN_EXTERNAL_OUTPUT_FILTERING"] =
                new VariableValue("true");
            var options = new ExternalOutputOptions();

            context.OutputExternal(null, null);
            context.OutputExternal("##vso[task.setvariable variable=x]external", options);
            context.Output("##vso[task.setvariable variable=x]trusted");

            Assert.Equal(
                new[]
                {
                    "##_vso[task.setvariable variable=x]external",
                    "##vso[task.setvariable variable=x]trusted"
                },
                messages);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void OutputExternalPreservesOutputWhenFilteringIsDisabled()
        {
            var messages = new List<string>();
            var trace = new Mock<ITraceWriter>();
            trace
                .Setup(x => x.Info(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string message, string operation) => messages.Add(message));
            var context = new AgentTaskPluginExecutionContext(trace.Object);
            const string command = "##vso[task.setvariable variable=x]value";

            context.OutputExternal(null);
            context.OutputExternal(command, new ExternalOutputOptions());

            Assert.Equal(new[] { command }, messages);
        }
    }
}
