using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed partial class Condition
    {
        private void CreateTree()
        {
            _trace.Entering();
            int level = 0;
            ContainerNode container = null;
            Token token = null;
            Token lastToken = null;
            while ((token = GetNextToken()) != null)
            {
                Node newNode = null;
                switch (token.Kind)
                {
                    case TokenKind.Unrecognized:
                        ThrowParseException("Unrecognized value", token);
                        break;

                    // Punctuation
                    case TokenKind.OpenFunction:
                    case TokenKind.OpenHashtable:
                        // Required opening punctuation is validated and skipped when a function or hashtable
                        // is encountered. Any opening punctuation found at this point is an error.
                        ThrowParseException("Unexpected symbol", token);
                        break;
                    case TokenKind.CloseFunction:
                        ValidateCloseFunction(container, token, lastToken);
                        container = container.Container; // Pop container.
                        level--;
                        break;
                    case TokenKind.CloseHashtable:
                        ValidateCloseHashtable(container, token, lastToken);
                        container = container.Container; // Pop container.
                        level--;
                        break;
                    case TokenKind.Separator:
                        ValidateSeparator(container, token, lastToken);
                        break;

                    // Functions
                    case TokenKind.And:
                    case TokenKind.Equal:
                    case TokenKind.GreaterThan:
                    case TokenKind.GreaterThanOrEqual:
                    case TokenKind.LessThan:
                    case TokenKind.LessThanOrEqual:
                    case TokenKind.Not:
                    case TokenKind.NotEqual:
                    case TokenKind.Or:
                    case TokenKind.Xor:
                        newNode = CreateFunction(token);

                        // Get next token and validate is opening punctuation.
                        lastToken = token;
                        token = GetNextToken();
                        if (token == null || token.Kind != TokenKind.OpenFunction)
                        {
                            ThrowParseException("Unexpected symbol", token);
                        }

                        break;

                    // Hashtables
                    case TokenKind.Capabilities:
                    case TokenKind.Variables:
                        newNode = CreateHashtable(token);

                        // Get next token and validate is opening punctuation.
                        lastToken = token;
                        token = GetNextToken();
                        if (token == null || token.Kind != TokenKind.OpenHashtable)
                        {
                            ThrowParseException("Unexpected symbol", token);
                        }

                        break;

                    // Literal values
                    case TokenKind.False:
                    case TokenKind.True:
                    case TokenKind.Number:
                    case TokenKind.Version:
                    case TokenKind.String:
                        ValidateLiteral(container, token, lastToken);
                        newNode = CreateLiteral(token);
                        break;
                }

                // Update the tree.
                if (_root == null)
                {
                    _root = newNode;
                }
                else
                {
                    container.AddParameter(newNode);
                }

                // Push the container node.
                if (newNode is ContainerNode)
                {
                    container = newNode as ContainerNode;
                    level++;
                }
            }
        }

        private void ValidateCloseFunction(ContainerNode container, Token token, Token lastToken)
        {
            var function = container as FunctionNode;
            if (function == null ||                                     // Container should be a function
                function.Parameters.Count < function.MinParameters ||   // Above min parameters threshold
                lastToken.Kind == TokenKind.Separator)                  // Last token should not be a separator
            {
                ThrowParseException("Unexpected symbol", token);
            }
        }

        private void ValidateCloseHashtable(ContainerNode container, Token token, Token lastToken)
        {
            var hashtable = container as HashtableNode;
            if (hashtable == null ||                // Container should be a hashtable
                hashtable.Parameters.Count != 1)    // With exactly 1 parameter
            {
                ThrowParseException("Unexpected symbol", token);
            }
        }

        private void ValidateLiteral(ContainerNode container, Token token, Token lastToken)
        {
            bool expected = false;
            if (lastToken == null) // The first token.
            {
                expected = true;
            }
            else if (container != null) // Inside a container
            {
                if (lastToken.Kind == TokenKind.OpenFunction ||     // Preceeded by opening punctuation
                    lastToken.Kind == TokenKind.OpenHashtable ||    // or by a separator.
                    lastToken.Kind == TokenKind.Separator)
                {
                    expected = true;
                }
            }

            if (!expected)
            {
                ThrowParseException("Unexpected symbol", token);
            }
        }

        private void ValidateSeparator(ContainerNode container, Token token, Token lastToken)
        {
            var function = container as FunctionNode;
            if (function == null ||                                     // Container should be a function
                function.Parameters.Count < 1 ||                        // With at least 1 parameter
                function.Parameters.Count >= function.MaxParameters ||  // Under max parameters threshold
                lastToken.Kind == TokenKind.Separator)                  // Last token should not be a separator
            {
                ThrowParseException("Unexpected symbol", token);
            }
        }

        // // private void CreateTree_old()
        // // {
        // //     _trace.Entering();
        // //     int level = 0;
        // //     ContainerNode container = null;
        // //     for (int tokenIndex = 0; tokenIndex < _tokens.Count; tokenIndex++)
        // //     {
        // //         Token token = _tokens[tokenIndex];
        // //         ThrowIfInvalid(token);

        // //         // Check if punctuation.
        // //         var punctuation = token as PunctuationToken;
        // //         if (punctuation != null)
        // //         {
        // //             ValidatePunctuation(container, punctuation, tokenIndex);
        // //             if (punctuation.Value == Constants.Conditions.CloseFunction ||
        // //                 punctuation.Value == Constants.Conditions.CloseHashtable)
        // //             {
        // //                 container = container.Container; // Pop container.
        // //                 level--;
        // //             }

        // //             continue;
        // //         }

        // //         // Validate the token and create the node.
        // //         Node newNode = null;
        // //         if (token is LiteralToken)
        // //         {
        // //             var literalToken = token as LiteralToken;
        // //             ValidateLiteral_old(literalToken, tokenIndex);
        // //             string traceFormat = literalToken is StringToken ? "'{0}' ({1})" : "{0} ({1})";
        // //             _trace.Verbose(string.Empty.PadLeft(level * 2) + traceFormat, literalToken.Value, literalToken.Value.GetType().Name);
        // //             newNode = new LiteralNode(literalToken, _trace, level);
        // //         }
        // //         else if (token is FunctionToken)
        // //         {
        // //             var functionToken = token as FunctionToken;
        // //             ValidateFunction_old(functionToken, tokenIndex);
        // //             tokenIndex++; // Skip the open paren that follows.
        // //             _trace.Verbose(string.Empty.PadLeft(level * 2) + $"{functionToken.Name} (Function)");
        // //             newNode = CreateFunction(functionToken, level);
        // //         }
        // //         else if (token is HashtableToken)
        // //         {
        // //             var hashtableToken = token as HashtableToken;
        // //             ValidateHashtable(hashtableToken, tokenIndex);
        // //             tokenIndex++; // Skip the open bracket that follows.
        // //             _trace.Verbose(string.Empty.PadLeft(level * 2) + $"{hashtableToken.Name} (Hashtable)");
        // //             newNode = CreateHashtable(hashtableToken, level);
        // //         }
        // //         else
        // //         {
        // //             throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
        // //         }

        // //         // Update the tree.
        // //         if (_root == null)
        // //         {
        // //             _root = newNode;
        // //         }
        // //         else
        // //         {
        // //             container.AddParameter(newNode);
        // //         }

        // //         // Push the container node.
        // //         if (newNode is ContainerNode)
        // //         {
        // //             container = newNode as ContainerNode;
        // //             level++;
        // //         }
        // //     }
        // // }

        // // private void ThrowIfInvalid(Token token)
        // // {
        // //     ArgUtil.NotNull(token, nameof(token));
        // //     if (token is InvalidToken)
        // //     {
        // //         if (token is MalformedNumberToken)
        // //         {
        // //             ThrowParseException("Unable to parse number", token);
        // //         }
        // //         else if (token is UnterminatedStringToken)
        // //         {
        // //             ThrowParseException("Unterminated string", token);
        // //         }
        // //         else if (token is UnrecognizedToken)
        // //         {
        // //             ThrowParseException("Unrecognized keyword", token);
        // //         }

        // //         throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
        // //     }
        // // }

        // // private void ValidateLiteral_old(LiteralToken token, int tokenIndex)
        // // {
        // //     ArgUtil.NotNull(token, nameof(token));

        // //     // Validate nothing follows, a separator follows, or close punction follows.
        // //     Token nextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 1] : null;
        // //     ValidateNullOrSeparatorOrClosePunctuation(nextToken);
        // // }

        // // private void ValidateHashtable(HashtableToken token, int tokenIndex)
        // // {
        // //     ArgUtil.NotNull(token, nameof(token));

        // //     // Validate open bracket follows.
        // //     PunctuationToken nextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 1] as PunctuationToken : null;
        // //     if (nextToken == null || nextToken.Value != Constants.Conditions.OpenHashtable)
        // //     {
        // //         ThrowParseException($"Expected '{Constants.Conditions.OpenHashtable}' to follow symbol", token);
        // //     }

        // //     // Validate a literal, hashtable, or function follows.
        // //     Token nextNextToken = tokenIndex + 2 < _tokens.Count ? _tokens[tokenIndex + 2] : null;
        // //     if (nextNextToken as LiteralToken == null && nextNextToken as HashtableToken == null && nextNextToken as FunctionToken == null)
        // //     {
        // //         ThrowParseException("Expected a value to follow symbol", nextToken);
        // //     }
        // // }

        // // private void ValidateFunction_old(FunctionToken token, int tokenIndex)
        // // {
        // //     ArgUtil.NotNull(token, nameof(token));

        // //     // Valdiate open paren follows.
        // //     PunctuationToken nextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 1] as PunctuationToken : null;
        // //     if (nextToken == null || nextToken.Value != Constants.Conditions.OpenFunction)
        // //     {
        // //         ThrowParseException($"Expected '{Constants.Conditions.OpenFunction}' to follow symbol", token);
        // //     }

        // //     // Validate a literal, hashtable, or function follows.
        // //     Token nextNextToken = tokenIndex + 2 < _tokens.Count ? _tokens[tokenIndex + 2] : null;
        // //     if (nextNextToken as LiteralToken == null && nextNextToken as HashtableToken == null && nextNextToken as FunctionToken == null)
        // //     {
        // //         ThrowParseException("Expected a value to follow symbol", nextToken);
        // //     }
        // // }

        // // private void ValidatePunctuation(ContainerNode container, PunctuationToken token, int tokenIndex)
        // // {
        // //     ArgUtil.NotNull(token, nameof(token));

        // //     // Required open brackets and parens are validated and skipped when a hashtable
        // //     // or function node is created. Any open bracket or paren tokens found at this
        // //     // point are errors.
        // //     if (token.Value == Constants.Conditions.OpenFunction ||
        // //         token.Value == Constants.Conditions.OpenHashtable)
        // //     {
        // //         ThrowParseException("Unexpected symbol", token);
        // //     }

        // //     if (container == null)
        // //     {
        // //         // A condition cannot lead with punction.
        // //         // And punction should not trail the closing of the root node.
        // //         ThrowParseException("Unexpected symbol", token);
        // //     }

        // //     if (token.Value == Constants.Conditions.Separator)
        // //     {
        // //         // Validate current container is a function under max parameters threshold.
        // //         var function = container as FunctionNode;
        // //         if (function == null ||
        // //             function.Parameters.Count >= function.MaxParameters)
        // //         {
        // //             ThrowParseException("Unexpected symbol", token);
        // //         }

        // //         // Validate a literal, function, or hashtable follows.
        // //         Token nextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 1] : null;
        // //         if (nextToken == null ||
        // //             (!(nextToken is LiteralToken) && !(nextToken is FunctionToken) && !(nextToken is HashtableToken)))
        // //         {
        // //             ThrowParseException("Expected a value to follow the separator symbol", token);
        // //         }
        // //     }
        // //     else if (token.Value == Constants.Conditions.CloseHashtable)
        // //     {
        // //         // Validate nothing follows, a separator follows, or close punction follows.
        // //         Token nextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 1] : null;
        // //         ValidateNullOrSeparatorOrClosePunctuation(nextToken);
        // //     }
        // //     else if (token.Value == Constants.Conditions.CloseFunction)
        // //     {
        // //         // Validate current container is a function above min parameters threshold.
        // //         var function = container as FunctionNode;
        // //         if (function == null ||
        // //             function.Parameters.Count < function.MinParameters)
        // //         {
        // //             ThrowParseException("Unexpected symbol", token);
        // //         }

        // //         // Validate nothing follows, a separator follows, or close punction follows.
        // //         Token nextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 1] : null;
        // //         ValidateNullOrSeparatorOrClosePunctuation(nextToken);
        // //     }
        // // }

        // // private void ValidateNullOrSeparatorOrClosePunctuation(Token token)
        // // {
        // //     if (token == null)
        // //     {
        // //         return;
        // //     }

        // //     var punctuation = token as PunctuationToken;
        // //     if (punctuation != null)
        // //     {
        // //         switch (punctuation.Value)
        // //         {
        // //             case Constants.Conditions.CloseFunction:
        // //             case Constants.Conditions.CloseHashtable:
        // //             case Constants.Conditions.Separator:
        // //                 return;
        // //         }
        // //     }

        // //     ThrowParseException("Unexpected symbol", token);
        // // }

        private void ThrowParseException(string description, Token token)
        {
            string rawToken = _raw.Substring(token.Index, token.Length);
            int position = token.Index + 1;
            // TODO: loc
            throw new ParseException($"{description}: '{rawToken}'. Located at position {position} within condition expression: {_raw}");
        }

        private FunctionNode CreateFunction(FunctionToken token, int level)
        {
            ArgUtil.NotNull(token, nameof(token));
            switch (token.Name)
            {
                case Constants.Conditions.And:
                    return new AndFunction(token, _trace, level);
                case Constants.Conditions.Equal:
                    return new EqualFunction(token, _trace, level);
                case Constants.Conditions.GreaterThan:
                    return new GreaterThanFunction(token, _trace, level);
                case Constants.Conditions.GreaterThanOrEqual:
                    return new GreaterThanOrEqualFunction(token, _trace, level);
                case Constants.Conditions.LessThan:
                    return new LessThanFunction(token, _trace, level);
                case Constants.Conditions.LessThanOrEqual:
                    return new LessThanOrEqualFunction(token, _trace, level);
                case Constants.Conditions.Not:
                    return new NotFunction(token, _trace, level);
                case Constants.Conditions.NotEqual:
                    return new NotEqualFunction(token, _trace, level);
                case Constants.Conditions.Or:
                    return new OrFunction(token, _trace, level);
                case Constants.Conditions.Xor:
                    return new XorFunction(token, _trace, level);
                default:
                    throw new NotSupportedException($"Unexpected function token name: '{token.Name}'");
            }
        }

        private HashtableNode CreateHashtable(HashtableToken token, int level)
        {
            ArgUtil.NotNull(token, nameof(token));
            switch (token.Name)
            {
                case Constants.Conditions.Capabilities:
                case Constants.Conditions.Variables:
                    throw new NotImplementedException();
                default:
                    throw new NotSupportedException($"Unexpected hashtable token name: '{token.Name}'");
            }
        }

        private sealed class ParseException : Exception
        {
            public ParseException(string message)
                : base(message)
            {
            }
        }

        private abstract class Node
        {
            private static readonly NumberStyles NumberStyles =
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowThousands |
                NumberStyles.AllowTrailingWhite;
            private readonly Tracing _trace;
            private readonly int _level;

            public Node(Token token, Tracing trace, int level)
            {
                Token = token;
                _trace = trace;
                _level = level;
            }

            public ContainerNode Container { get; set; }

            public Token Token { get; }

            public abstract object GetValue();

            public bool GetValueAsBool()
            {
                object val = GetValue();
                bool result;
                if (val is bool)
                {
                    result = (bool)val;
                }
                else if (val is decimal)
                {
                    result = (decimal)val != 0m; // 0 converts to false, otherwise true.
                    TraceValue(result);
                }
                else
                {
                    result = !string.IsNullOrEmpty(val as string);
                    TraceValue(result);
                }

                return result;
            }

            public decimal GetValueAsNumber()
            {
                object val = GetValue();
                if (val is decimal)
                {
                    return (decimal)val;
                }

                decimal d;
                if (TryConvertToNumber(val, out d))
                {
                    TraceValue(d);
                    return d;
                }

                try
                {
                    return decimal.Parse(
                        val as string ?? string.Empty,
                        NumberStyles,
                        CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    // TODO: loc
                    throw new Exception($"Unable to convert value '{val}' to a number. {ex.Message}");
                }
            }

            public string GetValueAsString()
            {
                string result;
                object val = GetValue();
                if (object.ReferenceEquals(val, null) || val is string)
                {
                    result = val as string;
                }
                else if (val is bool)
                {
                    result = string.Format(CultureInfo.InvariantCulture, "{0}", val);
                    TraceValue(result);
                }
                else
                {
                    decimal d = (decimal)val;
                    result = d.ToString("G", CultureInfo.InvariantCulture);
                    if (result.Contains("."))
                    {
                        result = result.TrimEnd('0').TrimEnd('.'); // Omit trailing zeros after the decimal point.
                    }

                    TraceValue(result);
                }

                return result;
            }

            public bool TryGetValueAsNumber(out decimal result)
            {
                object val = GetValue();
                if (val is decimal)
                {
                    result = (decimal)val;
                    return true;
                }

                if (TryConvertToNumber(val, out result))
                {
                    TraceValue(result);
                    return true;
                }

                TraceValue(val: null, isUnconverted: false, isNotANumber: true);
                return false;
            }

            protected void TraceInfo(string message)
            {
                _trace.Info(string.Empty.PadLeft(_level * 2) + (message ?? string.Empty));
            }

            protected void TraceValue(object val, bool isUnconverted = false, bool isNotANumber = false)
            {
                string prefix = isUnconverted ? string.Empty : "=> ";
                if (isNotANumber)
                {
                    TraceInfo(StringUtil.Format("{0}NaN", prefix));
                }
                else if (val is bool || val is decimal)
                {
                    TraceInfo(StringUtil.Format("{0}{1} ({2})", prefix, val, val.GetType().Name));
                }
                else
                {
                    TraceInfo(StringUtil.Format("{0}{1} (String)", prefix, val));
                }
            }

            private bool TryConvertToNumber(object val, out decimal result)
            {
                if (val is bool)
                {
                    result = (bool)val ? 1m : 0m;
                    return true;
                }
                else if (val is decimal)
                {
                    result = (decimal)val;
                    return true;
                }

                string s = val as string ?? string.Empty;
                if (string.IsNullOrEmpty(s))
                {
                    result = 0m;
                    return true;
                }

                return decimal.TryParse(
                    s,
                    NumberStyles,
                    CultureInfo.InvariantCulture,
                    out result);
            }
        }

        private abstract class ContainerNode : Node
        {
            public ContainerNode(Token token, Tracing trace, int level)
                : base(token, trace, level)
            {
            }

            private readonly List<Node> _parameters = new List<Node>();

            public IReadOnlyList<Node> Parameters => _parameters.AsReadOnly();

            public void AddParameter(Node node)
            {
                _parameters.Add(node);
                node.Container = this;
            }
        }

        private sealed class LiteralNode : Node
        {
            public LiteralNode(LiteralToken token, Tracing trace, int level)
                : base(token, trace, level)
            {
                ArgUtil.NotNull(token, nameof(token));
            }

            public sealed override object GetValue()
            {
                object result = (Token as LiteralToken).Value;
                TraceValue(result, isUnconverted: true);
                return result;
            }
        }

        private abstract class HashtableNode : ContainerNode
        {
            public HashtableNode(HashtableToken token, Tracing trace, int level)
                : base(token, trace, level)
            {
                ArgUtil.NotNull(token, nameof(token));
            }
        }

        private abstract class FunctionNode : ContainerNode
        {
            public FunctionNode(FunctionToken token, Tracing trace, int level, int minParameters, int maxParameters)
                : base(token, trace, level)
            {
                ArgUtil.NotNull(token, nameof(token));
                if (minParameters < 1)
                {
                    throw new Exception($"Parameter {nameof(minParameters)} must be greater or equal to 1");
                }

                if (maxParameters < minParameters)
                {
                    throw new Exception($"Parameter {nameof(maxParameters)} must be greater or equal to {nameof(minParameters)}");
                }

                MinParameters = minParameters;
                MaxParameters = maxParameters;
            }

            public int MinParameters { get; }
            
            public int MaxParameters { get; }

            protected void TraceName()
            {
                TraceInfo((Token as FunctionToken).Name + " (Function)");
            }
        }

        private sealed class AndFunction : FunctionNode
        {
            public AndFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 2, maxParameters: int.MaxValue)
            {
            }

            public sealed override object GetValue()
            {
                TraceName();
                bool result = true;
                foreach (Node parameter in Parameters)
                {
                    if (!parameter.GetValueAsBool())
                    {
                        result = false;
                        break;
                    }
                }

                TraceValue(result);
                return result;
            }
        }

        private class EqualFunction : FunctionNode
        {
            public EqualFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 2, maxParameters: 2)
            {
            }

            public override object GetValue()
            {
                TraceName();
                bool result;
                object left = Parameters[0].GetValue();
                if (left is bool)
                {
                    bool right = Parameters[1].GetValueAsBool();
                    result = (bool)left == right;
                }
                else if (left is decimal)
                {
                    decimal right;
                    if (Parameters[1].TryGetValueAsNumber(out right))
                    {
                        result = (decimal)left == right;
                    }
                    else
                    {
                        result = false;
                    }
                }
                else
                {
                    string right = Parameters[1].GetValueAsString();
                    result = string.Equals(
                        left as string ?? string.Empty,
                        right ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
                }

                TraceValue(result);
                return result;
            }
        }

        private class GreaterThanFunction : FunctionNode
        {
            public GreaterThanFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 2, maxParameters: 2)
            {
            }

            public override object GetValue()
            {
                TraceName();
                bool result;
                object left = Parameters[0].GetValue();
                if (left is bool)
                {
                    bool right = Parameters[1].GetValueAsBool();
                    result = ((bool)left).CompareTo(right) >= 1;
                }
                else if (left is decimal)
                {
                    decimal right = Parameters[1].GetValueAsNumber();
                    result = ((decimal)left).CompareTo(right) >= 1;
                }
                else
                {
                    string upperLeft = (left as string ?? string.Empty).ToUpperInvariant();
                    string upperRight = (Parameters[1].GetValueAsString() ?? string.Empty).ToUpperInvariant();
                    result = upperLeft.CompareTo(upperRight) >= 1;
                }

                TraceValue(result);
                return result;
            }
        }

        private class GreaterThanOrEqualFunction : FunctionNode
        {
            public GreaterThanOrEqualFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 2, maxParameters: 2)
            {
            }

            public override object GetValue()
            {
                TraceName();
                bool result;
                object left = Parameters[0].GetValue();
                if (left is bool)
                {
                    bool right = Parameters[1].GetValueAsBool();
                    result = ((bool)left).CompareTo(right) >= 0;
                }
                else if (left is decimal)
                {
                    decimal right = Parameters[1].GetValueAsNumber();
                    result = ((decimal)left).CompareTo(right) >= 0;
                }
                else
                {
                    string upperLeft = (left as string ?? string.Empty).ToUpperInvariant();
                    string upperRight = (Parameters[1].GetValueAsString() ?? string.Empty).ToUpperInvariant();
                    result = upperLeft.CompareTo(upperRight) >= 0;
                }

                TraceValue(result);
                return result;
            }
        }

        private sealed class LessThanFunction : GreaterThanOrEqualFunction
        {
            public LessThanFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level)
            {
            }

            public sealed override object GetValue()
            {
                bool result = !(bool)base.GetValue();
                TraceValue(result);
                return result;
            }
        }

        private sealed class LessThanOrEqualFunction : GreaterThanFunction
        {
            public LessThanOrEqualFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level)
            {
            }

            public sealed override object GetValue()
            {
                bool result = !(bool)base.GetValue();
                TraceValue(result);
                return result;
            }
        }

        private sealed class NotEqualFunction : EqualFunction
        {
            public NotEqualFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level)
            {
            }

            public sealed override object GetValue()
            {
                bool result = !(bool)base.GetValue();
                TraceValue(result);
                return result;
            }
        }

        private sealed class NotFunction : FunctionNode
        {
            public NotFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 1, maxParameters: 1)
            {
            }

            public sealed override object GetValue()
            {
                TraceName();
                bool result = !Parameters[0].GetValueAsBool();
                TraceValue(result);
                return result;
            }
        }

        private sealed class OrFunction : FunctionNode
        {
            public OrFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 2, maxParameters: int.MaxValue)
            {
            }

            public sealed override object GetValue()
            {
                TraceName();
                bool result = false;
                foreach (Node parameter in Parameters)
                {
                    if (parameter.GetValueAsBool())
                    {
                        result = true;
                        break;
                    }
                }

                TraceValue(result);
                return result;
            }
        }

        private sealed class XorFunction : FunctionNode
        {
            public XorFunction(FunctionToken token, Tracing trace, int level)
                : base(token, trace, level, minParameters: 2, maxParameters: 2)
            {
            }

            public sealed override object GetValue()
            {
                TraceName();
                bool result = Parameters[0].GetValueAsBool() ^ Parameters[1].GetValueAsBool();
                TraceValue(result);
                return result;
            }
        }
    }
}