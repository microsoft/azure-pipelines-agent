using System;
using System.Collections.Generic;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.TextTemplating
{
    /// <summary>
    /// Utility class for working with mustache-style templates
    /// </summary>
    public class MustacheTemplateParser
    {
        private static Dictionary<String, MustacheTemplateHelperMethod> s_defaultHandlebarHelpers = HandleBarBuiltinHelpers.GetHelpers();
        private static Dictionary<String, MustacheTemplateHelperMethod> s_commonHelpers = CommonMustacheHelpers.GetHelpers();

        private Dictionary<String, MustacheTemplateHelperMethod> m_helpers;
        private Dictionary<String, MustacheRootExpression> m_partials;

        // /// <summary>
        // /// Template helpers to use when evaluating expressions
        // /// </summary>
        // [Obsolete("Use the RegisterHelper method")]
        // public Dictionary<String, MustacheTemplateHelper> Helpers { get; private set; }

        // /// <summary>
        // /// Template block helpers to use when evaluating expressions
        // /// </summary>
        // [Obsolete("Use the RegisterHelper method")]
        // public Dictionary<String, MustacheTemplateHelper> BlockHelpers { get; private set; }

        // /// <summary
        // /// Externally defined partial templates
        // /// </summary>
        // [Obsolete("Use the RegisterPartial method")]
        // public Dictionary<String, MustacheRootExpression> Partials { get; private set; }

        /// <summary>
        /// Create a helper for parsing mustache templates
        /// </summary>
        /// <param name="useDefaultHandlebarHelpers"></param>
        public MustacheTemplateParser(
            bool useDefaultHandlebarHelpers = true,
            Dictionary<String, String> partials = null)
            : this(useDefaultHandlebarHelpers, true)
        {
            if (partials != null)
            {
                foreach (KeyValuePair<String, String> partial in partials)
                {
                    m_partials[partial.Key] = (MustacheRootExpression)Parse(partial.Value);
                }
            }
        }

        /// <summary>
        /// Create a helper for parsing mustache templates
        /// </summary>
        /// <param name="useDefaultHandlebarHelpers">Register handlebar helpers (with, if, else, etc.)</param>
        /// <param name="useCommonHandlebarHelpers">Register common template helpers (equals, notequals, etc.)</param>
        public MustacheTemplateParser(
            bool useDefaultHandlebarHelpers,
            bool useCommonTemplateHelpers)
        {
            if (useDefaultHandlebarHelpers)
            {
                m_helpers = new Dictionary<string, MustacheTemplateHelperMethod>(s_defaultHandlebarHelpers, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                m_helpers = new Dictionary<string, MustacheTemplateHelperMethod>(StringComparer.OrdinalIgnoreCase);
            }

            if (useCommonTemplateHelpers)
            {
                foreach (KeyValuePair<String, MustacheTemplateHelperMethod> helper in s_commonHelpers)
                {
                    m_helpers[helper.Key] = helper.Value;
                }
            }

            m_partials = new Dictionary<String, MustacheRootExpression>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Register the helper with the specified name
        /// </summary>
        /// <param name="helperName"></param>
        /// <param name="helper"></param>
        public void RegisterHelper(String helperName, MustacheTemplateHelperMethod helper)
        {
            m_helpers[helperName] = helper;
        }

        /// <summary>
        /// Register a new partial template in string form with the template parser
        /// Overwrites an existing partial with the same name
        /// </summary>
        /// <param name="partialName"></param>
        /// <param name="partialExpression"></param>
        public void ParseAndRegisterPartial(String partialName, String partialExpression)
        {
            m_partials[partialName] = (MustacheRootExpression)Parse(partialExpression);
        }

        /// <summary>
        /// Register a new partial template in mustache-expression-tree form with the template parser
        /// Overwrites an existing partial with the same name
        /// </summary>
        /// <param name="partialName"></param>
        /// <param name="partialExpression"></param>
        public void RegisterPartial(String partialName, MustacheRootExpression partialExpression)
        {
            m_partials[partialName] = partialExpression;
        }

        /// <summary>
        /// Repace values in a mustache-style template with values from the given property bag.
        /// </summary>
        /// <param name="template">mustache-style template</param>
        /// <param name="replacementContext">properties to use as replacements</param>
        /// <returns></returns>
        public String ReplaceValues(String template, Object replacementContext)
        {
            MustacheRootExpression expression = MustacheExpression.Parse(template, m_helpers, m_partials);
            return expression.Evaluate(replacementContext, null);
        }

        /// <summary>
        /// Parse the given mustache template, resulting in a "compiled" expression that can
        /// be evaluated with a replacement context
        /// </summary>
        /// <param name="template">mustache-style template</param>
        /// <returns></returns>
        public MustacheExpression Parse(String template)
        {
            return MustacheExpression.Parse(template, m_helpers, m_partials);
        }
    }
}
