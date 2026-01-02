namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Collections;
    using HexaGen.CppAst.Model;
    using System.Collections.Generic;

    public unsafe class InclusionDirectiveVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_InclusionDirective
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var file = cursor.IncludedFile;
            CppInclusionDirective inclusionDirective = new(cursor, Path.GetFullPath(file.Name.ToString()));
            var rootContainer = (CppGlobalDeclarationContainer)CurrentRootContainer.DeclarationContainer;
            rootContainer.InclusionDirectives.Add(inclusionDirective);
            return inclusionDirective;
        }
    }
}