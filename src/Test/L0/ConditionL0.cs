using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class ConditionL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EqualCastsToMatchLeftSide()
        {
            using (var hc = new TestHostContext(this))
            {
                // Cast to bool.
                Assert.Equal(true, new Condition(hc, "eq(true,2)").Result);
                Assert.Equal(true, new Condition(hc, "eq(false,0)").Result);
                Assert.Equal(true, new Condition(hc, "eq(true,'a')").Result);
                Assert.Equal(true, new Condition(hc, "eq(true,' ')").Result);
                Assert.Equal(true, new Condition(hc, "eq(false,'')").Result);

                // Cast to string.
                Assert.Equal(true, new Condition(hc, "eq('TRue',true)").Result);
                Assert.Equal(true, new Condition(hc, "eq('FALse',false)").Result);
                Assert.Equal(true, new Condition(hc, "eq('123456.789',123456.789)").Result);
                Assert.Equal(false, new Condition(hc, "eq('123456.000',123456.000)").Result);

                // Cast to number (best effort).
                Assert.Equal(true, new Condition(hc, "eq(1,true)").Result);
                Assert.Equal(true, new Condition(hc, "eq(0,false)").Result);
                Assert.Equal(false, new Condition(hc, "eq(2,true)").Result);
                Assert.Equal(true, new Condition(hc, "eq(123456.789,' +123,456.7890 ')").Result);
                Assert.Equal(true, new Condition(hc, "eq(-123456.789,' -123,456.7890 ')").Result);
                Assert.Equal(true, new Condition(hc, "eq(123000,' 123,000.000 ')").Result);
                Assert.Equal(false, new Condition(hc, "eq(1,'not a number')").Result);
                Assert.Equal(false, new Condition(hc, "eq(0,'not a number')").Result);
            }
        }

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
                Assert.Equal(true, new Condition(hc, "and(true,true,true)").Result);
                Assert.Equal(true, new Condition(hc, "and(true,true)").Result);
                Assert.Equal(true, new Condition(hc, "and(true,1)").Result);
                Assert.Equal(true, new Condition(hc, "and(true,'a')").Result);
                Assert.Equal(false, new Condition(hc, "and(true,true,false)").Result);
                Assert.Equal(false, new Condition(hc, "and(true,0)").Result);
                Assert.Equal(false, new Condition(hc, "and(true,'')").Result);
                Assert.Equal(false, new Condition(hc, "and(true,false)").Result);
                Assert.Equal(false, new Condition(hc, "and(false,true)").Result);
                Assert.Equal(false, new Condition(hc, "and(false,false)").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EvaluatesEqual()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "eq(true,true)").Result);
                Assert.Equal(true, new Condition(hc, "eq(2,2)").Result);
                Assert.Equal(true, new Condition(hc, "eq('abcDEF','ABCdef')").Result);
                Assert.Equal(true, new Condition(hc, "eq(false,false)").Result);
                Assert.Equal(false, new Condition(hc, "eq(false,true)").Result);
                Assert.Equal(false, new Condition(hc, "eq(1,2)").Result);
                Assert.Equal(false, new Condition(hc, "eq('a','b')").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EvaluatesNot()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "not(false)").Result);
                Assert.Equal(true, new Condition(hc, "not(0)").Result);
                Assert.Equal(true, new Condition(hc, "not('')").Result);
                Assert.Equal(false, new Condition(hc, "not(true)").Result);
                Assert.Equal(false, new Condition(hc, "not(1)").Result);
                Assert.Equal(false, new Condition(hc, "not('a')").Result);
                Assert.Equal(false, new Condition(hc, "not(' ')").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void EvaluatesOr()
        {
            using (var hc = new TestHostContext(this))
            {
                Assert.Equal(true, new Condition(hc, "or(false,false,true)").Result);
                Assert.Equal(true, new Condition(hc, "or(false,true,false)").Result);
                Assert.Equal(true, new Condition(hc, "or(true,false,false)").Result);
                Assert.Equal(true, new Condition(hc, "or(false,1)").Result);
                Assert.Equal(true, new Condition(hc, "or(false,'a')").Result);
                Assert.Equal(false, new Condition(hc, "or(false,false,false)").Result);
                Assert.Equal(false, new Condition(hc, "or(false,0)").Result);
                Assert.Equal(false, new Condition(hc, "or(false,'')").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ShortCircuitsAndAfterFirstFalse()
        {
            using (var hc = new TestHostContext(this))
            {
                // The gt function should never evaluate. It would would throw since 'not a number'
                // cannot be converted to a number.
                Assert.Equal(false, new Condition(hc, "and(false,gt(1,'not a number'))").Result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ShortCircuitsOrAfterFirstTrue()
        {
            using (var hc = new TestHostContext(this))
            {
                // The gt function should never evaluate. It would would throw since 'not a number'
                // cannot be converted to a number.
                Assert.Equal(true, new Condition(hc, "or(true,gt(1,'not a number'))").Result);
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
