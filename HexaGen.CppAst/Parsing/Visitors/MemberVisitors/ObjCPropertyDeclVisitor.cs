namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Utilities;
    using System.Collections.Generic;

    public unsafe class ObjCPropertyDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_ObjCPropertyDecl
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            var containerContext = Builder.GetOrCreateDeclContainer(parent, data);
            var propertyName = CXUtil.GetCursorSpelling(cursor);
            var type = Builder.GetCppType(cursor.Type.Declaration, cursor.Type, cursor, data);

            var cppProperty = new CppProperty(type, propertyName);
            cppProperty.GetterName = cursor.ObjCPropertyGetterName.ToString();
            cppProperty.SetterName = cursor.ObjCPropertySetterName.ToString();
            Builder.ParseAttributes(cursor, cppProperty, true);
            containerContext.DeclarationContainer.Properties.Add(cppProperty);
            return cppProperty;
        }
    }
}