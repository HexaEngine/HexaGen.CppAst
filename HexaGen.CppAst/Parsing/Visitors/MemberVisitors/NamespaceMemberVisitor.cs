using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Declarations;

namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    public class NamespaceMemberVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_Namespace];

        protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var ns = Builder.GetOrCreateDeclarationContainer<CppNamespace>(cursor, out var context);
            Builder.ParseAttributes(cursor, ns, false);
            cursor.VisitChildren(Builder.VisitMember, default);
            return ns;
        }
    }
}