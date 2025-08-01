// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using HexaGen.CppAst.Model.Types;
using System;

namespace HexaGen.CppAst.Model.Templates
{
    /// <summary>
    /// A C++ template parameter type.
    /// </summary>
    public sealed class CppTemplateParameterType : CppType
    {
        /// <summary>
        /// Constructor of this template parameter type.
        /// </summary>
        /// <param name="name"></param>
        public CppTemplateParameterType(string name) : base(CppTypeKind.TemplateParameterType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>
        /// Name of the template parameter.
        /// </summary>
        public string Name { get; }

        private bool Equals(CppTemplateParameterType other)
        {
            return base.Equals(other) && Name.Equals(other.Name);
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
        public override string ToString() => Name;
    }
}