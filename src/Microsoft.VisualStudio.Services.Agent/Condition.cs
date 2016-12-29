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
            Node parentNode = null;
            for (int tokenIndex = 0; tokenIndex < _tokens.Count; tokenIndex++)
            {
                Token token = _tokens[tokenIndex];
                ThrowIfInvalidToken(token);

                // Test if punctuation.
                if (token is PunctuationToken)
                {
                    ValidatePunctuation(parentNode, token as PunctuationToken);
                    if (token is CloseBracketToken || token is CloseParenToken)
                    {
                        parentNode = parentNode.Parent;
                    }

                    continue;
                }

                // Validate.
                if (token is LiteralToken)
                {
                    ValidateLiteral(parentNode, token);
                }
                else if (token is FunctionToken)
                {
                    ValidateFunction(parentNode, token);
                }
                else if (token is HashtableToken)
                {
                    ValidateHashtable(parentNode, token);
                }
                else
                {
                    throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
                }

                // Create the node.
                if (_root == null)
                {
                    _root = CreateNode(token);
                }
                else
                {
                    parentNode.AddChild(CreateNode(token));
                }
            }
        }

        private void ThrowIfInvalidToken(Token token)
        {
            ArgUtil.NotNull(token, nameof(token));
            if (token is InvalidToken)
            {
                if (token is MalformedNumberToken)
                {
                    Throw("Unable to parse number");
                }
                else if (token is UnterminatedStringToken)
                {
                    Throw("Unterminated string");
                }
                else if (token is UnrecognizedToken)
                {
                    Throw("Unrecognized keyword");
                }

                throw new NotSupportedException("Unexpected token type: " + token.GetType().FullName);
            }
        }

        private void ValidatePunctuation(Node parentNode, PunctuationToken token)
        {
            ArgUtil.NotNull(token, nameof(token));

            // Required open brackets and parens are validated and skipped when a hashtable
            // or function node is created. Any open bracket or paren tokens found at this
            // point are errors.
            if (token is OpenBracketToken ||
                token is OpenParenToken)
            {
                Throw("Unexpected symbol", token);
            }

            if (parentNode == null)
            {
                // A condition cannot lead with punction.
                // And punction should not trail the closing of the root node.
                if (token is CommaToken ||
                    token is CloseBracketToken ||
                    token is CloseParenToken)
                {
                    Throw("Unexpected symbol", token);
                }
            }
            else
            {
                if (token is CommaToken)
                {
                    // Validate parent is a function under max parameters threshold.
                    if (!(parentNode is FunctionNode) ||
                        parentNode.Children.Count >= parentNode.MaxChildren)
                    {
                        Throw("Unexpected symbol", token);
                    }
                }
                else if (token is CloseBracketToken)
                {
                    // Validate parent is a hashtable with exactly one parameter.
                    if (!(parentNode is HashtableNode) ||
                        parentNode.Children.Count != 1)
                    {
                        Throw("Unexpected symbol", token);
                    }
                }
                else if (token is CloseParenToken)
                {
                    // Validate parent is a function above min parameters threshold.
                    if (!(parentNode is FunctionNode) ||
                        parentNode.Children.Count < parentNode.MinChildren)
                    {
                        Throw("Unexpected symbol", token);
                    }
                }
            }
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
            public Node Parent { get; set; }
        }

        private abstract class ContainerNode : Node
        {
            private readonly List<Node> _children = new List<Node>();

            private IReadOnlyList<Node> Children => _children.AsReadOnly();

            public void AddChild(Node child)
            {
                _children.Add(child);
                child.Parent = this;
            }
        }

        private abstract class LiteralNode : Node
        {
        }

        private abstract class HashtableNode : ContainerNode
        {
        }

        private abstract class FunctionNode : ContainerNode
        {
        }
    }
}