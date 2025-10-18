namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Model.Types;
    using System.Collections.Generic;

    public unsafe class ObjCClassProtocolRefVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_ObjCClassRef,
                CXCursorKind.CXCursor_ObjCProtocolRef
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var objCContainer = Context.GetOrCreateDeclContainer(parent).Container;
            if (objCContainer is CppClass cppClass && cppClass.ClassKind != CppClassKind.ObjCInterfaceCategory)
            {
                var referencedType = (CppClass)Context.GetOrCreateDeclContainer(cursor.Referenced).Container;
                if (cursor.Kind == CXCursorKind.CXCursor_ObjCClassRef)
                {
                    var cppBaseType = new CppBaseType(referencedType);
                    cppClass.BaseTypes.Add(cppBaseType);
                }
                else
                {
                    cppClass.ObjCImplementedProtocols.Add(referencedType);
                }
            }
            return null;
        }
    }
}