// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{

    public class AgentKnobs
    {
        public static readonly Knob UseNode10 = new Knob(
            nameof(UseNode10),
            "Forces the agent to use Node 10 handler for all Node-based tasks",
            new RuntimeKnobSource("AGENT_USE_NODE10"),
            new EnvironmentKnobSource("AGENT_USE_NODE10"),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob DisableAgentDowngrade = new Knob(
            nameof(DisableAgentDowngrade),
            "Disable agent downgrades. Upgrades will still be allowed.",
            new EnvironmentKnobSource("AZP_AGENT_DOWNGRADE_DISABLED"),
            new BuiltInDefaultKnobSource("false"));
        
        public const string QuietCheckoutRuntimeVarName = "agent.source.checkout.quiet";
        public const string QuietCheckoutEnvVarName = "AGENT_SOURCE_CHECKOUT_QUIET";
        
        public static readonly Knob QuietCheckout = new Knob(
            nameof(QuietCheckout),
            "Aggressively reduce what gets logged to the console when checking out source.",
            new RuntimeKnobSource(QuietCheckoutRuntimeVarName),
            new EnvironmentKnobSource(QuietCheckoutEnvVarName),
            new BuiltInDefaultKnobSource("false"));

        public static readonly Knob ProxyAddress = new Knob(
            nameof(ProxyAddress),
            "Proxy server address if one exists",
            new EnvironmentKnobSource("VSTS_HTTP_PROXY"),
            new EnvironmentKnobSource("http_proxy"),
            new BuiltInDefaultKnobSource(""));

        public static readonly Knob ProxyUsername = new Knob(
            nameof(ProxyUsername),
            "Proxy username if one exists",
            new EnvironmentKnobSource("VSTS_HTTP_PROXY_USERNAME"),
            new BuiltInDefaultKnobSource(""));

        public static readonly Knob ProxyPassword = new Knob(
            nameof(ProxyPassword),
            "Proxy password if one exists",
            new EnvironmentKnobSource("VSTS_HTTP_PROXY_PASSWORD"),
            new BuiltInDefaultKnobSource(""));

        public static readonly Knob NoProxy = new Knob(
            nameof(NoProxy),
            "Proxy bypass list if one exists. Should be comma seperated",
            new EnvironmentKnobSource("no_proxy"),
            new BuiltInDefaultKnobSource(""));
    }

}
