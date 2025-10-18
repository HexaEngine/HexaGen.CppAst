using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Declarations;

namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    public class NamespaceVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_Namespace];

        protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            var ns = Builder.GetOrCreateDeclarationContainer<CppNamespace>(cursor, data, out var context);
            Builder.ParseAttributes(cursor, ns, false);
            cursor.VisitChildren(Builder.VisitMember, new CXClientData((nint)data));
            return ns;
        }
    }
}