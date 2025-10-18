namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using System.Collections.Generic;

    public unsafe class CXXAccessSpecifierVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_CXXAccessSpecifier
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            var containerContext = Builder.GetOrCreateDeclContainer(parent, data);
            containerContext.CurrentVisibility = cursor.GetVisibility();
            return null;
        }
    }
}