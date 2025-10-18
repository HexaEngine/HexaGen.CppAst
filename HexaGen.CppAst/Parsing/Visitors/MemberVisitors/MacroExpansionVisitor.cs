namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class MacroExpansionVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_MacroExpansion];

        protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            return null;
        }
    }
}