// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.
using HexaGen.CppAst.AttributeUtils;
using HexaGen.CppAst.Model.Attributes;
using System;
using System.Collections.Generic;

namespace HexaGen.CppAst.Model.Interfaces
{
    /// <summary>
    /// Base interface for all with attribute element.
    /// </summary>
    public interface ICppAttributeContainer
    {
        /// <summary>
        /// Gets the attributes from element.
        /// </summary>
        List<CppAttribute> Attributes { get; }

        [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
        List<CppAttribute> TokenAttributes { get; }

        MetaAttributeMap MetaAttributes { get; }
    }
}