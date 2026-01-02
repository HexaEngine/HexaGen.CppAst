using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Declarations;
using HexaGen.CppAst.Utilities;

namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    public class EnumConstantVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_EnumConstantDecl];

        protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
        {
            var containerContext = Context.GetOrCreateDeclContainer(parent);
            var cppEnum = (CppEnum)containerContext.Container;
            var enumItem = new CppEnumItem(cursor, CXUtil.GetCursorSpelling(cursor), cursor.EnumConstantDeclValue);
            Builder.ParseAttributes(cursor, enumItem, true);

            Builder.VisitInitValue(cursor, out var enumItemExpression, out var enumValue);
            enumItem.ValueExpression = enumItemExpression;

            cppEnum.Items.Add(enumItem);
            return enumItem;
        }
    }
}