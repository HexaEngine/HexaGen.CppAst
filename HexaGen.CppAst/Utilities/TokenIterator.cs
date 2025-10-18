// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using HexaGen.CppAst.Model.Expressions;

namespace HexaGen.CppAst.Utilities
{
    /// <summary>
    /// Internal class to iterate on tokens
    /// </summary>
    public class TokenIterator
    {
        private readonly Tokenizer tokens;
        private int index;

        public TokenIterator(Tokenizer tokens)
        {
            this.tokens = tokens;
        }

        public bool Skip(string expectedText)
        {
            if (index < tokens.Count)
            {
                if (tokens.GetString(index) == expectedText)
                {
                    index++;
                    return true;
                }
            }

            return false;
        }

        public CppToken PreviousToken()
        {
            if (index > 0)
            {
                return tokens[index - 1];
            }

            return null;
        }

        public bool Skip(params string[] expectedTokens)
        {
            var startIndex = index;
            foreach (var expectedToken in expectedTokens)
            {
                if (startIndex < tokens.Count)
                {
                    if (tokens.GetString(startIndex) == expectedToken)
                    {
                        startIndex++;
                        continue;
                    }
                }
                return false;
            }
            index = startIndex;
            return true;
        }

        public bool Find(params string[] expectedTokens)
        {
            var startIndex = index;
        restart:
            while (startIndex < tokens.Count)
            {
                var firstIndex = startIndex;
                foreach (var expectedToken in expectedTokens)
                {
                    if (startIndex < tokens.Count)
                    {
                        if (tokens.GetString(startIndex) == expectedToken)
                        {
                            startIndex++;
                            continue;
                        }
                    }
                    startIndex = firstIndex + 1;
                    goto restart;
                }
                index = firstIndex;
                return true;
            }
            return false;
        }

        public bool Next(out CppToken token)
        {
            token = null;
            if (index < tokens.Count)
            {
                token = tokens[index];
                index++;
                return true;
            }
            return false;
        }

        public bool CanPeek => index < tokens.Count;

        public bool Next()
        {
            if (index < tokens.Count)
            {
                index++;
                return true;
            }
            return false;
        }

        public CppToken Peek()
        {
            if (index < tokens.Count)
            {
                return tokens[index];
            }
            return null;
        }

        public string PeekText()
        {
            if (index < tokens.Count)
            {
                return tokens.GetString(index);
            }
            return null;
        }
    }
}