// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{

    public class AgentKnobs
    {
        public static readonly Knob UseNode10 = new Knob(nameof(UseNode10), "Forces the agent to use Node 10 handler for all Node-based tasks",
                                                        new RuntimeKnobSource("AGENT_USE_NODE10"),
                                                        new EnvironmentKnobSource("AGENT_USE_NODE10"),
                                                        new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableAgentDowngrade = new Knob(nameof(DisableAgentDowngrade), "Disable agent downgrades. Upgrades will still be allowed.",
                                                            new EnvironmentKnobSource("AZP_AGENT_DOWNGRADE_DISABLED"),
                                                            new BuiltInDefaultKnobSource("false"));
    }

}
