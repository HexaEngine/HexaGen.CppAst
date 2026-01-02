using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Declarations;
using HexaGen.CppAst.Utilities;
using System;

namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    public unsafe class FieldVariableVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_FieldDecl, CXCursorKind.CXCursor_VarDecl];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var containerContext = Context.GetOrCreateDeclContainer(parent);
            var fieldName = CXUtil.GetCursorSpelling(cursor);
            var type = Builder.GetCppType(cursor.Type.Declaration, cursor.Type, cursor);

            var previousField = containerContext.DeclarationContainer.Fields.LastOrDefault();
            CppField cppField;

            // This happen in the type is anonymous, we create implicitly a field for it, but if type is the same
            // we should reuse the anonymous field we created just before
            if (previousField != null && previousField.IsAnonymous && type.IsAnonymousTypeUsed(previousField.Type))
            {
                cppField = previousField;
                cppField.Name = fieldName;
                cppField.Type = type;
                cppField.BitOffset = cursor.OffsetOfField;
            }
            else
            {
                cppField = new(cursor, type, fieldName)
                {
                    Visibility = cursor.GetVisibility(),
                    StorageQualifier = cursor.GetStorageQualifier(),
                    IsBitField = cursor.IsBitField,
                    BitFieldWidth = cursor.FieldDeclBitWidth,
                    BitOffset = cursor.OffsetOfField,
                };
                containerContext.DeclarationContainer.Fields.Add(cppField);
                Builder.ParseAttributes(cursor, cppField, true);

                if (cursor.Kind == CXCursorKind.CXCursor_VarDecl)
                {
                    Builder.VisitInitValue(cursor, out var fieldExpr, out var fieldValue);
                    cppField.InitValue = fieldValue;
                    cppField.InitExpression = fieldExpr;
                }
            }

            return cppField;
        }
    }
}