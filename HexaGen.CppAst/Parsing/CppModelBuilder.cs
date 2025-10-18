// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Declarations;
using HexaGen.CppAst.Model.Expressions;
using HexaGen.CppAst.Model.Interfaces;
using HexaGen.CppAst.Model.Metadata;
using HexaGen.CppAst.Model.Templates;
using HexaGen.CppAst.Model.Types;
using HexaGen.CppAst.Parsing.Visitors.MemberVisitors;
using HexaGen.CppAst.Utilities;
using System.Diagnostics.CodeAnalysis;
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

        public CppContainerContext GetOrCreateDeclContainer(CXCursor cursor, void* data)
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
            containerContext = visitor.Visit(context, cursor, cursor.SemanticParent, data);
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

        public CppType? TryToCreateTemplateParameters(CXCursor cursor, void* data)
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
                        // Just use low level ClangSharp object to do the logic
                        var tmptype = cursor.Type;
                        var tmpcpptype = GetCppType(tmptype.Declaration, tmptype, cursor, data);
                        var tmpname = CXUtil.GetCursorSpelling(cursor);

                        CppTemplateParameterNonType templateParameterType = new(tmpname, tmpcpptype);

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

        public bool TryGetDeclarationContainer(CXCursor cursor, out CursorKey typeKey, [NotNullWhen(true)] out CppContainerContext? containerContext)
        {
            typeKey = GetCursorKey(cursor);
            return containers.TryGetValue(typeKey, out containerContext);
        }

        public void AddTemplateParameters(CXCursor cursor, CppClass cppClass)
        {
            cursor.VisitChildren((childCursor, classCursor, clientData) =>
            {
                if (cppClass.ClassKind == CppClassKind.ObjCInterface ||
                    cppClass.ClassKind == CppClassKind.ObjCProtocol)
                {
                    var tmplParam = TryToCreateTemplateParametersObjC(childCursor);
                    if (tmplParam != null)
                    {
                        cppClass.TemplateKind = CppTemplateKind.ObjCGenericClass;
                        cppClass.TemplateParameters.Add(tmplParam);
                    }
                }
                else
                {
                    var tmplParam = TryToCreateTemplateParameters(childCursor, clientData);
                    if (tmplParam != null)
                    {
                        cppClass.TemplateKind = CppTemplateKind.TemplateClass;
                        cppClass.TemplateParameters.Add(tmplParam);
                    }
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            }, default);
        }

        public TCppElement GetOrCreateDeclarationContainer<TCppElement>(CXCursor cursor, void* data, out CppContainerContext context) where TCppElement : CppElement, ICppContainer
        {
            context = GetOrCreateDeclContainer(cursor, data);
            if (context.Container is TCppElement typedCppElement)
            {
                return typedCppElement;
            }
            throw new InvalidOperationException($"The element `{context.Container}` doesn't match the expected type `{typeof(TCppElement)}");
        }

        public CXChildVisitResult VisitTranslationUnit(CXCursor cursor, CXCursor parent, void* data)
        {
            var result = VisitMember(cursor, parent, data);
            //Debug.Assert(_mapTemplateParameterTypeToTypedefKeys.Count == 0);
            return result;
        }

        public CppClass VisitClassDecl(CXCursor cursor, void* data)
        {
            var cppStruct = GetOrCreateDeclarationContainer<CppClass>(cursor, data, out var context);
            if (IsCursorDefinition(cursor, cppStruct) && !context.IsChildrenVisited)
            {
                ParseAttributes(cursor, cppStruct, false);
                cppStruct.IsDefinition = true;
                cppStruct.SizeOf = (int)cursor.Type.SizeOf;
                cppStruct.AlignOf = (int)cursor.Type.AlignOf;
                context.IsChildrenVisited = true;
                var saveCurrentClassBeingVisited = this.context.CurrentClassBeingVisited;
                this.context.CurrentClassBeingVisited = cppStruct;
                cursor.VisitChildren(VisitMember, new CXClientData((nint)data));

                // Resolve getter/setter methods
                if (cppStruct.Properties.Count > 0)
                {
                    foreach (var prop in cppStruct.Properties)
                    {
                        // Search getter / setter methods
                        prop.Getter = cppStruct.Functions.FirstOrDefault(m => m.Name == prop.GetterName);
                        prop.Setter = cppStruct.Functions.FirstOrDefault(m => m.Name == prop.SetterName);
                    }
                }

                // Force assign source span as early as possible
                cppStruct.AssignSourceSpan(cursor);

                this.context.CurrentClassBeingVisited = saveCurrentClassBeingVisited;
            }
            return cppStruct;
        }

        private static bool IsCursorDefinition(CXCursor cursor, CppElement element)
        {
            return cursor.IsDefinition || element is CppInclusionDirective || element is CppClass cppClass && (cppClass.ClassKind == CppClassKind.ObjCInterface ||
                                                                                                                 cppClass.ClassKind == CppClassKind.ObjCProtocol ||
                                                                                                                 cppClass.ClassKind == CppClassKind.ObjCInterfaceCategory)
                ;
        }

        private void AddAnonymousTypeWithField(CppContainerContext containerContext, CXCursor cursor, CppType fieldType)
        {
            var fieldName = "__anonymous__" + containerContext.DeclarationContainer.Fields.Count;
            var cppField = new CppField(fieldType, fieldName)
            {
                Visibility = cursor.GetVisibility(),
                StorageQualifier = cursor.GetStorageQualifier(),
                IsAnonymous = true,
                BitOffset = cursor.OffsetOfField,
            };
            containerContext.DeclarationContainer.Fields.Add(cppField);
            ParseAttributes(cursor, cppField, true);
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

        private CppType VisitElaboratedDecl(CXCursor cursor, CXType type, CXCursor parent, void* data)
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
            return GetCppType(type.CanonicalType.Declaration, type.CanonicalType, parent, data);
        }

        public CppType GetCppType(CXCursor cursor, CXType type, CXCursor parent, void* data)
        {
            var cppType = GetCppTypeInternal(cursor, type, parent, data);

            if (type.IsConstQualified)
            {
                // Skip if it is already qualified.
                if (cppType is CppUnexposedType || cppType is CppQualifiedType q && q.Qualifier == CppTypeQualifier.Const)
                {
                    return cppType;
                }

                return new CppQualifiedType(CppTypeQualifier.Const, cppType);
            }
            else if (type.IsVolatileQualified)
            {
                // Skip if it is already qualified.
                if (cppType is CppQualifiedType q && q.Qualifier == CppTypeQualifier.Volatile)
                {
                    return cppType;
                }

                return new CppQualifiedType(CppTypeQualifier.Volatile, cppType);
            }

            return cppType;
        }

        private CppType GetCppTypeInternal(CXCursor cursor, CXType type, CXCursor parent, void* data)
        {
            switch (type.kind)
            {
                case CXTypeKind.CXType_Void:
                    return CppPrimitiveType.Void;

                case CXTypeKind.CXType_Bool:
                    return CppPrimitiveType.Bool;

                case CXTypeKind.CXType_UChar:
                    return CppPrimitiveType.UnsignedChar;

                case CXTypeKind.CXType_UShort:
                    return CppPrimitiveType.UnsignedShort;

                case CXTypeKind.CXType_UInt:
                    return CppPrimitiveType.UnsignedInt;

                case CXTypeKind.CXType_ULong:
                    return CppPrimitiveType.UnsignedLong;

                case CXTypeKind.CXType_ULongLong:
                    return CppPrimitiveType.UnsignedLongLong;

                case CXTypeKind.CXType_SChar:
                    return CppPrimitiveType.Char;

                case CXTypeKind.CXType_Char_S:
                    return CppPrimitiveType.Char;

                case CXTypeKind.CXType_WChar:
                    return CppPrimitiveType.WChar;

                case CXTypeKind.CXType_Short:
                    return CppPrimitiveType.Short;

                case CXTypeKind.CXType_Int:
                    return CppPrimitiveType.Int;

                case CXTypeKind.CXType_Long:
                    return CppPrimitiveType.Long;

                case CXTypeKind.CXType_LongLong:
                    return CppPrimitiveType.LongLong;

                case CXTypeKind.CXType_Float:
                    return CppPrimitiveType.Float;

                case CXTypeKind.CXType_Double:
                    return CppPrimitiveType.Double;

                case CXTypeKind.CXType_LongDouble:
                    return CppPrimitiveType.LongDouble;

                case CXTypeKind.CXType_ObjCObjectPointer:
                case CXTypeKind.CXType_Pointer:
                    return new CppPointerType(GetCppType(type.PointeeType.Declaration, type.PointeeType, parent, data)) { SizeOf = (int)type.SizeOf };

                case CXTypeKind.CXType_LValueReference:
                    return new CppReferenceType(GetCppType(type.PointeeType.Declaration, type.PointeeType, parent, data));

                case CXTypeKind.CXType_Record:
                    return VisitClassDecl(cursor, data);

                case CXTypeKind.CXType_ObjCInterface:
                    return VisitClassDecl(cursor, data);

                case CXTypeKind.CXType_Enum:
                    return (CppType)MemberVisitorRegistry.GetVisitor<EnumDeclMemberVisitor>().Visit(context, cursor, parent, data)!;

                case CXTypeKind.CXType_FunctionProto:
                    return VisitFunctionType(cursor, type, parent, data);

                case CXTypeKind.CXType_BlockPointer:
                    return VisitBlockFunctionType(cursor, type, parent, data);

                case CXTypeKind.CXType_Typedef:
                    return (CppType)MemberVisitorRegistry.GetVisitor<TypedefDeclVisitor>().Visit(context, cursor, parent, data)!;

                case CXTypeKind.CXType_Elaborated:
                    return VisitElaboratedDecl(cursor, type, parent, data);

                case CXTypeKind.CXType_ConstantArray:
                case CXTypeKind.CXType_IncompleteArray:
                    {
                        var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent, data);
                        return new CppArrayType(elementType, (int)type.ArraySize);
                    }

                case CXTypeKind.CXType_DependentSizedArray:
                    {
                        // TODO: this is not yet supported
                        RootCompilation.Diagnostics.Warning($"Dependent sized arrays `{CXUtil.GetTypeSpelling(type)}` from `{CXUtil.GetCursorSpelling(parent)}` is not supported", parent.GetSourceLocation());
                        var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent, data);
                        return new CppArrayType(elementType, (int)type.ArraySize);
                    }

                case CXTypeKind.CXType_Unexposed:
                    {
                        // It may be possible to parse them even if they are unexposed.
                        var kind = type.Declaration.Type.kind;
                        if (kind != CXTypeKind.CXType_Unexposed && kind != CXTypeKind.CXType_Invalid)
                        {
                            return GetCppType(type.Declaration, type.Declaration.Type, parent, data);
                        }

                        CppUnexposedType cppUnexposedType = new(CXUtil.GetTypeSpelling(type)) { SizeOf = (int)type.SizeOf };
                        var templateParameters = ParseTemplateSpecializedArguments(cursor, type, new CXClientData((nint)data));
                        if (templateParameters != null)
                        {
                            cppUnexposedType.TemplateParameters.AddRange(templateParameters);
                        }
                        return cppUnexposedType;
                    }

                case CXTypeKind.CXType_Attributed:
                    return GetCppType(type.ModifiedType.Declaration, type.ModifiedType, parent, data);

                case CXTypeKind.CXType_Auto:
                    return GetCppType(type.Declaration, type.Declaration.Type, parent, data);

                case CXTypeKind.CXType_ObjCId:
                    return CppPrimitiveType.ObjCId;

                case CXTypeKind.CXType_ObjCSel:
                    return CppPrimitiveType.ObjCSel;

                case CXTypeKind.CXType_ObjCClass:
                    return CppPrimitiveType.ObjCClass;

                case CXTypeKind.CXType_ObjCObject:
                    return CppPrimitiveType.ObjCObject;

                case CXTypeKind.CXType_Int128:
                    return CppPrimitiveType.Int128;

                case CXTypeKind.CXType_UInt128:
                    return CppPrimitiveType.UInt128;

                case CXTypeKind.CXType_Float16:
                    return CppPrimitiveType.Float16;

                case CXTypeKind.CXType_BFloat16:
                    return CppPrimitiveType.BFloat16;

                case CXTypeKind.CXType_ObjCTypeParam:
                    {
                        CppTemplateParameterType templateArgType = TryToCreateTemplateParametersObjC(cursor);

                        // Record that a typedef is using a template parameter type
                        // which will require to re-parent the typedef to the Obj-C interface it belongs to
                        if (context.CurrentTypedefKey != default)
                        {
                            var map = context.MapTemplateParameterTypeToTypedefKeys;
                            if (!map.TryGetValue(templateArgType, out var typedefKeys))
                            {
                                typedefKeys = [];
                                map.Add(templateArgType, typedefKeys);
                            }

                            typedefKeys.Add(context.CurrentTypedefKey);
                        }

                        return templateArgType;
                    }

                default:
                    {
                        WarningUnhandled(cursor, parent, type);
                        return new CppUnexposedType(CXUtil.GetTypeSpelling(type)) { SizeOf = (int)type.SizeOf };
                    }
            }
        }

        private CppFunctionTypeBase VisitBlockFunctionType(CXCursor cursor, CXType type, CXCursor parent, void* data)
        {
            var pointeeType = type.PointeeType;
            return VisitFunctionType(cursor, pointeeType, parent, data, true);
        }

        private CppFunctionTypeBase VisitFunctionType(CXCursor cursor, CXType type, CXCursor parent, void* data, bool isBlockFunctionType = false)
        {
            // Gets the return type
            var returnType = GetCppType(type.ResultType.Declaration, type.ResultType, cursor, data);

            var cppFunction = isBlockFunctionType
                ? (CppFunctionTypeBase)new CppBlockFunctionType(returnType)
                : new CppFunctionType(returnType);
            cppFunction.CallingConvention = type.GetCallingConvention();

            // We don't use this but use the visitor children to try to recover the parameter names

            //            for (uint i = 0; i < type.NumArgTypes; i++)
            //            {
            //                var argType = type.GetArgType(i);
            //                var cppType = GetCppType(argType.Declaration, argType, type.Declaration, data);
            //                cppFunction.ParameterTypes.Add(cppType);
            //            }

            bool isParsingParameter = false;
            parent.VisitChildren((argCursor, functionCursor, clientData) =>
            {
                if (argCursor.Kind == CXCursorKind.CXCursor_ParmDecl)
                {
                    var name = CXUtil.GetCursorSpelling(argCursor);
                    var parameterType = GetCppType(argCursor.Type.Declaration, argCursor.Type, argCursor, data);

                    cppFunction.Parameters.Add(new CppParameter(parameterType, name));
                    isParsingParameter = true;
                }
                return isParsingParameter ? CXChildVisitResult.CXChildVisit_Continue : CXChildVisitResult.CXChildVisit_Recurse;
            }, new CXClientData((nint)data));

            return cppFunction;
        }

        private List<CppType>? ParseTemplateSpecializedArguments(CXCursor cursor, CXType type, CXClientData data)
        {
            var numTemplateArguments = type.NumTemplateArguments;
            if (numTemplateArguments < 0) return null;

            List<CppType> templateCppTypes = [];
            for (var templateIndex = 0; templateIndex < numTemplateArguments; ++templateIndex)
            {
                var templateArg = type.GetTemplateArgument((uint)templateIndex);

                switch (templateArg.kind)
                {
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                        var templateArgType = templateArg.AsType;
                        //var templateArg = type.GetTemplateArgumentAsType((uint)templateIndex);
                        var templateCppType = GetCppType(templateArgType.Declaration, templateArgType, cursor, data);
                        templateCppTypes.Add(templateCppType);
                        break;

                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Null:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Declaration:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_NullPtr:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Template:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_TemplateExpansion:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Expression:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Pack:
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Invalid:
                        break;

                    default:
                        throw new InvalidOperationException();
                }
            }

            return templateCppTypes;
        }
    }
}