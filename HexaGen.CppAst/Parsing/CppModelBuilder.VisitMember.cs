namespace HexaGen.CppAst.Parsing
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Interfaces;

    public unsafe partial class CppModelBuilder
    {
        public CXChildVisitResult VisitMember(CXCursor cursor, CXCursor parent, void* data)
        {
            CppElement? element = null;

            // Only set the root container when we know the location
            // Otherwise assume that it hasn't changed
            // We expect it to be always set
            if (cursor.Location != CXSourceLocation.Null)
            {
                if (cursor.Location.IsInSystemHeader)
                {
                    if (!ParseSystemIncludes) return CXChildVisitResult.CXChildVisit_Continue;

                    rootContainerContext = systemRootContainerContext;
                }
                else
                {
                    rootContainerContext = userRootContainerContext;
                }
            }

            if (rootContainerContext is null)
            {
                RootCompilation.Diagnostics.Error($"Unexpected error with cursor location. Cannot determine Root Compilation context.");
                return CXChildVisitResult.CXChildVisit_Continue;
            }

            var visitor = MemberVisitorRegistry.GetVisitor(cursor.Kind);
            if (visitor != null)
            {
                element = visitor.Visit(context, cursor, parent, data);
            }
            else
            {
                if (!cursor.IsAttribute)
                {
                    WarningUnhandled(cursor, parent);
                }
            }

            if (element == null)
            {
                return CXChildVisitResult.CXChildVisit_Continue;
            }

            if (element.SourceFile is null || IsCursorDefinition(cursor, element))
            {
                element.AssignSourceSpan(cursor);
            }

            if (element is ICppDeclaration cppDeclaration)
            {
                cppDeclaration.Comment = cursor.GetComment();

                if (cppDeclaration is ICppAttributeContainer attrContainer && ParseCommentAttributeEnabled)
                {
                    cppDeclaration.Comment?.TryToParseAttributes(attrContainer);
                }
            }

            element.ConvertToMetaAttributes();

            return visitor!.VisitResult;
        }
    }
}