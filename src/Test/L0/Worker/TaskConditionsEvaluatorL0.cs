using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class TaskConditionsEvaluatorL0
    {
        private Mock<IExecutionContext> _executionContext;

        private Variables _variables;

        private List<TaskCondition> _conditions = new List<TaskCondition>();

        public static IEnumerable<object[]> EqualConditionCases
        {
            get
            {
                return new[]
                {
                    new object[] { new List<TaskCondition> { GetCondition("notfound", ConditionOperator.Equals, "val") }, false },
                    new object[] { new List<TaskCondition> { GetCondition("var1", ConditionOperator.Equals, "unequalValue") }, false },
                    new object[] { new List<TaskCondition> { GetCondition("VAR2", ConditionOperator.Equals, "VAL") }, false },
                    new object[] { new List<TaskCondition> { GetCondition("vAR3", ConditionOperator.Equals, "VALue3") }, true },
                    new object[] { new List<TaskCondition>
                                   { GetCondition("VAR2", ConditionOperator.Equals, "VALUE2"),
                                     GetCondition("vAR3", ConditionOperator.Equals, "VALue3")
                                   },
                                   true
                                 },
                    new object[] { new List<TaskCondition>
                                   { 
                                        GetCondition("var1", ConditionOperator.Equals, "value1"),
                                        GetCondition("notfound", ConditionOperator.Equals, "val"),
                                        GetCondition("var2", ConditionOperator.Equals, "value2")
                                   },
                                   false
                                 }
                };
            }
        }

        public static IEnumerable<object[]> NotEqualConditionCases
        {
            get
            {
                return new[]
                {
                    new object[] { new List<TaskCondition> { GetCondition("notfound", ConditionOperator.NotEquals, "val") }, true },
                    new object[] { new List<TaskCondition> { GetCondition("var1", ConditionOperator.NotEquals, "unequalValue") }, true },
                    new object[] { new List<TaskCondition> { GetCondition("vAR3", ConditionOperator.NotEquals, "VALue3") }, false },
                    new object[] { new List<TaskCondition>
                                   { GetCondition("VAR2", ConditionOperator.NotEquals, "unequal2"),
                                     GetCondition("vAR3", ConditionOperator.NotEquals, "unequal3")
                                   },
                                   true
                                 },
                    new object[] { new List<TaskCondition>
                                   {
                                        GetCondition("var1", ConditionOperator.NotEquals, "unequalValue1"),
                                        GetCondition("var2", ConditionOperator.NotEquals, "value2"),
                                        GetCondition("var3", ConditionOperator.NotEquals, "value3")
                                   },
                                   false
                                 }
                };
            }
        }

        public static IEnumerable<object[]> ContainsConditionCases
        {
            get
            {
                return new[]
                {
                    new object[] { new List<TaskCondition> { GetCondition("notfound", ConditionOperator.Contains, "val") }, false },
                    new object[] { new List<TaskCondition> { GetCondition("var1", ConditionOperator.Contains, "value12") }, false },
                    new object[] { new List<TaskCondition> { GetCondition("VAR2", ConditionOperator.Contains, "VAL") }, true },
                    new object[] { new List<TaskCondition> { GetCondition("VAR2", ConditionOperator.Contains, "value2") }, true },
                    new object[] { new List<TaskCondition>
                                   { GetCondition("VAR2", ConditionOperator.Contains, "VALUE2"),
                                     GetCondition("vAR3", ConditionOperator.Contains, "VAL")
                                   },
                                   true
                                 },
                    new object[] { new List<TaskCondition>
                                   {
                                        GetCondition("var1", ConditionOperator.Contains, "value1"),
                                        GetCondition("var2", ConditionOperator.Contains, "notfound"),
                                        GetCondition("var3", ConditionOperator.Contains, "value3")
                                   },
                                   false
                                 }
                };
            }
        }

        public static IEnumerable<object[]> NotContainsConditionCases
        {
            get
            {
                return new[]
                {
                    new object[] { new List<TaskCondition> { GetCondition("notfound", ConditionOperator.DoesNotContain, "val") }, true },
                    new object[] { new List<TaskCondition> { GetCondition("var1", ConditionOperator.DoesNotContain, "value12") }, true },
                    new object[] { new List<TaskCondition> { GetCondition("VAR2", ConditionOperator.DoesNotContain, "val") }, false },
                    new object[] { new List<TaskCondition> { GetCondition("VAR3", ConditionOperator.DoesNotContain, "notfound") }, true },
                    new object[] { new List<TaskCondition>
                                   { GetCondition("VAR2", ConditionOperator.DoesNotContain, "unequal2"),
                                     GetCondition("vAR3", ConditionOperator.DoesNotContain, "unequal3")
                                   },
                                   true
                                 },
                    new object[] { new List<TaskCondition>
                                   {
                                        GetCondition("var1", ConditionOperator.DoesNotContain, "notfound"),
                                        GetCondition("var2", ConditionOperator.DoesNotContain, "val"),
                                        GetCondition("var3", ConditionOperator.DoesNotContain, "none")
                                   },
                                   false
                                 }
                };
            }
        }

        public static IEnumerable<object[]> CompoundConditionCases
        {
            get
            {
                return new[]
                {
                    new object[] { new List<TaskCondition>
                                   {
                                        GetCondition("var1", ConditionOperator.Equals, "value1"),
                                        GetCondition("var2", ConditionOperator.NotEquals, "unequal1"),
                                        GetCondition("var3", ConditionOperator.Contains, "val"),
                                        GetCondition("var1", ConditionOperator.DoesNotContain, "unequal2")
                                   },
                                   true
                                 },
                    new object[] { new List<TaskCondition>
                                   {
                                        GetCondition("var1", ConditionOperator.Equals, "value1"),
                                        GetCondition("var2", ConditionOperator.NotEquals, "unequal1"),
                                        GetCondition("var3", ConditionOperator.Contains, "val"),
                                        GetCondition("var1", ConditionOperator.DoesNotContain, "val")
                                   },
                                   false
                                 }
                };
            }
        }

        [Theory]
        [MemberData(nameof(EqualConditionCases))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskConditionEqualsWorksCorrectly(List<TaskCondition> conditions, bool expected)
        {
            using (this.CreateTestContext())
            {
                bool actual = TaskConditionsEvaluator.AreConditionsSatisfied(
                    conditions,
                    _variables,
                    _executionContext.Object);

                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [MemberData(nameof(NotEqualConditionCases))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskConditionNotEqualsWorksCorrectly(List<TaskCondition> conditions, bool expected)
        {
            using (this.CreateTestContext())
            {
                bool actual = TaskConditionsEvaluator.AreConditionsSatisfied(
                    conditions,
                    _variables,
                    _executionContext.Object);

                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [MemberData(nameof(ContainsConditionCases))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskConditionContainsWorksCorrectly(List<TaskCondition> conditions, bool expected)
        {
            using (this.CreateTestContext())
            {
                bool actual = TaskConditionsEvaluator.AreConditionsSatisfied(
                    conditions,
                    _variables,
                    _executionContext.Object);

                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [MemberData(nameof(ContainsConditionCases))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskConditionDoesNotContainWorksCorrectly(List<TaskCondition> conditions, bool expected)
        {
            using (this.CreateTestContext())
            {
                bool actual = TaskConditionsEvaluator.AreConditionsSatisfied(
                    conditions,
                    _variables,
                    _executionContext.Object);

                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [MemberData(nameof(CompoundConditionCases))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskConditionsWorkCorrectlyInConjunction(List<TaskCondition> conditions, bool expected)
        {
            using (this.CreateTestContext())
            {
                bool actual = TaskConditionsEvaluator.AreConditionsSatisfied(
                    conditions,
                    _variables,
                    _executionContext.Object);

                Assert.Equal(expected, actual);
            }
        }

        private static TaskCondition GetCondition(string variable, ConditionOperator @operator = ConditionOperator.Equals, string value = "")
        {
            return new TaskCondition { VariableName = variable, Operator = @operator, Value = value};
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            var hc = new TestHostContext(this, testName);
            List<string> warnings;

            var copy = new Dictionary<string, string>
                           {
                               { "var1", "value1" },
                               { "VAR2", "VALUE2" },
                               { "Var3", "Value3" }
                           };
            _variables = new Variables(
                hostContext: hc,
                copy: copy,
                maskHints: new List<MaskHint>(),
                warnings: out warnings);

            _executionContext = new Mock<IExecutionContext>();
            _executionContext.SetupAllProperties();
            return hc;
        }
    }
}
