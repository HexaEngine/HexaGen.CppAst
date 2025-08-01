﻿// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.


// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using HexaGen.CppAst.Model.Interfaces;
using HexaGen.CppAst.Model.Metadata;

namespace HexaGen.CppAst.Model.Declarations
{
    /// <summary>
    /// Base class for any declaration that is not a type (<see cref="CppTypeDeclaration"/>)
    /// </summary>
    public abstract class CppDeclaration : CppElement, ICppDeclaration
    {
        /// <summary>
        /// Gets or sets the comment attached to this element. Might be null.
        /// </summary>
        public CppComment Comment { get; set; }
    }
}