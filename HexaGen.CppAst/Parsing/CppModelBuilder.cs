// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Expressions;
using HexaGen.CppAst.Model.Metadata;
using HexaGen.CppAst.Model.Templates;
using HexaGen.CppAst.Model.Types;
using HexaGen.CppAst.Utilities;
using System.Runtime.CompilerServices;

namespace HexaGen.CppAst.Parsing
{
    /// <summary>
    /// Internal class used to build the entire C++ model from the libclang representation.
    /// </summary>
    public partial class CppModelBuilder : CompilationLoggerBase
    {
        private readonly CppModelContext context;

        public CppModelBuilder(CXTranslationUnit translationUnit)
        {
            context = new CppModelContext(this, translationUnit);
        }

        public bool AutoSquashTypedef { get; set; }

        public bool ParseSystemIncludes { get; set; }

        public bool ParseTokenAttributeEnabled { get; set; }

        public bool ParseCommentAttributeEnabled { get; set; }

        public override CppCompilation RootCompilation => context.RootCompilation;

        public Dictionary<CursorKey, CppContainerContext> Containers => context.Containers;

        public CppContainerContext CurrentRootContainer => context.CurrentRootContainer;

        public TypedefResolver TypedefResolver => context.TypedefResolver;

        public CppType? TryToCreateTemplateParameters(CXCursor cursor)
        {
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_TemplateTypeParameter:
                    {
                        var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                        CppTemplateParameterType templateParameterType = new(cursor, templateParameterName);
                        return templateParameterType;
                    }
                case CXCursorKind.CXCursor_NonTypeTemplateParameter:
                    {
                        var type = cursor.Type;
                        var cppType = GetCppType(type.Declaration, type, cursor);
                        var name = CXUtil.GetCursorSpelling(cursor);

                        CppTemplateParameterNonType templateParameterType = new(cursor, name, cppType);

                        return templateParameterType;
                    }
                case CXCursorKind.CXCursor_TemplateTemplateParameter:
                    {
                        // TODO: add template template parameter support here
                        RootCompilation.Diagnostics.Warning($"Unhandled template parameter: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                        var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                        CppTemplateParameterType templateParameterType = new(cursor, templateParameterName);
                        return templateParameterType;
                    }
            }

            return null;
        }

        public unsafe CXChildVisitResult VisitTranslationUnit(CXCursor cursor, CXCursor parent, void* data)
        {
            var result = VisitMember(cursor, parent, data);
            return result;
        }

        public unsafe void VisitInitValue(CXCursor cursor, out CppExpression? expression, out CppValue? value)
        {
            expression = null;
            cursor.VisitChildren(static (initCursor, varCursor, clientData) =>
            {
                ref CppExpression? expression = ref Unsafe.AsRef<CppExpression?>(clientData);
                if (initCursor.IsExpression())
                {
                    expression = VisitExpression(initCursor);
                    return CXChildVisitResult.CXChildVisit_Break;
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, (CXClientData)Unsafe.AsPointer(ref expression));

            using CXEvalResult resultEval = cursor.Evaluate;
            switch (resultEval.Kind)
            {
                case CXEvalResultKind.CXEval_Int:
                    value = new(cursor, resultEval.AsLongLong);
                    break;

                case CXEvalResultKind.CXEval_Float:
                    value = new(cursor, resultEval.AsDouble);
                    break;

                case CXEvalResultKind.CXEval_ObjCStrLiteral:
                case CXEvalResultKind.CXEval_StrLiteral:
                case CXEvalResultKind.CXEval_CFStr:
                    value = new(cursor, resultEval.AsStr);
                    break;

                case CXEvalResultKind.CXEval_UnExposed:
                    value = null;
                    break;

                default:
                    value = null;
                    RootCompilation.Diagnostics.Warning($"Not supported field default value {CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                    break;
            }
        }
    }
}