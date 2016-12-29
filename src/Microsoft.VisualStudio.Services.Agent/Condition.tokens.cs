using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed partial class Condition
    {
        private void CreateTokens()
        {
            int index = 0;
            while (true)
            {
                // Skip whitespace.
                while (index < _raw.Length && char.IsWhiteSpace(_raw[index]))
                {
                    index++;
                }

                // Test end of string.
                if (index >= _raw.Length)
                {
                    return;
                }

                // Read the first character to determine the type of token.
                char c = _raw[index];
                switch (c)
                {
                    case ',':
                        _tokens.Add(new CommaToken(index++));
                        return;
                    case '[':
                        _tokens.Add(new OpenBracketToken(index++));
                        return;
                    case ']':
                        _tokens.Add(new CloseBracketToken(index++));
                        return;
                    case '(':
                        _tokens.Add(new OpenParenToken(index++));
                        return;
                    case ')':
                        _tokens.Add(new CloseParenToken(index++));
                        return;
                    case '\'':
                        CreateStringToken(ref index);
                        return;
                    default:
                        if (c == '-' || c == '.' || (c >= '0' && c <= '9'))
                        {
                            CreateNumberToken(ref index);
                            return;
                        }

                        CreateKeywordToken(ref index);
                        return;
                }
            }
        }

        private void CreateNumberToken(ref int index)
        {
            int startIndex = index;
            index++; // Skip the first char. It is already known to be the start of the number.
            while (index < _raw.Length && !IsWhitespaceOrPunctuation(_raw[index]))
            {
                index++;
            }

            // Note, NumberStyles.AllowThousands cannot be allowed since comma has special meaning to the parser.
            decimal d;
            int length = index - startIndex;
            string str = _raw.Substring(startIndex, length);
            if (decimal.TryParse(
                str,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out d))
            {
                _tokens.Add(new NumberToken(d, startIndex, length));
                return;
            }

            _tokens.Add(new MalformedNumberToken(startIndex, length));
        }

        private void CreateKeywordToken(ref int index)
        {
            int startIndex = index;
            index++; // Skip the first char. It is already known to be the start of the keyword.
            while (index < _raw.Length && !IsWhitespaceOrPunctuation(_raw[index]))
            {
                index++;
            }

            int length = index - startIndex;
            string str = _raw.Substring(startIndex, length);
            switch (str.ToUpperInvariant())
            {
                case "TRUE":
                    _tokens.Add(new TrueToken(startIndex, length));
                    return;
                case "FALSE":
                    _tokens.Add(new FalseToken(startIndex, length));
                    return;
                case "AND":
                    _tokens.Add(new AndToken(startIndex, length));
                    return;
                case "OR":
                    _tokens.Add(new OrToken(startIndex, length));
                    return;
                case "XOR":
                    _tokens.Add(new XorToken(startIndex, length));
                    return;
                case "NOT":
                    _tokens.Add(new NotToken(startIndex, length));
                    return;
                case "VARIABLES":
                    _tokens.Add(new VariablesToken(startIndex, length));
                    return;
                case "CAPABILITIES":
                    _tokens.Add(new CapabilitiesToken(startIndex, length));
                    return;
                default:
                    _tokens.Add(new UnrecognizedToken(startIndex, length));
                    return;
            }
        }

        private void CreateStringToken(ref int index)
        {
            // TODO: Confirm double-single-quote for escaping is sufficient. Better than backslash-escaping since this is not a complex language and backslash is common to file-paths.
            int startIndex = index;
            char c;
            bool closed = false;
            var str = new StringBuilder();
            index++; // Skip the leading single-quote.
            while (index < _raw.Length)
            {
                c = _raw[index++];
                if (c == '\'')
                {
                    // End of string.
                    if (index >= _raw.Length || _raw[index] != '\'')
                    {
                        closed = true;
                        break;
                    }

                    // Escaped single quote.
                    index++;
                }

                str.Append(c);
            }

            int length = index - startIndex;
            if (closed)
            {
                _tokens.Add(new StringToken(str.ToString(), startIndex, length));
                return;
            }

            _tokens.Add(new UnterminatedStringToken(startIndex, length));
        }

        private static bool IsWhitespaceOrPunctuation(char c)
        {
            switch (c)
            {
                case ',':
                case '[':
                case ']':
                case '(':
                case ')':
                    return true;
                default:
                    return char.IsWhiteSpace(c);
            }
        }

        private abstract class Token
        {
            public Token(int index, int length = 1)
            {
                Index = index;
                Length = length;
            }

            public int Index { get; }

            public int Length { get; }
        }

        // --------------------------------------------------------------------------------
        // Punctuation: , [ ] ( )
        // --------------------------------------------------------------------------------
        private sealed class PunctuationToken : Token
        {
            public PunctuationToken(int index)
                : base(index)
            {
            }
        }
        private sealed class CommaToken : PunctuationToken
        {
            public CommaToken(int index)
                : base(index)
            {
            }
        }

        private sealed class OpenBracketToken : PunctuationToken
        {
            public OpenBracketToken(int index)
                : base(index)
            {
            }
        }

        private sealed class CloseBracketToken : PunctuationToken
        {
            public CloseBracketToken(int index)
                : base(index)
            {
            }
        }

        private sealed class OpenParenToken : PunctuationToken
        {
            public OpenParenToken(int index)
                : base(index)
            {
            }
        }

        private sealed class CloseParenToken : PunctuationToken
        {
            public CloseParenToken(int index)
                : base(index)
            {
            }
        }

        // --------------------------------------------------------------------------------
        // Literals: True, False, Number, String
        // --------------------------------------------------------------------------------
        private abstract class LiteralToken : Token
        {
            public LiteralToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class TrueToken : LiteralToken
        {
            public TrueToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class FalseToken : LiteralToken
        {
            public FalseToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class NumberToken : LiteralToken
        {
            public NumberToken(decimal d, int index, int length)
                : base(index, length)
            {
                Value = d;
            }

            public decimal Value { get; }
        }

        private sealed class StringToken : LiteralToken
        {
            public StringToken(string str, int index, int length)
                : base(index, length)
            {
                Value = str;
            }

            public string Value { get; }
        }

        // --------------------------------------------------------------------------------
        // Hashtable: Capabilities, Variables
        // --------------------------------------------------------------------------------
        private abstract class HashtableToken : Token
        {
            public HashtableToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class CapabilitiesToken : HashtableToken
        {
            public CapabilitiesToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class VariablesToken : HashtableToken
        {
            public VariablesToken(int index, int length)
                : base(index, length)
            {
            }
        }

        // --------------------------------------------------------------------------------
        // Functions: And, Or, Xor, Not
        // --------------------------------------------------------------------------------
        private abstract class FunctionToken : Token
        {
            public FunctionToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class AndToken : FunctionToken
        {
            public AndToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class OrToken : FunctionToken
        {
            public OrToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class XorToken : FunctionToken
        {
            public XorToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class NotToken : FunctionToken
        {
            public NotToken(int index, int length)
                : base(index, length)
            {
            }
        }

        // --------------------------------------------------------------------------------
        // Invalid: Malformed Number, Unterminated String, Unrecognized
        // --------------------------------------------------------------------------------
        private abstract class InvalidToken : Token
        {
            public InvalidToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class MalformedNumberToken : InvalidToken
        {
            public MalformedNumberToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class UnterminatedStringToken : InvalidToken
        {
            public UnterminatedStringToken(int index, int length)
                : base(index, length)
            {
            }
        }

        private sealed class UnrecognizedToken : InvalidToken
        {
            public UnrecognizedToken(int index, int length)
                : base(index, length)
            {
            }
        }
    }
}

/*
Team build provides users with the capability to create a single definition with multiple different triggers.  Depending on the trigger configuration those triggers my also build many different branches.  This capability has solved some of the scenarios where customers of Xaml build had to create and maintain multiple build definitions, however, it still falls short in a couple of key ways.   
 
One specific example is the ability to have certain triggers do more or less work in a given build.  It is very common for a user to want to configure their CI build to be very fast and run a minimal set of tests while having a nightly scheduled build run a larger set of tests.  Currently the only option the user has is to fall back on writing a script to run their tests and then check the BUILD_REASON environment variable.  While this is a work around is does reduce the overall usefulness of our CI system. 
 
To improve on the scenario of having a single build definition that builds multiple different triggers and branches we can introduce the concept of a Conditional on each task and phase that will be evaluated first on the server and then on the agent.  In the case of the server evaluation a negative condition acts in the same way a disabled task would and it is removed from the job before it is sent to the agent. 
 
 
Supported Conditions 
equals 
This condition will be true if an empty value is specified and the variable exists and its value is an empty string; or if a value is specified and the variable contains the specified value and the variable exists.  The comparison is done in an invariant culture and is case insensitive. 
does not equal 
The condition will be true if an empty value is specified and the variable exists and its value is NOT empty; or if a specific value is specified and either the variable doesn't exist or the variable exists and its value does not equal the specified value. The comparison is done in an invariant culture and is case insensitive. 
does not contain 
This condition will be true if the specified variable either does not exists or exists and does not contain the specified string. 
is more than 
is not more than 
is less than 
is not less than 
The conditions only work with numbers.  If the variable contains a value that can't be coerced into a number the condition will evaluate to false. 
matches 
does not match 
This condition will be true if the specified variable exists and its value matches/does not match the specified regular expression 
 
Expression syntax 
The UI can provide for an editor but the expression should be stored in a simple syntax that will be easily compatible with config as code scenarios.  The syntax will simply be a nested set of functions that are evaluated starting with the inner function and working its way out.  All expressions must ultimately result in a boolean.  At a later date we could easily add additional evaluators to the expression for string parsing and other operations. 
 
Example: Run a step only for the master branch 
@equals(variables['Source.BranchName'], 'master') 
 
Functions can be nested to give more complex expressions 
Example: Run a task for all branches other than master 
@not(equals(variables['Source.BranchName'], 'master')) 
 
@greaterOrEqual(1,2) 
 
@continueOnError() 
 
@runAlways() 
 
@and(equals(capabilities['node]')
*/