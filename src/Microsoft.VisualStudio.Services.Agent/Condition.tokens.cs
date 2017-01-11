using System;
using System.Globalization;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed partial class Condition
    {
        private Token GetNextToken()
        {
            // Skip whitespace.
            while (_index < _raw.Length && char.IsWhiteSpace(_raw[_index]))
            {
                _index++;
            }

            // Test end of string.
            if (_index >= _raw.Length)
            {
                return null;
            }

            // Read the first character to determine the type of token.
            char c = _raw[_index];
            switch (c)
            {
                case Constants.Conditions.CloseHashtable:
                    return new Token(TokenKind.CloseHashtable, _index++);
                case Constants.Conditions.CloseFunction:
                    return new Token(TokenKind.CloseFunction, _index++);
                case Constants.Conditions.OpenHashtable:
                    return new Token(TokenKind.OpenHashtable, _index++);
                case Constants.Conditions.OpenFunction:
                    return new Token(TokenKind.OpenFunction, _index++);
                case Constants.Conditions.Separator:
                    return new Token(TokenKind.Separator, _index++);
                // // _trace.Verbose($"Pnctu: {c}");
                // // _tokens.Add(new PunctuationToken(c, index++));
                // // continue;
                case '\'':
                    return ReadStringToken();
                // // continue;
                default:
                    if (c == '-' || c == '.' || (c >= '0' && c <= '9'))
                    {
                        return ReadNumberOrVersionToken();
                        // // continue;
                    }

                    return ReadKeywordToken();
                    // // continue;
            }
        }

        private Token ReadNumberOrVersionToken()
        {
            int startIndex = _index;
            int periods = 0;
            do
            {
                if (_raw[_index] == '.')
                {
                    periods++;
                }

                _index++;
            }
            while (_index < _raw.Length && !TestWhitespaceOrPunctuation(_raw[_index]));

            int length = _index - startIndex;
            string str = _raw.Substring(startIndex, length);
            if (periods >= 2)
            {
                Version version;
                if (Version.TryParse(str, out version))
                {
                    return new Token(TokenKind.Version, startIndex, length, version);
                }
            }
            else
            {
                // Note, NumberStyles.AllowThousands cannot be allowed since comma has special meaning as a token separator.
                decimal d;
                if (decimal.TryParse(
                        str,
                        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out d))
                {
                    return new Token(TokenKind.Number, startIndex, length, d);
                }
            }

            return new Token(TokenKind.Unrecognized, startIndex, length);
            // // // Note, NumberStyles.AllowThousands cannot be allowed since comma has special meaning to the parser.
            // // decimal d;
            // // int length = index - startIndex;
            // // string str = _raw.Substring(startIndex, length);
            // // if (decimal.TryParse(
            // //     str,
            // //     NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            // //     CultureInfo.InvariantCulture,
            // //     out d))
            // // {
            // //     _trace.Verbose($"Numbr: {d}");
            // //     _tokens.Add(new NumberToken(d, startIndex, length));
            // //     return;
            // // }

            // // _trace.Verbose($"InvNm: '{str}'");
            // // _tokens.Add(new MalformedNumberToken(startIndex, length));
        }

        private Token ReadKeywordToken()
        {
            // Read to the end of the keyword.
            int startIndex = _index;
            _index++; // Skip the first char. It is already known to be the start of the keyword.
            while (_index < _raw.Length && !TestWhitespaceOrPunctuation(_raw[_index]))
            {
                _index++;
            }

            // Convert to token.
            int length = _index - startIndex;
            string str = _raw.Substring(startIndex, length);
            if (str.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                // // _trace.Verbose($"Booln:  {true}");
                return new Token(TokenKind.True, startIndex, length);
            }
            else if (str.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase))
            {
                // // _trace.Verbose($"Booln:  {false}");
                return new Token(TokenKind.False, startIndex, length);
            }
            // Functions
            else if (str.Equals(Constants.Conditions.And, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.And, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.Equal, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.Equal, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.GreaterThan, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.GreaterThan, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.GreaterThanOrEqual, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.GreaterThanOrEqual, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.LessThan, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.LessThan, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.LessThanOrEqual, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.LessThanOrEqual, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.Not, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.Not, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.NotEqual, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.NotEqual, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.Or, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.Or, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.Xor, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.Xor, startIndex, length);
            }
            // Hashtables
            else if (str.Equals(Constants.Conditions.Capabilities, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.Capabilities, startIndex, length);
            }
            else if (str.Equals(Constants.Conditions.Variables, StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenKind.Variables, startIndex, length);
            }
            // Unrecognized
            else
            {
                return new Token(TokenKind.Unrecognized, startIndex, length);
            }

            // //  ||
            // //     str.Equals(Constants.Conditions.Equal, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.GreaterThan, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.GreaterThanOrEqual, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.LessThan, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.LessThanOrEqual, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.Not, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.NotEqual, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.Or, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.Xor, StringComparison.OrdinalIgnoreCase))
            // // {
            // //     _trace.Verbose($"Fnctn: {str}");
            // //     _tokens.Add(new FunctionToken(str, startIndex, length));
            // // }
            // // else if (str.Equals(Constants.Conditions.Capabilities, StringComparison.OrdinalIgnoreCase) ||
            // //     str.Equals(Constants.Conditions.Variables, StringComparison.OrdinalIgnoreCase))
            // // {
            // //     _trace.Verbose($"Hasht: {str}");
            // //     _tokens.Add(new HashtableToken(str, startIndex, length));
            // // }
            // // else
            // // {
            // //     _trace.Verbose($"Unrec: {str}");
            // //     _tokens.Add(new UnrecognizedToken(startIndex, length));
            // // }
        }

        private Token ReadStringToken()
        {
            // TODO: Confirm double-single-quote for escaping is sufficient. Better than backslash-escaping since this is not a complex language and backslash is common to file-paths.
            int startIndex = _index;
            char c;
            bool closed = false;
            var str = new StringBuilder();
            _index++; // Skip the leading single-quote.
            while (_index < _raw.Length)
            {
                c = _raw[_index++];
                if (c == '\'')
                {
                    // End of string.
                    if (_index >= _raw.Length || _raw[_index] != '\'')
                    {
                        closed = true;
                        break;
                    }

                    // Escaped single quote.
                    _index++;
                }

                str.Append(c);
            }

            int length = _index - startIndex;
            if (closed)
            {
                // _trace.Verbose($"Strng: '{str.ToString()}'");
                return new Token(TokenKind.String, startIndex, length, str.ToString());
            }

            // _trace.Verbose($"InvSt: '{str.ToString()}'");
            return new Token(TokenKind.Unrecognized, startIndex, length);
        }

        private static bool TestWhitespaceOrPunctuation(char c)
        {
            switch (c)
            {
                case Constants.Conditions.CloseFunction:
                case Constants.Conditions.CloseHashtable:
                case Constants.Conditions.OpenFunction:
                case Constants.Conditions.OpenHashtable:
                case Constants.Conditions.Separator:
                    return true;
                default:
                    return char.IsWhiteSpace(c);
            }
        }

        private sealed class Token
        {
            public Token(TokenKind kind, int index, int length = 1, object parsedValue = null)
            {
                Kind = kind;
                Index = index;
                Length = length;
                ParsedValue = parsedValue;
            }

            public TokenKind Kind { get; }

            public int Index { get; }

            public int Length { get; }

            public object ParsedValue { get; }
        }

        // // // Punctuation: , [ ] ( )
        // // private sealed class PunctuationToken : Token
        // // {
        // //     public PunctuationToken(char c, int index)
        // //         : base(index)
        // //     {
        // //         Value = c;
        // //     }

        // //     public char Value { get; }
        // // }

        // // // Literals: True, False, Number, String
        // // private abstract class LiteralToken : Token
        // // {
        // //     public LiteralToken(object o, int index, int length)
        // //         : base(index, length)
        // //     {
        // //         Value = o;
        // //     }

        // //     public object Value { get; private set; }
        // // }

        // // private sealed class BooleanToken : LiteralToken
        // // {
        // //     public BooleanToken(bool b, int index, int length)
        // //         : base(b, index, length)
        // //     {
        // //     }
        // // }

        // // private sealed class NumberToken : LiteralToken
        // // {
        // //     public NumberToken(decimal d, int index, int length)
        // //         : base(d, index, length)
        // //     {
        // //     }
        // // }

        // // private sealed class StringToken : LiteralToken
        // // {
        // //     public StringToken(string s, int index, int length)
        // //         : base(s, index, length)
        // //     {
        // //     }
        // // }

        // // // Hashtable: Capabilities, Variables
        // // private sealed class HashtableToken : Token
        // // {
        // //     public HashtableToken(string name, int index, int length)
        // //         : base(index, length)
        // //     {
        // //         Name = name;
        // //     }

        // //     public string Name { get; }
        // // }

        // // // Functions: And, Not, Or, Xor
        // // private sealed class FunctionToken : Token
        // // {
        // //     public FunctionToken(string name, int index, int length)
        // //         : base(index, length)
        // //     {
        // //         Name = name;
        // //     }

        // //     public string Name { get; }
        // // }

        // // // --------------------------------------------------------------------------------
        // // // Invalid: Malformed Number, Unterminated String, Unrecognized
        // // // --------------------------------------------------------------------------------
        // // private abstract class InvalidToken : Token
        // // {
        // //     public InvalidToken(int index, int length)
        // //         : base(index, length)
        // //     {
        // //     }
        // // }

        // // private sealed class MalformedNumberToken : InvalidToken
        // // {
        // //     public MalformedNumberToken(int index, int length)
        // //         : base(index, length)
        // //     {
        // //     }
        // // }

        // // private sealed class UnterminatedStringToken : InvalidToken
        // // {
        // //     public UnterminatedStringToken(int index, int length)
        // //         : base(index, length)
        // //     {
        // //     }
        // // }

        // // private sealed class UnrecognizedToken : InvalidToken
        // // {
        // //     public UnrecognizedToken(int index, int length)
        // //         : base(index, length)
        // //     {
        // //     }
        // // }

        private enum TokenKind
        {
            // Punctuation
            CloseHashtable,
            CloseFunction,
            OpenHashtable,
            OpenFunction,
            Separator,

            // Literal value types
            True,
            False,
            Number,
            Version,
            String,

            // Functions
            And,
            Equal,
            GreaterThan,
            GreaterThanOrEqual,
            LessThan,
            LessThanOrEqual,
            Not,
            NotEqual,
            Or,
            Xor,

            // Hashtables
            Capabilities,
            Variables,

            Unrecognized,
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
