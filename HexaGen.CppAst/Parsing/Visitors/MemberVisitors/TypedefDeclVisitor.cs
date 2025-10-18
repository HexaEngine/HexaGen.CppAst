﻿namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Model.Interfaces;
    using HexaGen.CppAst.Model.Types;
    using HexaGen.CppAst.Utilities;
    using System.Collections.Generic;

    public unsafe class TypedefDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_TypedefDecl
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var fulltypeDefName = Context.GetCursorKey(cursor);
            if (TypedefResolver.TryResolve(fulltypeDefName, out var type))
            {
                return type;
            }

            var contextContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent);
            Context.CurrentTypedefKey = fulltypeDefName;
            var underlyingTypeDefType = Builder.GetCppType(cursor.TypedefDeclUnderlyingType.Declaration, cursor.TypedefDeclUnderlyingType, cursor);
            Context.CurrentTypedefKey = default;

            var typedefName = CXUtil.GetCursorSpelling(cursor);

            ICppDeclarationContainer? container = null;

            if (Builder.AutoSquashTypedef && underlyingTypeDefType is ICppMember cppMember && (string.IsNullOrEmpty(cppMember.Name) || typedefName == cppMember.Name))
            {
                cppMember.Name = typedefName;
                type = (CppType)cppMember;
            }
            else
            {
                var typedef = new CppTypedef(typedefName, underlyingTypeDefType) { Visibility = contextContainer.CurrentVisibility };
                container = contextContainer.DeclarationContainer;
                type = typedef;
            }

            Builder.ParseTypedefAttribute(cursor, type, underlyingTypeDefType);

            // The type could have been added separately as part of the GetCppType above
            TypedefResolver.RegisterTypedef(fulltypeDefName, type);

            var map = Context.MapTemplateParameterTypeToTypedefKeys;

            // Try to remap typedef using a parameter type declared in an ObjC interface
            if (map.Count > 0)
            {
                foreach (var pair in map.ToList())
                {
                    if (pair.Value.Contains(fulltypeDefName))
                    {
                        container = (ICppDeclarationContainer?)pair.Key.Parent;
                        map.Remove(pair.Key);
                        break;
                    }
                }
            }

            container?.Typedefs.Add((CppTypedef)type);

            // Update Span
            if (type is CppElement element)
            {
                element.AssignSourceSpan(cursor);
                if (element is CppTypedef typedef && typedef.ElementType is CppClass && string.IsNullOrWhiteSpace(typedef.ElementType.SourceFile))
                {
                    typedef.ElementType.Span = element.Span;
                }
            }

            return type;
        }
    }
}