// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Xunit;
using Moq;


namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class KnobL0
    {

        public class TestKnobs
        {
            public static Knob A = new Knob("A", "Test Knob", new RuntimeKnobSource("A"), new EnvironmentKnobSource("A"), new BuiltInDefaultKnobSource("false"));
            public static Knob B = new DeprecatedKnob("B", "Deprecated Knob", new BuiltInDefaultKnobSource("true"));
            public static Knob C = new ExperimentalKnob("C", "Experimental Knob", new BuiltInDefaultKnobSource("foo"));
            public static Knob D = new ExperimentalKnob("D", "Test knob only with default", new BuiltInDefaultKnobSource("foo"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void HasAgentKnobs()
        {
            Assert.True(Knob.GetAllKnobsFor<TestKnobs>().Count == 4, "GetAllKnobsFor returns the right amount");
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void BasicKnobTests()
        {
            Assert.True(!TestKnobs.A.IsDeprecated, "A is NOT Deprecated");
            Assert.True(!TestKnobs.A.IsExperimental, "A is NOT Experimental");

            var environment = new LocalEnvironment();

            var executionContext = new Mock<IExecutionContext>();
            executionContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            {
                var knobValue = TestKnobs.A.GetValue(executionContext.Object);
                Assert.True(knobValue.Source.GetType() == typeof(BuiltInDefaultKnobSource));
            }

            environment.SetEnvironmentVariable("A", "true");

            {
                var knobValue = TestKnobs.A.GetValue(executionContext.Object);
                Assert.True(knobValue.Source.GetType() == typeof(EnvironmentKnobSource));
                Assert.True(knobValue.AsBoolean());
                Assert.True(string.Equals(knobValue.AsString(), "true", StringComparison.OrdinalIgnoreCase));
            }

            environment.SetEnvironmentVariable("A", "false");

            {
                var knobValue = TestKnobs.A.GetValue(executionContext.Object);
                Assert.True(knobValue.Source.GetType() == typeof(EnvironmentKnobSource));
                Assert.True(!knobValue.AsBoolean());
                Assert.True(string.Equals(knobValue.AsString(), "false", StringComparison.OrdinalIgnoreCase));
            }

            environment.SetEnvironmentVariable("A", null);

            executionContext.Setup(x => x.GetVariableValueOrDefault(It.Is<string>(s => string.Equals(s, "A")))).Returns("true");

            {
                var knobValue = TestKnobs.A.GetValue(executionContext.Object);
                Assert.True(knobValue.Source.GetType() == typeof(RuntimeKnobSource));
                Assert.True(knobValue.AsBoolean());
                Assert.True(string.Equals(knobValue.AsString(), "true", StringComparison.OrdinalIgnoreCase));
            }

            executionContext.Setup(x => x.GetVariableValueOrDefault(It.Is<string>(s => string.Equals(s, "A")))).Returns("false");

            {
                var knobValue = TestKnobs.A.GetValue(executionContext.Object);
                Assert.True(knobValue.Source.GetType() == typeof(RuntimeKnobSource));
                Assert.True(!knobValue.AsBoolean());
                Assert.True(string.Equals(knobValue.AsString(), "false", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void DeprecatedKnobTests()
        {
            Assert.True(TestKnobs.B.IsDeprecated, "B is Deprecated");
            Assert.True(!TestKnobs.B.IsExperimental, "B is NOT Experimental");
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExperimentalKnobTests()
        {
            Assert.True(TestKnobs.C.IsExperimental, "C is Experimental");
            Assert.True(!TestKnobs.C.IsDeprecated, "C is NOT Deprecated");
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetSpecificKnobValueBySpecificType()
        {
            var environment = new LocalEnvironment();

            var executionContext = new Mock<IExecutionContext>();
            executionContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            environment.SetEnvironmentVariable("A", "true");

            var knobValue = TestKnobs.A.GetValue<BuiltInDefaultKnobSource>(executionContext.Object);
            Assert.True(knobValue.Source.GetType() == typeof(BuiltInDefaultKnobSource));
            Assert.Equal("false", knobValue.AsString());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void SpecificKnobTypeNotPresentTest()
        {
            var environment = new LocalEnvironment();

            var executionContext = new Mock<IExecutionContext>();
            executionContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            var knobValue = TestKnobs.D.GetValue<RuntimeKnobSource>(executionContext.Object);
            Assert.Equal(null, knobValue);

            knobValue = TestKnobs.D.GetValue<BuiltInDefaultKnobSource>(executionContext.Object);
            Assert.True(knobValue.Source.GetType() == typeof(BuiltInDefaultKnobSource));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void SpecificKnobTypeByInterfaceRestrictedTest()
        {
            var environment = new LocalEnvironment();

            var executionContext = new Mock<IExecutionContext>();
            executionContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            environment.SetEnvironmentVariable("A", "true");

            var knobValue = TestKnobs.A.GetValue<IEnvironmentKnobSource>(executionContext.Object);
            Assert.Equal(null, knobValue);

            knobValue = TestKnobs.A.GetValue<EnvironmentKnobSource>(executionContext.Object);
            Assert.True(knobValue.Source.GetType() == typeof(EnvironmentKnobSource));
            Assert.Equal("true", knobValue.AsString());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void NotRightKnobTypeSetTest()
        {
            var environment = new LocalEnvironment();

            var executionContext = new Mock<IExecutionContext>();
            executionContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(environment);

            var knobValue = TestKnobs.A.GetValue<IAgentService>(executionContext.Object);
            Assert.Equal(null, knobValue);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void PipelineFeatureKnobTest()
        {
            var executionContext = new Mock<IExecutionContext>();
            executionContext
                .Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.TestFeature"))
                .Returns("true");
            var knob = new Knob("TestKnob", "Pipeline Feature Knob", new PipelineFeatureSource("TestFeature"));

            var knobValue = knob.GetValue(executionContext.Object);

            Assert.True(knobValue.AsBoolean());
        }
    }
}
