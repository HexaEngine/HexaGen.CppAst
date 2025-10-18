﻿namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Types;
    using System.Collections.Generic;

    public unsafe class TypeRefVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_TypeRef
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            if (Context.CurrentClassBeingVisited != null && Context.CurrentClassBeingVisited.BaseTypes.Count == 1)
            {
                var baseType = Context.CurrentClassBeingVisited.BaseTypes[0].Type;
                CppGenericType genericType = baseType as CppGenericType ?? new CppGenericType(baseType);
                var type = Builder.GetCppType(cursor.Referenced, cursor.Type, cursor);
                genericType.GenericArguments.Add(type);
            }
            return null;
        }
    }
}