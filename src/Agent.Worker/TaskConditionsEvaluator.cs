using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public static class TaskConditionsEvaluator
    {
        public static bool AreConditionsSatisfied(IList<TaskCondition> conditions, Variables variables, IExecutionContext executionContext)
        {
            foreach (TaskCondition condition in conditions)
            {
                string actualValue;
                bool variableFound = variables.TryGetValue(condition.VariableName, out actualValue);                
                string expectedValue = condition.Value ?? string.Empty;

                bool conditionSatisfied = true;
                switch (condition.Operator)
                {
                    case ConditionOperator.Equals:
                        conditionSatisfied = variableFound && string.Equals(expectedValue, actualValue, StringComparison.OrdinalIgnoreCase);
                        break;
                    case ConditionOperator.NotEquals:
                        conditionSatisfied = !variableFound || !string.Equals(expectedValue, actualValue, StringComparison.OrdinalIgnoreCase);
                        break;
                    case ConditionOperator.Contains:
                        conditionSatisfied = variableFound && actualValue.IndexOf(expectedValue, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case ConditionOperator.DoesNotContain:
                        conditionSatisfied = !variableFound || actualValue.IndexOf(expectedValue, StringComparison.OrdinalIgnoreCase) < 0;
                        break;
                }

                if (!conditionSatisfied)
                {
                    executionContext.Output(StringUtil.Loc("ConditionEvaluatedToFalse", condition));
                    executionContext.Output(
                        !variableFound
                            ? StringUtil.Loc("UndefinedVariable", condition.VariableName)
                            : StringUtil.Loc("VariableValue", condition.VariableName, actualValue));

                    return false;
                }
            }

            return true;
        }
    }
}
