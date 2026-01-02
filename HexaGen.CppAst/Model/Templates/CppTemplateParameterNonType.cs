// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Model.Types;
using System;

namespace HexaGen.CppAst.Model.Templates
{
    /// <summary>
    /// A C++ template parameter type.
    /// </summary>
    public sealed class CppTemplateParameterNonType : CppType
    {
        /// <summary>
        /// Constructor of this none type template parameter type.
        /// </summary>
        /// <param name="cursor"></param>
        /// <param name="name"></param>
        /// <param name="templateNonType"></param>
        public CppTemplateParameterNonType(CXCursor cursor, string name, CppType templateNonType) : base(cursor, CppTypeKind.TemplateParameterNonType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NoneTemplateType = templateNonType ?? throw new ArgumentNullException(nameof(templateNonType));
        }

        public CppTemplateParameterNonType(CX_TemplateArgument templateArgument, string name, CppType templateNonType) : base(CXCursor.Null, CppTypeKind.TemplateParameterNonType)
        {
            TemplateArgument = templateArgument;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NoneTemplateType = templateNonType ?? throw new ArgumentNullException(nameof(templateNonType));
        }

        public CX_TemplateArgument TemplateArgument { get; set; }

        /// <summary>
        /// Name of the template parameter.
        /// </summary>
        public string Name { get; }

        public CppType NoneTemplateType { get; }

        private bool Equals(CppTemplateParameterNonType other)
        {
            return base.Equals(other) && Name.Equals(other.Name) && NoneTemplateType.Equals(other.NoneTemplateType);
        }

        /// <inheritdoc />
        public override int SizeOf
        {
            get => 0;
            set => throw new InvalidOperationException("This type does not support SizeOf");
        }

        /// <inheritdoc />
        public override CppType GetCanonicalType() => this;

        /// <inheritdoc />
        public override string ToString() => $"{NoneTemplateType.ToString()} {Name}";
    }
}