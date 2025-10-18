// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Collections;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Declarations;
using HexaGen.CppAst.Model.Expressions;
using HexaGen.CppAst.Model.Interfaces;
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
    public unsafe partial class CppModelBuilder : CompilationLoggerBase
    {
        private readonly CppModelContext context;
        private readonly CppContainerContext userRootContainerContext;
        private readonly CppContainerContext systemRootContainerContext;
        private CppContainerContext rootContainerContext = null!;
        private readonly Dictionary<CursorKey, CppContainerContext> containers;
        private readonly TypedefResolver typedefResolver = new();
        private readonly Dictionary<CursorKey, CppTemplateParameterType> objCTemplateParameterTypes;

        public CppModelBuilder()
        {
            containers = [];
            RootCompilation = new();
            objCTemplateParameterTypes = [];
            userRootContainerContext = new(RootCompilation, CppContainerContextType.User, CppVisibility.Default);
            systemRootContainerContext = new(RootCompilation.System, CppContainerContextType.System, CppVisibility.Default);
            context = new CppModelContext(containers, RootCompilation, this);
        }

        public bool AutoSquashTypedef { get; set; }

        public bool ParseSystemIncludes { get; set; }

        public bool ParseTokenAttributeEnabled { get; set; }

        public bool ParseCommentAttributeEnabled { get; set; }

        public override CppCompilation RootCompilation { get; }

        public CppContainerContext CurrentRootContainer => rootContainerContext;

        public TypedefResolver TypedefResolver => typedefResolver;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CursorKey GetCursorKey(CXCursor cursor)
        {
            return new(rootContainerContext, cursor);
        }

        public CppContainerContext GetOrCreateDeclContainer(CXCursor cursor)
        {
            while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
            {
                cursor = cursor.SemanticParent;
            }

            var typeKey = GetCursorKey(cursor);
            if (containers.TryGetValue(typeKey, out var containerContext))
            {
                return containerContext;
            }

            var visitor = DeclContainerVisitorRegistry.GetVisitor(cursor.Kind);
            containerContext = visitor.Visit(context, cursor, cursor.SemanticParent);
            containers.TryAdd(typeKey, containerContext);
            return containerContext;
        }

        public CppTemplateParameterType TryToCreateTemplateParametersObjC(CXCursor cursor)
        {
            if (cursor.Kind != CXCursorKind.CXCursor_TemplateTypeParameter)
            {
                throw new InvalidOperationException("Only CXCursor_TemplateTypeParameter is supported here");
            }

            var key = GetCursorKey(cursor);
            if (!objCTemplateParameterTypes.TryGetValue(key, out var templateParameterType))
            {
                var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                templateParameterType = new(templateParameterName);
                objCTemplateParameterTypes.Add(key, templateParameterType);
            }
            return templateParameterType;
        }

        public CppType? TryToCreateTemplateParameters(CXCursor cursor)
        {
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_TemplateTypeParameter:
                    {
                        var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                        CppTemplateParameterType templateParameterType = new(templateParameterName);
                        return templateParameterType;
                    }
                case CXCursorKind.CXCursor_NonTypeTemplateParameter:
                    {
                        var type = cursor.Type;
                        var cppType = GetCppType(type.Declaration, type, cursor);
                        var name = CXUtil.GetCursorSpelling(cursor);

                        CppTemplateParameterNonType templateParameterType = new(name, cppType);

                        return templateParameterType;
                    }
                case CXCursorKind.CXCursor_TemplateTemplateParameter:
                    {
                        // TODO: add template template parameter support here
                        RootCompilation.Diagnostics.Warning($"Unhandled template parameter: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                        var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                        CppTemplateParameterType templateParameterType = new(templateParameterName);
                        return templateParameterType;
                    }
            }

            return null;
        }

        public TCppElement GetOrCreateDeclarationContainer<TCppElement>(CXCursor cursor, out CppContainerContext context) where TCppElement : CppElement, ICppContainer
        {
            context = GetOrCreateDeclContainer(cursor);
            if (context.Container is TCppElement typedCppElement)
            {
                return typedCppElement;
            }
            throw new InvalidOperationException($"The element `{context.Container}` doesn't match the expected type `{typeof(TCppElement)}");
        }

        public CXChildVisitResult VisitTranslationUnit(CXCursor cursor, CXCursor parent, void* data)
        {
            var result = VisitMember(cursor, parent, data);
            return result;
        }

        public CppClass VisitClassDecl(CXCursor cursor)
        {
            var cppStruct = GetOrCreateDeclarationContainer<CppClass>(cursor, out var context);
            if (cursor.IsCursorDefinition(cppStruct) && !context.IsChildrenVisited)
            {
                ParseAttributes(cursor, cppStruct, false);
                cppStruct.IsDefinition = true;
                cppStruct.SizeOf = (int)cursor.Type.SizeOf;
                cppStruct.AlignOf = (int)cursor.Type.AlignOf;
                context.IsChildrenVisited = true;
                var saveCurrentClassBeingVisited = this.context.CurrentClassBeingVisited;
                this.context.CurrentClassBeingVisited = cppStruct;
                cursor.VisitChildren(VisitMember, default);

                if (cppStruct.Properties.Count > 0)
                {
                    foreach (var prop in cppStruct.Properties)
                    {
                        prop.Getter = cppStruct.Functions.FindByName(prop.GetterName);
                        prop.Setter = cppStruct.Functions.FindByName(prop.SetterName);
                    }
                }

                cppStruct.AssignSourceSpan(cursor);

                this.context.CurrentClassBeingVisited = saveCurrentClassBeingVisited;
            }
            return cppStruct;
        }

        public void VisitInitValue(CXCursor cursor, out CppExpression? expression, out CppValue? value)
        {
            expression = null;
            cursor.VisitChildren(static (initCursor, varCursor, clientData) =>
            {
                ref CppExpression? expression = ref Unsafe.AsRef<CppExpression?>(clientData);
                if (initCursor.IsExpression())
                {
                    expression = VisitExpression(initCursor, clientData);
                    return CXChildVisitResult.CXChildVisit_Break;
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, (CXClientData)Unsafe.AsPointer(ref expression));

            using CXEvalResult resultEval = cursor.Evaluate;
            switch (resultEval.Kind)
            {
                case CXEvalResultKind.CXEval_Int:
                    value = new(resultEval.AsLongLong);
                    break;

                case CXEvalResultKind.CXEval_Float:
                    value = new(resultEval.AsDouble);
                    break;

                case CXEvalResultKind.CXEval_ObjCStrLiteral:
                case CXEvalResultKind.CXEval_StrLiteral:
                case CXEvalResultKind.CXEval_CFStr:
                    value = new(resultEval.AsStr);
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

        private CppType VisitElaboratedDecl(CXCursor cursor, CXType type, CXCursor parent)
        {
            var key = GetCursorKey(cursor);
            if (typedefResolver.TryResolve(key, out var typeRef))
            {
                return typeRef;
            }

            // If the type has been already declared, return it immediately.
            if (containers.TryGetValue(key, out var containerContext))
            {
                return (CppType)containerContext.Container;
            }

            // TODO: Pseudo fix, we are not supposed to land here, as the TryGet before should resolve an existing type already declared (but not necessarily defined)
            return GetCppType(type.CanonicalType.Declaration, type.CanonicalType, parent);
        }
    }
}