using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.Services.Common.Internal;
using Newtonsoft.Json.Linq;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.TextTemplating
{
    /// <summary>
    /// Static helper class for handlebar default/builtin template helpers
    /// </summary>
    internal static class HandleBarBuiltinHelpers
    {
        internal static Dictionary<String, MustacheTemplateHelperMethod> GetHelpers()
        {
            return new Dictionary<string, MustacheTemplateHelperMethod>(StringComparer.OrdinalIgnoreCase)
            {
                { ">", HandlebarPartialHelper },
                { "with", HandlebarBlockWithHelper },
                { "if", HandlebarBlockIfHelper },
                { "else", HandlebarBlockUnlessHelper },
                { "unless", HandlebarBlockUnlessHelper },
                { "each", HandlebarBlockEachHelper },
                { "lookup", HandlebarBlockLookupHelper }
            };
        }

        /// <summary>
        /// {{#with ...}} block helper sets context for the child expressions
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal static String HandlebarBlockWithHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            JToken replacementObject = expression.GetCurrentJToken(expression.Expression, context);
            if (!expression.IsTokenTruthy(replacementObject))
            {
                return String.Empty;
            }
            else
            {
                MustacheEvaluationContext newContext = new MustacheEvaluationContext()
                {
                    ParentContext = context,
                    ReplacementObject = replacementObject,
                    PartialExpressions = context.PartialExpressions,
                    AdditionalEvaluationData = context.AdditionalEvaluationData
                };
                MustacheEvaluationContext.CombinePartialsDictionaries(context, expression);
                return expression.EvaluateChildExpressions(newContext);
            }
        }

        /// <summary>
        /// {{#if ...}} block helper evaluates child expressions ONLY if the selected value is true
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal static String HandlebarBlockIfHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            JToken replacementObject = expression.GetCurrentJToken(expression.Expression, context);
            if (!expression.IsTokenTruthy(replacementObject))
            {
                return expression.IsBlockExpression ? String.Empty : "false";
            }
            else
            {
                return expression.IsBlockExpression ? expression.EvaluateChildExpressions(context) : "true";
            }
        }

        /// <summary>
        /// {{#unless ...}} block helper evaluates child expressions ONLY if the selected value is false
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal static String HandlebarBlockUnlessHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            JToken replacementObject = expression.GetCurrentJToken(expression.Expression, context);
            if (expression.IsTokenTruthy(replacementObject))
            {
                return expression.IsBlockExpression ? String.Empty : "false";
            }
            else
            {
                return expression.IsBlockExpression ? expression.EvaluateChildExpressions(context) : "true";
            }
        }

        /// <summary>
        /// {{#each ...}} block helper evaluates child expressions once for every item in an array or object
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal static String HandlebarBlockEachHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            JToken replacementObject = expression.GetCurrentJToken(expression.Expression, context);
            if (!expression.IsTokenTruthy(replacementObject))
            {
                return String.Empty;
            }
            else
            {
                StringBuilder sbReturnValue = new StringBuilder();

                if (replacementObject.Type == JTokenType.Array)
                {
                    return MustacheParsingUtil.EvaluateJToken(replacementObject as JArray, context, expression);
                }
                else if (replacementObject.Type == JTokenType.Object)
                {
                    // Handle object/dictionaries
                    foreach (KeyValuePair<string, JToken> kvp in (IDictionary<string, JToken>)replacementObject)
                    {
                        MustacheEvaluationContext childContext = new MustacheEvaluationContext
                        {
                            ReplacementObject = kvp.Value,
                            ParentContext = context,
                            CurrentKey = kvp.Key,
                            PartialExpressions = context.PartialExpressions,
                            AdditionalEvaluationData = context.AdditionalEvaluationData
                        };
                        MustacheEvaluationContext.CombinePartialsDictionaries(context, expression);
                        sbReturnValue.Append(expression.EvaluateChildExpressions(childContext));
                    }
                }

                return sbReturnValue.ToString();
            }
        }

        /// <summary>
        /// {{#lookup ../foo @index}} block helper allows for indexing into an object by @index or @key
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal static String HandlebarBlockLookupHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            if (!String.IsNullOrEmpty(expression.Expression))
            {
                string[] parts = expression.Expression.Split(' ');
                if (parts.Length == 2)
                {
                    String key = parts[1];
                    String selector = null;

                    if (String.Equals(key, "@index"))
                    {
                        key = context.CurrentIndex.ToString();
                        selector = String.Format("{0}[{1}]", parts[0], key);
                    }
                    else
                    {
                        if (String.Equals(key, "@key"))
                        {
                            key = context.CurrentKey;
                        }

                        if (!String.IsNullOrEmpty(key))
                        {
                            selector = String.Format("{0}.{1}", parts[0], key);
                        }
                    }

                    if (!String.IsNullOrEmpty(selector))
                    {
                        JToken replacementObject = expression.GetCurrentJToken(selector, context);
                        if (replacementObject != null && replacementObject.Type != JTokenType.Null)
                        {
                            String rawTokenString = replacementObject.ToString();
// todo: disable html-encode here?
                            if (expression.Encode)
                            {
                                rawTokenString = UriUtility.HtmlEncode(rawTokenString);
                            }
                            return rawTokenString;
                        }
                    }
                }
            }
            return String.Empty;
        }
        /// <summary>
        /// {{> foo context }} helper looks for a partial template registered as 'foo' and evaluates it against 'context'
        /// Evaluates against the current context if 'context' is not given
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal static String HandlebarPartialHelper(MustacheTemplatedExpression expression, MustacheEvaluationContext context)
        {
            String partialName = expression.GetRawHelperArgument(0);
            if (String.IsNullOrEmpty(partialName))
            {
                throw new MustacheExpressionInvalidException(WebApiResourcesTemp.MustacheTemplateInvalidPartialReference(expression.Expression));
            }

            // Dynamic partial syntax: lookup name of partial
            if (partialName[0].Equals('(') && partialName[partialName.Length - 1].Equals(')'))
            {
                JToken token = expression.GetCurrentJToken(partialName.Substring(1, partialName.Length - 2), context);
                if (token == null || !token.Type.Equals(JTokenType.String))
                {
                    return String.Empty;
                }
                partialName = token.ToString();
            }

            MustacheRootExpression parentPartial;
            context.PartialExpressions.TryGetValue(partialName, out parentPartial);

            if (parentPartial != null)
            {
                // Evaluate partial with all partials registered within scope as well as within the partial template
                MustacheEvaluationContext.CombinePartialsDictionaries(context, parentPartial);

                // Get context
                MustacheEvaluationContext replacementContext = new MustacheEvaluationContext()
                {
                    ReplacementObject = context.ReplacementObject,
                    ParentContext = context.ParentContext,
                    PartialExpressions = context.PartialExpressions,
                    AdditionalEvaluationData = context.AdditionalEvaluationData
                };

                String contextSelector = expression.GetRawHelperArgument(1);
                if (contextSelector != null)
                {
                    replacementContext.ReplacementObject = expression.GetCurrentJToken(contextSelector, context);
                    replacementContext.ParentContext = context;
                    replacementContext.PartialExpressions = context.PartialExpressions;
                }
                return parentPartial.Evaluate(replacementContext);
            }

            return String.Empty;
        }
    }
}
