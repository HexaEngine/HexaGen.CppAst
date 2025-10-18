namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using System.Collections.Generic;

    internal class ObjCIvarDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_ObjCIvarDecl];

        protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            return null;
        }
    }
}