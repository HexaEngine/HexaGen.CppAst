// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Model.Interfaces;
using HexaGen.CppAst.Model.Metadata;
using HexaGen.CppAst.Model.Types;

namespace HexaGen.CppAst.Model.Declarations
{
    /// <summary>
    /// Base class for a type declaration (<see cref="CppClass"/>, <see cref="CppEnum"/>, <see cref="CppFunctionType"/> or <see cref="CppTypedef"/>)
    /// </summary>
    public abstract class CppTypeDeclaration : CppType, ICppDeclaration, ICppContainer
    {
        protected CppTypeDeclaration(CXCursor cursor, CppTypeKind typeKind) : base(cursor, typeKind)
        {
        }

        /// <inheritdoc />
        public CppComment? Comment { get; set; }

        /// <inheritdoc />
        public virtual IEnumerable<ICppDeclaration> Children => [];
    }
}