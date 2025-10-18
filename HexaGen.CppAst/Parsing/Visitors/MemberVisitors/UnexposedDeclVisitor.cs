namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using System.Collections.Generic;

    public class UnexposedDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_UnexposedDecl];

        public override CXChildVisitResult VisitResult => CXChildVisitResult.CXChildVisit_Recurse;

        protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            return null;
        }
    }
}