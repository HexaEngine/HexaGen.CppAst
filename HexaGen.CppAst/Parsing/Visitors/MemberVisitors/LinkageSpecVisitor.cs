namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using System.Collections.Generic;

    public unsafe class LinkageSpecVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_LinkageSpec
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            cursor.VisitChildren(Builder.VisitMember, new CXClientData((nint)data));
            return null;
        }
    }
}