using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class ConditionL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EvaluatesBool()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "true").Result);
                Assert.Equal(true, new Condition(hc, "TRUE").Result);
                Assert.Equal(false, new Condition(hc, "false").Result);
                Assert.Equal(false, new Condition(hc, "FALSE").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EvaluatesAnd()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "and(true,true)").Result);
                Assert.Equal(true, new Condition(hc, "and(true,1)").Result);
                Assert.Equal(true, new Condition(hc, "and(true,'a')").Result);
                Assert.Equal(true, new Condition(hc, "and(true,0)").Result);
                Assert.Equal(true, new Condition(hc, "and(true,'')").Result);
                Assert.Equal(false, new Condition(hc, "and(true,false)").Result);
                Assert.Equal(false, new Condition(hc, "and(false,true)").Result);
                Assert.Equal(false, new Condition(hc, "and(false,false)").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void TreatsNumberAsTruthy()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "1").Result);
                Assert.Equal(true, new Condition(hc, ".5").Result);
                Assert.Equal(true, new Condition(hc, "0.5").Result);
                Assert.Equal(true, new Condition(hc, "2").Result);
                Assert.Equal(true, new Condition(hc, "-1").Result);
                Assert.Equal(true, new Condition(hc, "-.5").Result);
                Assert.Equal(true, new Condition(hc, "-0.5").Result);
                Assert.Equal(true, new Condition(hc, "-2").Result);
                Assert.Equal(false, new Condition(hc, "0").Result);
                Assert.Equal(false, new Condition(hc, "0.0").Result);
                Assert.Equal(false, new Condition(hc, "-0").Result);
                Assert.Equal(false, new Condition(hc, "-0.0").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void TreatsStringAsTruthy()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "'a'").Result);
                Assert.Equal(true, new Condition(hc, "'false'").Result);
                Assert.Equal(true, new Condition(hc, "'0'").Result);
                Assert.Equal(true, new Condition(hc, "' '").Result);
                Assert.Equal(false, new Condition(hc, "''").Result);
            }
        }
    }
}
