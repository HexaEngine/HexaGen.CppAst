namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using System.Collections.Generic;

    public unsafe class EnumDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_EnumDecl
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            return Builder.VisitEnumDecl(cursor, data);
        }
    }
}