// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Hexa.NET.Utilities;
using HexaGen.CppAst.Utilities;
using System;

namespace HexaGen.CppAst.Parsing
{
    public readonly unsafe struct CString : IEquatable<CString>
    {
        private readonly byte* data;
        private readonly int length;

        public CString(byte* data, int length)
        {
            this.data = data;
            this.length = length;
        }

        public readonly byte* CStr => data;

        public readonly int Length => length;

        public readonly Span<byte> AsSpan() => new(data, length);

        public override readonly bool Equals(object? obj)
        {
            return obj is CString @string && Equals(@string);
        }

        public readonly bool Equals(CString other)
        {
            return Utils.StrCmp(data, other.data) == 0;
        }

        public override readonly int GetHashCode()
        {
            return (int)MurmurHash3.Hash32(AsSpan());
        }

        public static bool operator ==(CString left, CString right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CString left, CString right)
        {
            return !(left == right);
        }

        public static implicit operator Span<byte>(CString str) => str.AsSpan();
    }
}