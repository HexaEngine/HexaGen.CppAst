namespace HexaGen.CppAst.Parsing.Visitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Parsing;
    using HexaGen.CppAst.Utilities;
    using System.Collections.Generic;

    public class NamespaceDeclVisitor : DeclContainerVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_Namespace];

        protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
        {
            var parentContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent).GlobalDeclarationContainer;
            CppNamespace ns = new(cursor, CXUtil.GetCursorSpelling(cursor))
            {
                IsInlineNamespace = cursor.IsInlineNamespace
            };

            parentContainer.Namespaces.Add(ns);
            return new(ns);
        }
    }
}