// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class StepHostL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ContainerPayloadOmitsUnsetEnvironmentForPlainDictionary()
        {
            string payload = ContainerStepHost.CreateContainerStandardInPayload(
                "handler",
                "args",
                "work",
                new Dictionary<string, string> { ["EXAMPLE"] = "value" },
                null);

            var json = JObject.Parse(payload);

            Assert.Null(json["unsetEnvironment"]);
            Assert.Equal("value", json["environment"]["EXAMPLE"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ContainerPayloadIncludesUnsetEnvironmentForRemovalCapableDictionary()
        {
            var state = new TaskEnvironmentState();
            state.ApplyChanges(
                new Dictionary<string, string>
                {
                    ["REMOVED"] = "before",
                    ["RESTORED"] = "before",
                },
                new Dictionary<string, string>(),
                excludedNames: System.Array.Empty<string>());
            var environment = new TaskEnvironment(new Dictionary<string, string>
            {
                ["EXAMPLE"] = "value",
                ["RESTORED"] = "task",
            });
            environment.Reset(state.GetSnapshot());

            string payload = ContainerStepHost.CreateContainerStandardInPayload(
                "handler",
                "args",
                "work",
                environment,
                null);

            var json = JObject.Parse(payload);

            Assert.Equal(new[] { "REMOVED" }, json["unsetEnvironment"].Values<string>());
            Assert.Equal("value", json["environment"]["EXAMPLE"]);
            Assert.Equal("task", json["environment"]["RESTORED"]);
        }
    }
}
