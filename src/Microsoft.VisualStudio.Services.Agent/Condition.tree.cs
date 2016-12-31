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
            ContainerNode container = null;
            for (int tokenIndex = 0; tokenIndex < _tokens.Count; tokenIndex++)
            {
                Token token = _tokens[tokenIndex];
                ThrowIfInvalid(token);

                // Check if punctuation.
                var punctuation = token as PunctuationToken;
                if (punctuation != null)
                {
                    ValidatePunctuation(container, punctuation, tokenIndex);
                    if (punctuation.Value == Constants.Conditions.CloseFunction ||
                        punctuation.Value == Constants.Conditions.CloseHashtable)
                    {
                        container = container.Container; // Pop container.
                    }

                    continue;
                }

                // Validate the token and create the node.
                Node newNode = null;
                if (token is LiteralToken)
                {
                    ValidateLiteral(token as LiteralToken, tokenIndex);
                    newNode = new LiteralNode(token as LiteralToken);
                }
                else if (token is FunctionToken)
                {
                    ValidateFunction(token as FunctionToken, tokenIndex);
                    tokenIndex++; // Skip the open paren that follows.
                    newNode = CreateFunction(token as FunctionToken);
                }
                else if (token is HashtableToken)
                {
                    ValidateHashtable(token as HashtableToken, tokenIndex);
                    tokenIndex++; // Skip the open bracket that follows.
                    newNode = CreateHashtable(token as HashtableToken);
                }
                else
                {
                    throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
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

                // Adjust current container node.
                if (newNode is ContainerNode)
                {
                    container = newNode as ContainerNode;
                }
            }
        }

        private void ThrowIfInvalid(Token token)
        {
            ArgUtil.NotNull(token, nameof(token));
            if (token is InvalidToken)
            {
                if (token is MalformedNumberToken)
                {
                    ThrowParseException("Unable to parse number", token);
                }
                else if (token is UnterminatedStringToken)
                {
                    ThrowParseException("Unterminated string", token);
                }
                else if (token is UnrecognizedToken)
                {
                    ThrowParseException("Unrecognized keyword", token);
                }

                throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
            }
        }

        private void ValidateLiteral(LiteralToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Validate nothing follows, a separator follows, or close punction follows.
            Token nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] : null;
            ValidateNullOrSeparatorOrClosePunctuation(nextToken);
        }

        private void ValidateHashtable(HashtableToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Validate open bracket follows.
            PunctuationToken nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] as PunctuationToken : null;
            if (nextToken == null || nextToken.Value != Constants.Conditions.OpenHashtable)
            {
                ThrowParseException($"Expected '{Constants.Conditions.OpenHashtable}' to follow symbol", token);
            }

            // Validate a literal, hashtable, or function follows.
            Token nextNextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 2] : null;
            if (nextNextToken as LiteralToken == null && nextNextToken as HashtableToken == null && nextNextToken as FunctionToken == null)
            {
                ThrowParseException("Expected a value to follow symbol", nextToken);
            }
        }

        private void ValidateFunction(FunctionToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Valdiate open paren follows.
            PunctuationToken nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] as PunctuationToken : null;
            if (nextToken == null || nextToken.Value != Constants.Conditions.OpenFunction)
            {
                ThrowParseException($"Expected '{Constants.Conditions.OpenFunction}' to follow symbol", token);
            }

            // Validate a literal, hashtable, or function follows.
            Token nextNextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 2] : null;
            if (nextNextToken as LiteralToken == null && nextNextToken as HashtableToken == null && nextNextToken as FunctionToken == null)
            {
                ThrowParseException("Expected a value to follow symbol", nextToken);
            }
        }

        private void ValidatePunctuation(ContainerNode container, PunctuationToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Required open brackets and parens are validated and skipped when a hashtable
            // or function node is created. Any open bracket or paren tokens found at this
            // point are errors.
            if (token.Value == Constants.Conditions.OpenFunction ||
                token.Value == Constants.Conditions.OpenHashtable)
            {
                ThrowParseException("Unexpected symbol", token);
            }

            if (container == null)
            {
                // A condition cannot lead with punction.
                // And punction should not trail the closing of the root node.
                ThrowParseException("Unexpected symbol", token);
            }

            if (token.Value == Constants.Conditions.Separator)
            {
                // Validate current container is a function under max parameters threshold.
                var function = container as FunctionNode;
                if (function == null ||
                    function.Parameters.Count >= function.MaxParameters)
                {
                    ThrowParseException("Unexpected symbol", token);
                }

                // Validate a literal, function, or hashtable follows.
                Token nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] : null;
                if (nextToken == null ||
                    (!(nextToken is LiteralToken) && !(nextToken is FunctionToken) && !(nextToken is HashtableToken)))
                {
                    ThrowParseException("Expected another value to follow the separator symbol", token);
                }
            }
            else if (token.Value == Constants.Conditions.CloseHashtable)
            {
                // Validate nothing follows, a separator follows, or close punction follows.
                Token nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] : null;
                ValidateNullOrSeparatorOrClosePunctuation(nextToken);
            }
            else if (token.Value == Constants.Conditions.CloseFunction)
            {
                // Validate current container is a function above min parameters threshold.
                var function = container as FunctionNode;
                if (function == null ||
                    function.Parameters.Count < function.MinParameters)
                {
                    ThrowParseException("Unexpected symbol", token);
                }

                // Validate nothing follows, a separator follows, or close punction follows.
                Token nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] : null;
                ValidateNullOrSeparatorOrClosePunctuation(nextToken);
            }
        }

        private void ValidateNullOrSeparatorOrClosePunctuation(Token token)
        {
            if (token == null)
            {
                return;
            }

            var punctuation = token as PunctuationToken;
            if (punctuation != null)
            {
                switch (punctuation.Value)
                {
                    case Constants.Conditions.CloseFunction:
                    case Constants.Conditions.CloseHashtable:
                    case Constants.Conditions.Separator:
                        return;
                }
            }

            ThrowParseException("Unexpected symbol", token);
        }

        private void ThrowParseException(string description, Token token)
        {
            string rawToken = _raw.Substring(token.Index, token.Length);
            int position = token.Index + 1;
            // TODO: loc
            throw new ParseException($"{description}: '{rawToken}'. Located at position {position} within condition expression: {_raw}");
        }

        private FunctionNode CreateFunction(FunctionToken token)
        {
            ArgUtil.NotNull(token, nameof(token));
            switch (token.Name)
            {
                case Constants.Conditions.And:
                    return new AndFunction(token);
                case Constants.Conditions.Equal:
                    return new EqualFunction(token);
                case Constants.Conditions.GreaterThan:
                    return new GreaterThanFunction(token);
                case Constants.Conditions.GreaterThanOrEqual:
                    return new GreaterThanOrEqualFunction(token);
                case Constants.Conditions.LessThan:
                    return new LessThanFunction(token);
                case Constants.Conditions.LessThanOrEqual:
                    return new LessThanOrEqualFunction(token);
                case Constants.Conditions.Not:
                    return new NotFunction(token);
                case Constants.Conditions.NotEqual:
                    return new NotEqualFunction(token);
                case Constants.Conditions.Or:
                    return new OrFunction(token);
                case Constants.Conditions.Xor:
                    return new XorFunction(token);
                default:
                    throw new NotSupportedException($"Unexpected function token name: '{token.Name}'");
            }
        }

        private HashtableNode CreateHashtable(HashtableToken token)
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
            private readonly NumberStyles NumberStyles =
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowThousands;

            public Node(Token token)
            {
                Token = token;
            }

            public ContainerNode Container { get; set; }

            public Token Token { get; }

            public abstract object GetValue();

            public bool GetValueAsBool()
            {
                object val = GetValue();
                if (val is bool)
                {
                    return (bool)val;
                }
                else if (val is decimal)
                {
                    return (decimal)val != 0m; // 0 converts to false, otherwise true.
                }

                return !string.IsNullOrEmpty(val as string);
            }

            public decimal GetValueAsNumber()
            {
                object val = GetValue();
                decimal d;
                if (TryConvertToNumber(val, out d))
                {
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
                return string.Format(CultureInfo.InvariantCulture, "{0}", GetValue());
            }

            public bool TryGetValueAsNumber(out decimal result)
            {
                return TryConvertToNumber(GetValue(), out result);
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
            public ContainerNode(Token token)
                : base(token)
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
            public LiteralNode(LiteralToken token)
                : base(token)
            {
                ArgUtil.NotNull(token, nameof(token));
            }

            public sealed override object GetValue()
            {
                return (Token as LiteralToken).Value;
            }
        }

        private abstract class HashtableNode : ContainerNode
        {
            public HashtableNode(HashtableToken token)
                : base(token)
            {
                ArgUtil.NotNull(token, nameof(token));
            }
        }

        private abstract class FunctionNode : ContainerNode
        {
            public FunctionNode(FunctionToken token, int minParameters, int maxParameters)
                : base(token)
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
        }

        private sealed class AndFunction : FunctionNode
        {
            public AndFunction(FunctionToken token)
                : base(token, minParameters: 2, maxParameters: int.MaxValue)
            {
            }

            public sealed override object GetValue()
            {
                foreach (Node parameter in Parameters)
                {
                    if (!parameter.GetValueAsBool())
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private class EqualFunction : FunctionNode
        {
            public EqualFunction(FunctionToken token)
                : base(token, minParameters: 2, maxParameters: 2)
            {
            }

            public override object GetValue()
            {
                object left = Parameters[0].GetValue();
                if (left is bool)
                {
                    bool right = Parameters[1].GetValueAsBool();
                    return (bool)left == right;
                }
                else if (left is decimal)
                {
                    decimal right;
                    if (Parameters[1].TryGetValueAsNumber(out right))
                    {
                        return (decimal)left == right;
                    }

                    return false;
                }

                string r = Parameters[1].GetValueAsString();
                return string.Equals(
                    left as string ?? string.Empty,
                    r ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class GreaterThanFunction : FunctionNode
        {
            public GreaterThanFunction(FunctionToken token)
                : base(token, minParameters: 2, maxParameters: 2)
            {
            }

            public sealed override object GetValue()
            {
                object left = Parameters[0].GetValue();
                if (left is bool)
                {
                    bool right = Parameters[1].GetValueAsBool();
                    return ((bool)left).CompareTo(right) == 1;
                }
                else if (left is decimal)
                {
                    decimal right = Parameters[1].GetValueAsNumber();
                    return ((decimal)left).CompareTo(right) == 1;
                }

                string upperLeft = (left as string ?? string.Empty).ToUpperInvariant();
                string upperRight = (Parameters[1].GetValueAsString() ?? string.Empty).ToUpperInvariant();
                return upperLeft.CompareTo(upperRight) == 1;
            }
        }

        private sealed class NotFunction : FunctionNode
        {
            public NotFunction(FunctionToken token)
                : base(token, minParameters: 1, maxParameters: 1)
            {
            }

            public sealed override object GetValue()
            {
                return !Parameters[0].GetValueAsBool();
            }
        }

        private sealed class NotEqualFunction : EqualFunction
        {
            public NotEqualFunction(FunctionToken token)
                : base(token)
            {
            }

            public sealed override object GetValue()
            {
                return !(bool)base.GetValue();
            }
        }

        private sealed class OrFunction : FunctionNode
        {
            public OrFunction(FunctionToken token)
                : base(token, minParameters: 2, maxParameters: int.MaxValue)
            {
            }

            public sealed override object GetValue()
            {
                foreach (Node parameter in Parameters)
                {
                    if (parameter.GetValueAsBool())
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class XorFunction : FunctionNode
        {
            public XorFunction(FunctionToken token)
                : base(token, minParameters: 2, maxParameters: 2)
            {
            }

            public sealed override object GetValue()
            {
                return Parameters[0].GetValueAsBool() ^ Parameters[1].GetValueAsBool();
            }
        }
    }
}