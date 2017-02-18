namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.TextTemplating
{
    public static class WebApiResourcesTemp
    {
        public static string MissingCloseInlineMessage()
        {
            return "Missing close expression for inline content.";
        }

        public static string MissingEndingBracesMessage(object p1)
        {
            return string.Format("No ending braces for expression '{0}'.", p1);
        }

        public static string NestedInlinePartialsMessage()
        {
            return "An inline partial cannot contain another inline partial";
        }

        public static string MustacheTemplateBraceCountMismatch(object p1)
        {
            return string.Format("The expression '{0}' is invalid due to mismatching start and end brace count.", p1);
        }

        public static string MustacheTemplateInvalidEndBlock(object p1)
        {
            return string.Format("Unexpected end block '{0}' before any start block", p1);
        }

        public static string MustacheTemplateInvalidEndBraces(object p1, object p2)
        {
            return string.Format("Invalid end braces before start braces at position '{0}' of template '{1}'", p1, p2);
        }

        public static string MustacheTemplateInvalidEscapedStringLiteral(object p1, object p2)
        {
            return string.Format("Invalid escape character in string literal '{0}' within template expression '{1}'", p1, p2);
        }

        public static string MustacheTemplateInvalidNumericLiteral(object p1, object p2)
        {
            return string.Format("Invalid numeric literal '{0}' within template expression '{1}'", p1, p2);
        }

        public static string MustacheTemplateInvalidPartialReference(object p1)
        {
            return string.Format("Invalid partial reference: {0}", p1);
        }

        public static string MustacheTemplateInvalidStartBraces(object p1, object p2, object p3)
        {
            return string.Format("Invalid start braces within template expression '{0}' at position {1} of template '{2}'.", p1, p2, p3);
        }

        public static string MustacheTemplateMissingBlockHelper(object p1, object p2)
        {
            return string.Format("Block Helper '{0}' not found for expression '{1}'", p1, p2);
        }

        public static string MustacheTemplateMissingHelper(object p1, object p2)
        {
            return string.Format("Helper '{0}' not found for expression '{1}'", p1, p2);
        }

        public static string MustacheTemplateNonMatchingEndBlock(object p1, object p2)
        {
            return string.Format("End block '{0}' does not match start block '{1}'", p1, p2);
        }

        public static string MustacheTemplateUnterminatedStringLiteral(object p1, object p2)
        {
            return string.Format("Unterminated string literal '{0}' within template expression '{1}'", p1, p2);
        }
    }
}
