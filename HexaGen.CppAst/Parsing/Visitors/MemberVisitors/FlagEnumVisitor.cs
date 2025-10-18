namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Attributes;
    using HexaGen.CppAst.Model.Declarations;
    using System.Collections.Generic;

    public unsafe class FlagEnumVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_FlagEnum
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var containerContext = Context.GetOrCreateDeclContainer(parent);
            var cppEnum = (CppEnum)containerContext.Container;
            cppEnum.Attributes.Add(new CppAttribute("flag_enum", AttributeKind.ObjectiveCAttribute));
            return null;
        }
    }
}