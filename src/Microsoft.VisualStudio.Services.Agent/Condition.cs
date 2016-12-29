using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed partial class Condition
    {
        private readonly List<Token> _tokens = new List<Token>();
        private readonly string _raw; // Raw condition string.
        private Node _root;

        public Condition(string condition)
        {
            _raw = condition;
            CreateTokens();
            CreateTree();
        }

        private void CreateTree()
        {
            ContainerNode parentNode = null;
            for (int tokenIndex = 0; tokenIndex < _tokens.Count; tokenIndex++)
            {
                Token token = _tokens[tokenIndex];
                ThrowIfInvalid(token);

                // Check if punctuation.
                var punctuation = token as PunctuationToken;
                if (punctuation != null)
                {
                    ValidatePunctuation(parentNode, punctuation, tokenIndex);
                    if (punctuation.Value == Constants.Conditions.CloseFunction ||
                        punctuation.Value == Constants.Conditions.CloseHashtable)
                    {
                        parentNode = parentNode.Parent; // Pop parent.
                    }

                    continue;
                }

                // Validate the token and create the node.
                Node newNode = null;
                if (token is LiteralToken)
                {
                    ValidateLiteral(parentNode, token as LiteralToken, tokenIndex);
                    newNode = new LiteralNode(token as LiteralToken);
                }
                else if (token is FunctionToken)
                {
                    ValidateFunction(parentNode, token as FunctionToken, tokenIndex);
                    tokenIndex++; // Skip the open paren that follows.
                    newNode = new FunctionNode(token as FunctionToken);
                }
                else if (token is HashtableToken)
                {
                    ValidateHashtable(parentNode, token as HashtableToken, tokenIndex);
                    tokenIndex++; // Skip the open bracket that follows.
                    newNode = new HashtableNode(token as HashtableToken);
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
                    parentNode.AddChild(newNode);
                }

                // Adjust parent.
                if (newNode is ContainerNode)
                {
                    parentNode = newNode as ContainerNode;
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
                    Throw("Unable to parse number", token);
                }
                else if (token is UnterminatedStringToken)
                {
                    Throw("Unterminated string", token);
                }
                else if (token is UnrecognizedToken)
                {
                    Throw("Unrecognized keyword", token);
                }

                throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
            }
        }

        private void ValidateLiteral(Node parentNode, LiteralToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Validate nothing follows, a separator follows, or close punction follows.
            Token nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] : null;
            ValidateNullOrSeparatorOrClosePunctuation(nextToken);
        }

        private void ValidateHashtable(Node parentNode, HashtableToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Validate open bracket follows.
            PunctuationToken nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] as PunctuationToken : null;
            if (nextToken == null || nextToken.Value != Constants.Conditions.OpenHashtable)
            {
                Throw($"Expected '{Constants.Conditions.OpenHashtable}' to follow symbol", token);
            }

            // Validate a literal, hashtable, or function follows.
            Token nextNextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 2] : null;
            if (nextNextToken as LiteralToken == null && nextNextToken as HashtableToken == null && nextNextToken as FunctionToken == null)
            {
                Throw("Expected a value to follow symbol", nextToken);
            }
        }

        private void ValidateFunction(Node parentNode, FunctionToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Valdiate open paren follows.
            PunctuationToken nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] as PunctuationToken : null;
            if (nextToken == null || nextToken.Value != Constants.Conditions.OpenFunction)
            {
                Throw($"Expected '{Constants.Conditions.OpenFunction}' to follow symbol", token);
            }

            // Validate a literal, hashtable, or function follows.
            Token nextNextToken = tokenIndex + 1 < _tokens.Count ? _tokens[tokenIndex + 2] : null;
            if (nextNextToken as LiteralToken == null && nextNextToken as HashtableToken == null && nextNextToken as FunctionToken == null)
            {
                Throw("Expected a value to follow symbol", nextToken);
            }
        }

        private void ValidatePunctuation(ContainerNode parentNode, PunctuationToken token, int tokenIndex)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Required open brackets and parens are validated and skipped when a hashtable
            // or function node is created. Any open bracket or paren tokens found at this
            // point are errors.
            if (token.Value == Constants.Conditions.OpenFunction ||
                token.Value == Constants.Conditions.OpenHashtable)
            {
                Throw("Unexpected symbol", token);
            }

            if (parentNode == null)
            {
                // A condition cannot lead with punction.
                // And punction should not trail the closing of the root node.
                Throw("Unexpected symbol", token);
            }

            if (token.Value == Constants.Conditions.Separator)
            {
                // Validate parent is a function under max parameters threshold.
                if (!(parentNode is FunctionNode) ||
                    parentNode.Children.Count >= parentNode.MaxChildren)
                {
                    Throw("Unexpected symbol", token);
                }

                // Validate a literal, function, or hashtable follows.
                Token nextToken = tokenIndex < _tokens.Count ? _tokens[tokenIndex + 1] : null;
                if (nextToken == null ||
                    (!(nextToken is LiteralToken) && !(nextToken is FunctionToken) && !(nextToken is HashtableToken)))
                {
                    Throw("Expected another value to follow the separator symbol", token);
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
                // Validate parent is a function above min parameters threshold.
                if (!(parentNode is FunctionNode) ||
                    parentNode.Children.Count < parentNode.MinChildren)
                {
                    Throw("Unexpected symbol", token);
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

            Throw("Unexpected symbol", token);
        }

        private void Throw(string description, Token token)
        {
            string rawToken = _raw.Substring(token.Index, token.Length);
            int position = token.Index + 1;
            // TODO: loc
            throw new ParseException($"{description}: '{rawToken}'. Located at position {position} within condition expression: {_raw}");
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
            public Node(Token token)
            {
                Token = token;
            }

            public ContainerNode Parent { get; set; }

            public Token Token { get; }
        }

        private abstract class ContainerNode : Node
        {
            public ContainerNode(Token token)
                : base(token)
            {
            }

            private readonly List<Node> _children = new List<Node>();

            public IReadOnlyList<Node> Children => _children.AsReadOnly();

            public abstract int MinChildren { get; }

            public abstract int MaxChildren { get; }

            public void AddChild(Node child)
            {
                _children.Add(child);
                child.Parent = this;
            }
        }

        private sealed class LiteralNode : Node
        {
            public LiteralNode(LiteralToken token)
                : base(token)
            {
                ArgUtil.NotNull(token, nameof(token));
            }
        }

        private sealed class HashtableNode : ContainerNode
        {
            public HashtableNode(HashtableToken token)
                : base(token)
            {
                ArgUtil.NotNull(token, nameof(token));
            }

            public sealed override int MinChildren => 1;

            public sealed override int MaxChildren => 1;
        }

        private sealed class FunctionNode : ContainerNode
        {
            public FunctionNode(FunctionToken token)
                : base(token)
            {
                ArgUtil.NotNull(token, nameof(token));
                if (string.Equals(token.Name, Constants.Conditions.And, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(token.Name, Constants.Conditions.Or, StringComparison.OrdinalIgnoreCase))
                {
                    MaxChildren = int.MaxValue;
                }
                else if (string.Equals(token.Name, Constants.Conditions.Xor, StringComparison.OrdinalIgnoreCase))
                {
                    MaxChildren = 2;
                }
                else if (string.Equals(token.Name, Constants.Conditions.Not, StringComparison.OrdinalIgnoreCase))
                {
                    MaxChildren = 1;
                }
            }

            public sealed override int MinChildren => 1;
            
            public sealed override int MaxChildren { get; }
        }
    }
}