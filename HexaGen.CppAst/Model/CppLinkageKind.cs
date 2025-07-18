﻿// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace HexaGen.CppAst.Model
{
    /// <summary>
    /// Type of linkage.
    /// </summary>
    public enum CppLinkageKind
    {
        Invalid,
        NoLinkage,
        Internal,
        UniqueExternal,
        External,
    }
}