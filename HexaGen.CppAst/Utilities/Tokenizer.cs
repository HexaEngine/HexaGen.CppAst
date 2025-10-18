// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Model.Expressions;
using HexaGen.CppAst.Parsing;
using System.Diagnostics;
using System.Text;

namespace HexaGen.CppAst.Utilities
{
    /// <summary>
    /// Internal class to tokenize
    /// </summary>
    [DebuggerTypeProxy(typeof(TokenizerDebuggerType))]
    public class Tokenizer
    {
        private readonly CXSourceRange range;
        private CppToken[] cppTokens;
        protected readonly CXTranslationUnit tu;

        public Tokenizer(CXCursor cursor)
        {
            tu = cursor.TranslationUnit;
            range = GetRange(cursor);
        }

        public Tokenizer(CXTranslationUnit tu, CXSourceRange range)
        {
            this.tu = tu;
            this.range = range;
        }

        public virtual CXSourceRange GetRange(CXCursor cursor)
        {
            return cursor.Extent;
        }

        public int Count
        {
            get
            {
                var tokens = tu.Tokenize(range);
                int length = tokens.Length;
                tu.DisposeTokens(tokens);
                return length;
            }
        }

        public CppToken this[int i]
        {
            get
            {
                // Only create a tokenizer if necessary
                cppTokens ??= new CppToken[Count];

                ref var cppToken = ref cppTokens[i];
                if (cppToken != null)
                {
                    return cppToken;
                }
                var tokens = tu.Tokenize(range);
                var token = tokens[i];

                CppTokenKind cppTokenKind = 0;
                switch (token.Kind)
                {
                    case CXTokenKind.CXToken_Punctuation:
                        cppTokenKind = CppTokenKind.Punctuation;
                        break;

                    case CXTokenKind.CXToken_Keyword:
                        cppTokenKind = CppTokenKind.Keyword;
                        break;

                    case CXTokenKind.CXToken_Identifier:
                        cppTokenKind = CppTokenKind.Identifier;
                        break;

                    case CXTokenKind.CXToken_Literal:
                        cppTokenKind = CppTokenKind.Literal;
                        break;

                    case CXTokenKind.CXToken_Comment:
                        cppTokenKind = CppTokenKind.Comment;
                        break;

                    default:
                        break;
                }

                var tokenStr = CXUtil.GetTokenSpelling(token, tu);
                var tokenLocation = token.GetLocation(tu);

                var tokenRange = token.GetExtent(tu);
                cppToken = new CppToken(cppTokenKind, tokenStr)
                {
                    Span = tokenRange.ToSourceRange()
                };
                tu.DisposeTokens(tokens);
                return cppToken;
            }
        }

        public string GetString(int i)
        {
            var tokens = tu.Tokenize(range);
            var TokenSpelling = CXUtil.GetTokenSpelling(tokens[i], tu);
            tu.DisposeTokens(tokens);
            return TokenSpelling;
        }

        public string TokensToString()
        {
            int length = Count;
            if (length <= 0)
            {
                return null;
            }

            List<CppToken> tokens = new(length);

            for (int i = 0; i < length; i++)
            {
                tokens.Add(this[i]);
            }

            return CppToken.TokensToString(tokens);
        }

        public string GetStringForLength(int length)
        {
            StringBuilder result = new(length);
            for (var cur = 0; cur < Count; ++cur)
            {
                result.Append(GetString(cur));
                if (result.Length >= length)
                    return result.ToString();
            }
            return result.ToString();
        }
    }

    public class TokenizerDebuggerType
    {
        private readonly Tokenizer tokenizer;

        public TokenizerDebuggerType(Tokenizer tokenizer)
        {
            this.tokenizer = tokenizer;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object[] Items
        {
            get
            {
                var array = new object[tokenizer.Count];
                for (int i = 0; i < tokenizer.Count; i++)
                {
                    array[i] = tokenizer[i];
                }
                return array;
            }
        }
    }
}