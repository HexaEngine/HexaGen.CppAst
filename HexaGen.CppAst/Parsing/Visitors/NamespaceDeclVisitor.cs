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

        protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            var parentContainer = Builder.GetOrCreateDeclContainer(cursor.SemanticParent, data).GlobalDeclarationContainer;
            CppNamespace ns = new(CXUtil.GetCursorSpelling(cursor))
            {
                IsInlineNamespace = cursor.IsInlineNamespace
            };

            parentContainer.Namespaces.Add(ns);
            return new(ns);
        }
    }
}