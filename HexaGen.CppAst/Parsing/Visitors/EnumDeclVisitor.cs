namespace HexaGen.CppAst.Parsing.Visitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Model.Interfaces;
    using HexaGen.CppAst.Parsing;
    using HexaGen.CppAst.Utilities;
    using System.Collections.Generic;

    public class EnumDeclVisitor : DeclContainerVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_EnumDecl];

        protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
        {
            var parentContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent).DeclarationContainer;
            CppEnum cppEnum = new(cursor, CXUtil.GetCursorSpelling(cursor))
            {
                IsAnonymous = cursor.IsAnonymous,
                Visibility = cursor.GetVisibility()
            };

            parentContainer.Enums.Add(cppEnum);
            return new(cppEnum);
        }
    }
}