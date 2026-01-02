// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Extensions;
using System;

namespace HexaGen.CppAst.Model.Types
{
    /// <summary>
    /// A C++ pointer type (e.g `int*`)
    /// </summary>
    public sealed class CppPointerType : CppTypeWithElementType
    {
        /// <summary>
        /// Constructor of a pointer type.
        /// </summary>
        /// <param name="cursor"></param>
        /// <param name="elementType">The element type pointed to.</param>
        public CppPointerType(CXCursor cursor, CppType elementType) : base(cursor, CppTypeKind.Pointer, elementType)
        {
            SizeOf = nint.Size;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{ElementType.GetDisplayName()} *";
        }

        /// <inheritdoc />
        public override CppType GetCanonicalType()
        {
            var elementTypeCanonical = ElementType.GetCanonicalType();
            if (ReferenceEquals(elementTypeCanonical, ElementType)) return this;
            return new CppPointerType(Cursor.CanonicalCursor, elementTypeCanonical);
        }
    }
}