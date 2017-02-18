using System;
using System.Collections.Generic;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.TextTemplating
{
    /// <summary>
    /// Static helper class for common mustache template helpers
    /// </summary>
    internal static class CommonMustacheHelpers
    {
        internal static Dictionary<String, MustacheTemplateHelperMethod> GetHelpers()
        {
            return new Dictionary<string, MustacheTemplateHelperMethod>(StringComparer.OrdinalIgnoreCase)
            {
                { "equals", EqualsHelper },
                { "notEquals", NotEqualsHelper },
                { "contains", StringContainsHelper },

                // Left for compatibility
                { "stringContains", StringContainsHelper }
            };
        }

        internal static String EqualsHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            String arg1 = expression.GetHelperArgument<String>(context, 0);
            String arg2 = expression.GetHelperArgument<String>(context, 1);
            Boolean ignoreCase = expression.GetHelperArgument(context, 2, false);

            if (String.Equals(arg1, arg2, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return expression.IsBlockExpression ? expression.EvaluateChildExpressions(context) : "true";
            }
            else
            {
                return expression.IsBlockExpression ? String.Empty : "false";
            }
        }

        internal static String NotEqualsHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            String arg1 = expression.GetHelperArgument<String>(context, 0);
            String arg2 = expression.GetHelperArgument<String>(context, 1);
            Boolean ignoreCase = expression.GetHelperArgument(context, 2, false);

            if (String.Equals(arg1, arg2, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return expression.IsBlockExpression ? String.Empty : "false";
            }
            else
            {
                return expression.IsBlockExpression ? expression.EvaluateChildExpressions(context) : "true";
            }
        }

        internal static String StringContainsHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            String string1 = expression.GetHelperArgument(context, 0, String.Empty);
            String value = expression.GetHelperArgument(context, 1, String.Empty);

            if (string1 != null && value != null)
            {
                StringComparison comparer = expression.GetHelperArgument(context, 2, false) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (string1.IndexOf(value, comparer) >= 0)
                {
                    return expression.IsBlockExpression ? expression.EvaluateChildExpressions(context) : "true";
                }
            }

            return expression.IsBlockExpression ? String.Empty : "false";
        }
    }
}
