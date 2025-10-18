namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Model.Interfaces;
    using HexaGen.CppAst.Model.Types;
    using HexaGen.CppAst.Utilities;
    using System.Collections.Generic;

    public unsafe class TypeAliasDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_TypeAliasDecl,
                CXCursorKind.CXCursor_TypeAliasTemplateDecl
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var fulltypeDefName = Builder.GetCursorKey(cursor);
            if (TypedefResolver.TryResolve(fulltypeDefName, out var type))
            {
                return type;
            }

            var contextContainer = Builder.GetOrCreateDeclContainer(cursor.SemanticParent);

            var kind = cursor.Kind;

            CXCursor usedCursor = cursor;
            if (kind == CXCursorKind.CXCursor_TypeAliasTemplateDecl)
            {
                usedCursor = cursor.TemplatedDecl;
            }

            var underlyingTypeDefType = Builder.GetCppType(usedCursor.TypedefDeclUnderlyingType.Declaration, usedCursor.TypedefDeclUnderlyingType, usedCursor);
            var typedefName = CXUtil.GetCursorSpelling(usedCursor);

            if (Builder.AutoSquashTypedef && underlyingTypeDefType is ICppMember cppMember && (string.IsNullOrEmpty(cppMember.Name) || typedefName == cppMember.Name))
            {
                cppMember.Name = typedefName;
                type = (CppType)cppMember;
            }
            else
            {
                var typedef = new CppTypedef(typedefName, underlyingTypeDefType) { Visibility = contextContainer.CurrentVisibility };
                contextContainer.DeclarationContainer.Typedefs.Add(typedef);
                type = typedef;
            }

            Builder.ParseTypedefAttribute(cursor, type, underlyingTypeDefType);

            TypedefResolver.RegisterTypedef(fulltypeDefName, type);

            return type;
        }
    }
}