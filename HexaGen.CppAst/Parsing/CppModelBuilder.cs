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
using HexaGen.CppAst.Utilities;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace HexaGen.CppAst.Parsing
{
    /// <summary>
    /// Internal class used to build the entire C++ model from the libclang representation.
    /// </summary>
    public unsafe partial class CppModelBuilder : CompilationLoggerBase
    {
        private readonly CppModelContext context;
        private readonly CppContainerContext _userRootContainerContext;
        private readonly CppContainerContext _systemRootContainerContext;
        private CppContainerContext _rootContainerContext = null!;
        private readonly Dictionary<CursorKey, CppContainerContext> _containers;
        private readonly TypedefResolver typedefResolver = new();
        private readonly Dictionary<CursorKey, CppTemplateParameterType> _objCTemplateParameterTypes;
        private CursorKey _currentTypedefKey;
        private readonly Dictionary<CppTemplateParameterType, HashSet<CursorKey>> _mapTemplateParameterTypeToTypedefKeys;

        public CppModelBuilder()
        {
            _containers = [];
            _mapTemplateParameterTypeToTypedefKeys = [];
            RootCompilation = new();
            _objCTemplateParameterTypes = [];
            _userRootContainerContext = new(RootCompilation, CppContainerContextType.User);
            _systemRootContainerContext = new(RootCompilation.System, CppContainerContextType.System);
            context = new CppModelContext(_containers, RootCompilation, this);
        }

        public bool AutoSquashTypedef { get; set; }

        public bool ParseSystemIncludes { get; set; }

        public bool ParseTokenAttributeEnabled { get; set; }

        public bool ParseCommentAttributeEnabled { get; set; }

        public override CppCompilation RootCompilation { get; }

        public CppContainerContext CurrentRootContainer => _rootContainerContext;

        public TypedefResolver TypedefResolver => typedefResolver;

        public CppContainerContext GetOrCreateDeclarationContainer(CXCursor cursor, void* data)
        {
            while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
            {
                cursor = cursor.SemanticParent;
            }

            if (TryGetDeclarationContainer(cursor, out var typeKey, out var containerContext))
            {
                return containerContext;
            }

            ICppContainer? symbol = null;
            ICppDeclarationContainer? parent = null;
            if (cursor.Kind != CXCursorKind.CXCursor_TranslationUnit && cursor.Kind != CXCursorKind.CXCursor_UnexposedDecl)
            {
                parent = GetOrCreateDeclarationContainer(cursor.SemanticParent, data).DeclarationContainer;
            }

            var defaultContainerVisibility = CppVisibility.Default;
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_Namespace:
                    var ns = new CppNamespace(CXUtil.GetCursorSpelling(cursor));
                    symbol = ns;
                    ns.IsInlineNamespace = cursor.IsInlineNamespace;
                    defaultContainerVisibility = CppVisibility.Default;
                    ((ICppGlobalDeclarationContainer)parent!).Namespaces.Add(ns);
                    break;

                case CXCursorKind.CXCursor_EnumDecl:
                    var cppEnum = new CppEnum(CXUtil.GetCursorSpelling(cursor))
                    {
                        IsAnonymous = cursor.IsAnonymous,
                        Visibility = cursor.GetVisibility()
                    };
                    parent!.Enums.Add(cppEnum);
                    symbol = cppEnum;
                    break;

                case CXCursorKind.CXCursor_ClassTemplate:
                case CXCursorKind.CXCursor_ClassTemplatePartialSpecialization:
                case CXCursorKind.CXCursor_ClassDecl:
                case CXCursorKind.CXCursor_StructDecl:
                case CXCursorKind.CXCursor_UnionDecl:
                case CXCursorKind.CXCursor_ObjCInterfaceDecl:
                case CXCursorKind.CXCursor_ObjCProtocolDecl:
                case CXCursorKind.CXCursor_ObjCCategoryDecl:
                    var cppClass = new CppClass(CXUtil.GetCursorSpelling(cursor));
                    parent!.Classes.Add(cppClass);
                    symbol = cppClass;
                    cppClass.IsAnonymous = cursor.IsAnonymous;
                    switch (cursor.Kind)
                    {
                        case CXCursorKind.CXCursor_ClassDecl:
                        case CXCursorKind.CXCursor_ClassTemplate:
                        case CXCursorKind.CXCursor_ClassTemplatePartialSpecialization:
                            cppClass.ClassKind = CppClassKind.Class;
                            break;

                        case CXCursorKind.CXCursor_StructDecl:
                            cppClass.ClassKind = CppClassKind.Struct;
                            break;

                        case CXCursorKind.CXCursor_UnionDecl:
                            cppClass.ClassKind = CppClassKind.Union;
                            break;

                        case CXCursorKind.CXCursor_ObjCInterfaceDecl:
                            cppClass.ClassKind = CppClassKind.ObjCInterface;
                            break;

                        case CXCursorKind.CXCursor_ObjCProtocolDecl:
                            cppClass.ClassKind = CppClassKind.ObjCProtocol;
                            break;

                        case CXCursorKind.CXCursor_ObjCCategoryDecl:
                            {
                                cppClass.ClassKind = CppClassKind.ObjCInterfaceCategory;

                                // Fetch the target class for the category
                                CXCursor parentCursor = default;
                                cursor.VisitChildren((cxCursor, parent, clientData) =>
                                {
                                    if (cxCursor.Kind == CXCursorKind.CXCursor_ObjCClassRef)
                                    {
                                        parentCursor = cxCursor.Referenced;
                                        return CXChildVisitResult.CXChildVisit_Break;
                                    }

                                    return CXChildVisitResult.CXChildVisit_Continue;
                                }, default);

                                var parentContainer = GetOrCreateDeclarationContainer(parentCursor, data).Container;
                                var targetClass = (CppClass)parentContainer;
                                cppClass.ObjCCategoryName = cppClass.Name;
                                cppClass.Name = targetClass.Name;
                                cppClass.ObjCCategoryTargetClass = targetClass;

                                // Link back
                                targetClass.ObjCCategories.Add(cppClass);
                                break;
                            }
                    }

                    cppClass.IsAbstract = cursor.CXXRecord_IsAbstract;

                    if (cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplateSpecialization
                        || cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplatePartialSpecialization)
                    {
                        //Try to generate template class first
                        cppClass.SpecializedTemplate = (CppClass)GetOrCreateDeclarationContainer(cursor.SpecializedCursorTemplate, data).Container;
                        if (cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplatePartialSpecialization)
                        {
                            cppClass.TemplateKind = CppTemplateKind.PartialTemplateClass;
                        }
                        else
                        {
                            cppClass.TemplateKind = CppTemplateKind.TemplateSpecializedClass;
                        }

                        //Just use low level api to call ClangSharp
                        var tempArgsCount = cursor.NumTemplateArguments;
                        var tempParams = cppClass.SpecializedTemplate.TemplateParameters;

                        //Just use template class template params here
                        foreach (var param in tempParams)
                        {
                            switch (param)
                            {
                                case CppTemplateParameterType paramType:
                                    cppClass.TemplateParameters.Add(new CppTemplateParameterType(paramType.Name));
                                    break;

                                case CppTemplateParameterNonType nonType:
                                    cppClass.TemplateParameters.Add(new CppTemplateParameterNonType(nonType.Name, nonType.NoneTemplateType));
                                    break;
                            }
                        }

                        if (cppClass.TemplateKind == CppTemplateKind.TemplateSpecializedClass)
                        {
                            Debug.Assert(cppClass.SpecializedTemplate.TemplateParameters.Count == tempArgsCount);
                        }

                        for (uint i = 0; i < tempArgsCount; i++)
                        {
                            var arg = cursor.GetTemplateArgument(i);
                            switch (arg.kind)
                            {
                                case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                                    {
                                        var argh = arg.AsType;
                                        var argType = GetCppType(argh.Declaration, argh, cursor, data);
                                        cppClass.TemplateSpecializedArguments.Add(new CppTemplateArgument(tempParams[(int)i], argType, argh.TypeClass != CX_TypeClass.CX_TypeClass_TemplateTypeParm));
                                    }
                                    break;

                                case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                                    {
                                        cppClass.TemplateSpecializedArguments.Add(new CppTemplateArgument(tempParams[(int)i], arg.AsIntegral));
                                    }
                                    break;

                                default:
                                    {
                                        RootCompilation.Diagnostics.Warning($"Unhandled template argument with type {arg.kind}: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                                        cppClass.TemplateSpecializedArguments.Add(new CppTemplateArgument(tempParams[(int)i], arg.ToString()));
                                    }
                                    break;
                            }
                            arg.Dispose();
                        }
                    }
                    else
                    {
                        AddTemplateParameters(cursor, cppClass);
                    }

                    defaultContainerVisibility = cursor.Kind == CXCursorKind.CXCursor_ClassDecl ? CppVisibility.Private : CppVisibility.Public;
                    break;

                case CXCursorKind.CXCursor_TranslationUnit:
                case CXCursorKind.CXCursor_UnexposedDecl:
                case CXCursorKind.CXCursor_FirstInvalid:
                    _containers.TryAdd(typeKey, _rootContainerContext);
                    return _rootContainerContext;

                default:
                    Unhandled(cursor);
                    // TODO: Workaround for now, as the container below would have an empty symbol
                    goto case CXCursorKind.CXCursor_TranslationUnit;
            }

            containerContext = new CppContainerContext(symbol, CppContainerContextType.Unspecified) { CurrentVisibility = defaultContainerVisibility };

            // The type could have been added separately as part of the GetCppType above TemplateParameters
            _containers.TryAdd(typeKey, containerContext);
            return containerContext;
        }

        public CppTemplateParameterType TryToCreateTemplateParametersObjC(CXCursor cursor)
        {
            if (cursor.Kind != CXCursorKind.CXCursor_TemplateTypeParameter)
            {
                throw new InvalidOperationException("Only CXCursor_TemplateTypeParameter is supported here");
            }

            var key = GetCursorKey(cursor);
            if (!_objCTemplateParameterTypes.TryGetValue(key, out var templateParameterType))
            {
                var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                templateParameterType = new(templateParameterName);
                _objCTemplateParameterTypes.Add(key, templateParameterType);
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
            return _containers.TryGetValue(typeKey, out containerContext);
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
            context = GetOrCreateDeclarationContainer(cursor, data);
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

        public void VisitInitValue(CXCursor cursor, void* data, out CppExpression? expression, out CppValue? value)
        {
            CppExpression? localExpression = null;
            CppValue? localValue = null;

            cursor.VisitChildren((initCursor, varCursor, clientData) =>
            {
                if (initCursor.IsExpression())
                {
                    localExpression = VisitExpression(initCursor, clientData);
                    return CXChildVisitResult.CXChildVisit_Break;
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData((nint)data));

            // Still tries to extract the compiled value
            CXEvalResult resultEval = cursor.Evaluate;

            switch (resultEval.Kind)
            {
                case CXEvalResultKind.CXEval_Int:
                    localValue = new CppValue(resultEval.AsLongLong);
                    break;

                case CXEvalResultKind.CXEval_Float:
                    localValue = new CppValue(resultEval.AsDouble);
                    break;

                case CXEvalResultKind.CXEval_ObjCStrLiteral:
                case CXEvalResultKind.CXEval_StrLiteral:
                case CXEvalResultKind.CXEval_CFStr:
                    localValue = new CppValue(resultEval.AsStr);
                    break;

                case CXEvalResultKind.CXEval_UnExposed:
                    break;

                default:
                    RootCompilation.Diagnostics.Warning($"Not supported field default value {CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                    break;
            }

            expression = localExpression;
            value = localValue;
            resultEval.Dispose();
        }

        public CppEnum VisitEnumDecl(CXCursor cursor, void* data)
        {
            var cppEnum = GetOrCreateDeclarationContainer<CppEnum>(cursor, data, out var context);
            if (cursor.IsDefinition && !context.IsChildrenVisited)
            {
                var integralType = cursor.EnumDecl_IntegerType;
                cppEnum.IntegerType = GetCppType(integralType.Declaration, integralType, cursor, data);
                cppEnum.IsScoped = cursor.EnumDecl_IsScoped;
                ParseAttributes(cursor, cppEnum);
                context.IsChildrenVisited = true;
                cursor.VisitChildren(VisitMember, new CXClientData((nint)data));
            }
            return cppEnum;
        }

        public CppType VisitTypeDefDecl(CXCursor cursor, void* data)
        {
            var fulltypeDefName = GetCursorKey(cursor);
            if (typedefResolver.TryResolve(fulltypeDefName, out var type))
            {
                return type;
            }

            var contextContainer = GetOrCreateDeclarationContainer(cursor.SemanticParent, data);
            _currentTypedefKey = fulltypeDefName;
            var underlyingTypeDefType = GetCppType(cursor.TypedefDeclUnderlyingType.Declaration, cursor.TypedefDeclUnderlyingType, cursor, data);
            _currentTypedefKey = default;

            var typedefName = CXUtil.GetCursorSpelling(cursor);

            ICppDeclarationContainer? container = null;

            if (AutoSquashTypedef && underlyingTypeDefType is ICppMember cppMember && (string.IsNullOrEmpty(cppMember.Name) || typedefName == cppMember.Name))
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

            ParseTypedefAttribute(cursor, type, underlyingTypeDefType);

            // The type could have been added separately as part of the GetCppType above
            typedefResolver.RegisterTypedef(fulltypeDefName, type);

            // Try to remap typedef using a parameter type declared in an ObjC interface
            if (_mapTemplateParameterTypeToTypedefKeys.Count > 0)
            {
                foreach (var pair in _mapTemplateParameterTypeToTypedefKeys.ToList())
                {
                    if (pair.Value.Contains(fulltypeDefName))
                    {
                        container = (ICppDeclarationContainer?)pair.Key.Parent;
                        _mapTemplateParameterTypeToTypedefKeys.Remove(pair.Key);
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

        private CppType VisitElaboratedDecl(CXCursor cursor, CXType type, CXCursor parent, void* data)
        {
            var fulltypeDefName = GetCursorKey(cursor);
            if (typedefResolver.TryResolve(fulltypeDefName, out var typeRef))
            {
                return typeRef;
            }

            // If the type has been already declared, return it immediately.
            if (TryGetDeclarationContainer(cursor, out _, out var containerContext))
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
                    {
                        return VisitClassDecl(cursor, data);
                    }
                case CXTypeKind.CXType_Enum:
                    return VisitEnumDecl(cursor, data);

                case CXTypeKind.CXType_FunctionProto:
                    return VisitFunctionType(cursor, type, parent, data);

                case CXTypeKind.CXType_BlockPointer:
                    return VisitBlockFunctionType(cursor, type, parent, data);

                case CXTypeKind.CXType_Typedef:
                    return VisitTypeDefDecl(cursor, data);

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
                        if (_currentTypedefKey != default)
                        {
                            if (!_mapTemplateParameterTypeToTypedefKeys.TryGetValue(templateArgType, out var typedefKeys))
                            {
                                typedefKeys = [];
                                _mapTemplateParameterTypeToTypedefKeys.Add(templateArgType, typedefKeys);
                            }

                            typedefKeys.Add(_currentTypedefKey);
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

            var templateCppTypes = new List<CppType>();
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
                        throw new ArgumentOutOfRangeException();
                }
            }

            return templateCppTypes;
        }

        public CursorKey GetCursorKey(CXCursor cursor)
        {
            return new(_rootContainerContext, cursor);
        }
    }
}