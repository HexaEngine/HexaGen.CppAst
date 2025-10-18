namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Declarations;
    using System.Collections.Generic;

    public unsafe class EnumDeclMemberVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_EnumDecl
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var cppEnum = Builder.GetOrCreateDeclarationContainer<CppEnum>(cursor, out var context);
            if (cursor.IsDefinition && !context.IsChildrenVisited)
            {
                var integralType = cursor.EnumDecl_IntegerType;
                cppEnum.IntegerType = Builder.GetCppType(integralType.Declaration, integralType, cursor);
                cppEnum.IsScoped = cursor.EnumDecl_IsScoped;
                Builder.ParseAttributes(cursor, cppEnum);
                context.IsChildrenVisited = true;
                cursor.VisitChildren(Builder.VisitMember, default);
            }
            return cppEnum;
        }
    }
}