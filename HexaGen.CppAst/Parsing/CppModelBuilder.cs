// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using ClangSharp.Interop;
using HexaGen.CppAst.AttributeUtils;
using HexaGen.CppAst.Collections;
using HexaGen.CppAst.Extensions;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Attributes;
using HexaGen.CppAst.Model.Declarations;
using HexaGen.CppAst.Model.Expressions;
using HexaGen.CppAst.Model.Interfaces;
using HexaGen.CppAst.Model.Metadata;
using HexaGen.CppAst.Model.Templates;
using HexaGen.CppAst.Model.Types;
using HexaGen.CppAst.Utilities;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace HexaGen.CppAst.Parsing
{
    /// <summary>
    /// Internal class used to build the entire C++ model from the libclang representation.
    /// </summary>
    public unsafe partial class CppModelBuilder
    {
        private readonly CppModelContext context;
        private readonly CppContainerContext _userRootContainerContext;
        private readonly CppContainerContext _systemRootContainerContext;
        private CppContainerContext _rootContainerContext = null!;
        private readonly Dictionary<CursorKey, CppContainerContext> _containers;
        private readonly TypedefResolver typedefResolver = new();
        private readonly Dictionary<CursorKey, CppType> _objCTemplateParameterTypes;
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

        public CppCompilation RootCompilation { get; }

        public CppContainerContext CurrentRootContainer => _rootContainerContext;

        public TypedefResolver TypedefResolver => typedefResolver;

        public CppContainerContext GetOrCreateDeclarationContainer(CXCursor cursor, void* data)
        {
            while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
            {
                cursor = cursor.SemanticParent;
            }

            if (TryGetDeclarationContainer(cursor, data, out var typeKey, out var containerContext))
            {
                return containerContext;
            }

            ICppContainer? symbol = null;

            ICppContainer? parent = null;
            if (cursor.Kind != CXCursorKind.CXCursor_TranslationUnit && cursor.Kind != CXCursorKind.CXCursor_UnexposedDecl)
            {
                parent = GetOrCreateDeclarationContainer(cursor.SemanticParent, data).Container;
            }

            ICppDeclarationContainer? parentDeclarationContainer = (ICppDeclarationContainer?)parent;
            ICppGlobalDeclarationContainer? parentGlobalDeclarationContainer = parent as ICppGlobalDeclarationContainer;

            var defaultContainerVisibility = CppVisibility.Default;
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_Namespace:
                    Debug.Assert(parentGlobalDeclarationContainer != null);
                    var ns = new CppNamespace(CXUtil.GetCursorSpelling(cursor));
                    symbol = ns;
                    ns.IsInlineNamespace = cursor.IsInlineNamespace;
                    defaultContainerVisibility = CppVisibility.Default;
                    parentGlobalDeclarationContainer.Namespaces.Add(ns);
                    break;

                case CXCursorKind.CXCursor_EnumDecl:
                    var cppEnum = new CppEnum(CXUtil.GetCursorSpelling(cursor))
                    {
                        IsAnonymous = cursor.IsAnonymous,
                        Visibility = cursor.GetVisibility()
                    };
                    parentDeclarationContainer.Enums.Add(cppEnum);
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
                    parentDeclarationContainer.Classes.Add(cppClass);
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

        public CppType TryToCreateTemplateParametersObjC(CXCursor cursor, void* data)
        {
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_TemplateTypeParameter:
                    {
                        var key = GetCursorKey(cursor);
                        if (!_objCTemplateParameterTypes.TryGetValue(key, out var templateParameterType))
                        {
                            var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                            templateParameterType = new CppTemplateParameterType(templateParameterName);
                            _objCTemplateParameterTypes.Add(key, templateParameterType);
                        }
                        return templateParameterType;
                    }
            }

            return null;
        }

        public CppType TryToCreateTemplateParameters(CXCursor cursor, void* data)
        {
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_TemplateTypeParameter:
                    {
                        var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                        var templateParameterType = new CppTemplateParameterType(templateParameterName);
                        return templateParameterType;
                    }
                case CXCursorKind.CXCursor_NonTypeTemplateParameter:
                    {
                        //Just use low level ClangSharp object to do the logic
                        var tmptype = cursor.Type;
                        var tmpcpptype = GetCppType(tmptype.Declaration, tmptype, cursor, data);
                        var tmpname = CXUtil.GetCursorSpelling(cursor);

                        var templateParameterType = new CppTemplateParameterNonType(tmpname, tmpcpptype);

                        return templateParameterType;
                    }
                case CXCursorKind.CXCursor_TemplateTemplateParameter:
                    {
                        //ToDo: add template template parameter support here~~
                        RootCompilation.Diagnostics.Warning($"Unhandled template parameter: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                        var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                        var templateParameterType = new CppTemplateParameterType(templateParameterName);
                        return templateParameterType;
                    }
            }

            return null;
        }

        public bool TryGetDeclarationContainer(CXCursor cursor, void* data, out CursorKey typeKey, out CppContainerContext containerContext)
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
                    var tmplParam = TryToCreateTemplateParametersObjC(childCursor, clientData);
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

        private CppComment GetComment(CXCursor cursor)
        {
            var cxComment = cursor.ParsedComment;
            return GetComment(cxComment);
        }

        private CppComment? GetComment(CXComment cxComment)
        {
            var cppKind = GetCommentKind(cxComment.Kind);

            CppComment? cppComment = null;

            bool removeTrailingEmptyText = false;

            switch (cppKind)
            {
                case CppCommentKind.Null:
                    return null;

                case CppCommentKind.Text:
                    cppComment = new CppCommentText()
                    {
                        Text = CXUtil.GetComment_TextComment_Text(cxComment)?.TrimStart()
                    };
                    break;

                case CppCommentKind.InlineCommand:
                    var inline = new CppCommentInlineCommand();
                    inline.CommandName = CXUtil.GetComment_InlineCommandComment_CommandName(cxComment);
                    cppComment = inline;
                    switch (cxComment.InlineCommandComment_RenderKind)
                    {
                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Normal:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Normal;
                            break;

                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Bold:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Bold;
                            break;

                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Monospaced:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Monospaced;
                            break;

                        case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Emphasized:
                            inline.RenderKind = CppCommentInlineCommandRenderKind.Emphasized;
                            break;
                    }

                    for (uint i = 0; i < cxComment.InlineCommandComment_NumArgs; i++)
                    {
                        inline.Arguments.Add(CXUtil.GetComment_InlineCommandComment_ArgText(cxComment, i));
                    }
                    break;

                case CppCommentKind.HtmlStartTag:
                    var htmlStartTag = new CppCommentHtmlStartTag();
                    htmlStartTag.TagName = CXUtil.GetComment_HtmlTagComment_TagName(cxComment);
                    htmlStartTag.IsSelfClosing = cxComment.HtmlStartTagComment_IsSelfClosing;
                    for (uint i = 0; i < cxComment.HtmlStartTag_NumAttrs; i++)
                    {
                        htmlStartTag.Attributes.Add(new KeyValuePair<string, string>(
                            CXUtil.GetComment_HtmlStartTag_AttrName(cxComment, i),
                            CXUtil.GetComment_HtmlStartTag_AttrValue(cxComment, i)
                            ));
                    }
                    cppComment = htmlStartTag;
                    break;

                case CppCommentKind.HtmlEndTag:
                    var htmlEndTag = new CppCommentHtmlEndTag();
                    htmlEndTag.TagName = CXUtil.GetComment_HtmlTagComment_TagName(cxComment);
                    cppComment = htmlEndTag;
                    break;

                case CppCommentKind.Paragraph:
                    cppComment = new CppCommentParagraph();
                    break;

                case CppCommentKind.BlockCommand:
                    var blockComment = new CppCommentBlockCommand();
                    blockComment.CommandName = CXUtil.GetComment_BlockCommandComment_CommandName(cxComment);
                    for (uint i = 0; i < cxComment.BlockCommandComment_NumArgs; i++)
                    {
                        blockComment.Arguments.Add(CXUtil.GetComment_BlockCommandComment_ArgText(cxComment, i));
                    }

                    removeTrailingEmptyText = true;
                    cppComment = blockComment;
                    break;

                case CppCommentKind.ParamCommand:
                    var paramComment = new CppCommentParamCommand();
                    paramComment.CommandName = "param";
                    paramComment.ParamName = CXUtil.GetComment_ParamCommandComment_ParamName(cxComment);
                    paramComment.IsDirectionExplicit = cxComment.ParamCommandComment_IsDirectionExplicit;
                    paramComment.IsParamIndexValid = cxComment.ParamCommandComment_IsParamIndexValid;
                    paramComment.ParamIndex = (int)cxComment.ParamCommandComment_ParamIndex;
                    switch (cxComment.ParamCommandComment_Direction)
                    {
                        case CXCommentParamPassDirection.CXCommentParamPassDirection_In:
                            paramComment.Direction = CppCommentParamDirection.In;
                            break;

                        case CXCommentParamPassDirection.CXCommentParamPassDirection_Out:
                            paramComment.Direction = CppCommentParamDirection.Out;
                            break;

                        case CXCommentParamPassDirection.CXCommentParamPassDirection_InOut:
                            paramComment.Direction = CppCommentParamDirection.InOut;
                            break;
                    }

                    removeTrailingEmptyText = true;
                    cppComment = paramComment;
                    break;

                case CppCommentKind.TemplateParamCommand:
                    var tParamComment = new CppCommentTemplateParamCommand();
                    tParamComment.CommandName = "tparam";
                    tParamComment.ParamName = CXUtil.GetComment_TParamCommandComment_ParamName(cxComment);
                    tParamComment.Depth = (int)cxComment.TParamCommandComment_Depth;
                    // TODO: index
                    tParamComment.IsPositionValid = cxComment.TParamCommandComment_IsParamPositionValid;

                    removeTrailingEmptyText = true;
                    cppComment = tParamComment;
                    break;

                case CppCommentKind.VerbatimBlockCommand:
                    var verbatimBlock = new CppCommentVerbatimBlockCommand();
                    verbatimBlock.CommandName = CXUtil.GetComment_BlockCommandComment_CommandName(cxComment);
                    for (uint i = 0; i < cxComment.BlockCommandComment_NumArgs; i++)
                    {
                        verbatimBlock.Arguments.Add(CXUtil.GetComment_BlockCommandComment_ArgText(cxComment, i));
                    }
                    cppComment = verbatimBlock;
                    break;

                case CppCommentKind.VerbatimBlockLine:
                    var text = CXUtil.GetComment_VerbatimBlockLineComment_Text(cxComment);

                    // For some reason, VerbatimBlockLineComment_Text can return the rest of the file instead of just the line
                    // So we explicitly trim the line here
                    var indexOfLine = text.IndexOf('\n');
                    if (indexOfLine >= 0)
                    {
                        text = text.Substring(0, indexOfLine);
                    }

                    cppComment = new CppCommentVerbatimBlockLine()
                    {
                        Text = text
                    };
                    break;

                case CppCommentKind.VerbatimLine:
                    cppComment = new CppCommentVerbatimLine()
                    {
                        Text = CXUtil.GetComment_VerbatimLineComment_Text(cxComment)
                    };
                    break;

                case CppCommentKind.Full:
                    cppComment = new CppCommentFull();
                    break;

                default:
                    return null;
            }

            Debug.Assert(cppComment != null);

            for (uint i = 0; i < cxComment.NumChildren; i++)
            {
                var cxChildComment = cxComment.GetChild(i);
                var cppChildComment = GetComment(cxChildComment);
                if (cppChildComment != null)
                {
                    if (cppComment.Children == null)
                    {
                        cppComment.Children = new List<CppComment>();
                    }
                    cppComment.Children.Add(cppChildComment);
                }
            }

            if (removeTrailingEmptyText)
            {
                RemoveTrailingEmptyText(cppComment);
            }

            return cppComment;
        }

        private static void RemoveTrailingEmptyText(CppComment cppComment)
        {
            // Remove the last paragraph if it is an empty string text
            if (cppComment.Children != null && cppComment.Children.Count > 0 && cppComment.Children[cppComment.Children.Count - 1] is CppCommentParagraph paragraph)
            {
                // Remove the last paragraph if it is an empty string text
                if (paragraph.Children != null && paragraph.Children.Count > 0 && paragraph.Children[paragraph.Children.Count - 1] is CppCommentText text && string.IsNullOrWhiteSpace(text.Text))
                {
                    paragraph.Children.RemoveAt(paragraph.Children.Count - 1);
                }
            }
        }

        private CppCommentKind GetCommentKind(CXCommentKind kind)
        {
            return kind switch
            {
                CXCommentKind.CXComment_Null => CppCommentKind.Null,
                CXCommentKind.CXComment_Text => CppCommentKind.Text,
                CXCommentKind.CXComment_InlineCommand => CppCommentKind.InlineCommand,
                CXCommentKind.CXComment_HTMLStartTag => CppCommentKind.HtmlStartTag,
                CXCommentKind.CXComment_HTMLEndTag => CppCommentKind.HtmlEndTag,
                CXCommentKind.CXComment_Paragraph => CppCommentKind.Paragraph,
                CXCommentKind.CXComment_BlockCommand => CppCommentKind.BlockCommand,
                CXCommentKind.CXComment_ParamCommand => CppCommentKind.ParamCommand,
                CXCommentKind.CXComment_TParamCommand => CppCommentKind.TemplateParamCommand,
                CXCommentKind.CXComment_VerbatimBlockCommand => CppCommentKind.VerbatimBlockCommand,
                CXCommentKind.CXComment_VerbatimBlockLine => CppCommentKind.VerbatimBlockLine,
                CXCommentKind.CXComment_VerbatimLine => CppCommentKind.VerbatimLine,
                CXCommentKind.CXComment_FullComment => CppCommentKind.Full,
                _ => throw new ArgumentOutOfRangeException($"Unsupported comment kind `{kind}`"),
            };
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

        public void VisitInitValue(CXCursor cursor, void* data, out CppExpression expression, out CppValue value)
        {
            CppExpression localExpression = null;
            CppValue localValue = null;

            cursor.VisitChildren((initCursor, varCursor, clientData) =>
            {
                if (IsExpression(initCursor))
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

        private static bool IsExpression(CXCursor cursor)
        {
            return cursor.Kind >= CXCursorKind.CXCursor_FirstExpr && cursor.Kind <= CXCursorKind.CXCursor_LastExpr;
        }

        private CppExpression VisitExpression(CXCursor cursor, void* data)
        {
            CppExpression expr = null;
            bool visitChildren = false;
            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_UnexposedExpr:
                    expr = new CppRawExpression(CppExpressionKind.Unexposed);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_DeclRefExpr:
                    expr = new CppRawExpression(CppExpressionKind.DeclRef);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_MemberRefExpr:
                    expr = new CppRawExpression(CppExpressionKind.MemberRef);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CallExpr:
                    expr = new CppRawExpression(CppExpressionKind.Call);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCMessageExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCMessage);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_BlockExpr:
                    expr = new CppRawExpression(CppExpressionKind.Block);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_IntegerLiteral:
                    expr = new CppLiteralExpression(CppExpressionKind.IntegerLiteral, GetCursorAsText(cursor));
                    break;

                case CXCursorKind.CXCursor_FloatingLiteral:
                    expr = new CppLiteralExpression(CppExpressionKind.FloatingLiteral, GetCursorAsText(cursor));
                    break;

                case CXCursorKind.CXCursor_ImaginaryLiteral:
                    expr = new CppLiteralExpression(CppExpressionKind.ImaginaryLiteral, GetCursorAsText(cursor));
                    break;

                case CXCursorKind.CXCursor_StringLiteral:
                    expr = new CppLiteralExpression(CppExpressionKind.StringLiteral, GetCursorAsText(cursor));
                    break;

                case CXCursorKind.CXCursor_CharacterLiteral:
                    expr = new CppLiteralExpression(CppExpressionKind.CharacterLiteral, GetCursorAsText(cursor));
                    break;

                case CXCursorKind.CXCursor_ParenExpr:
                    expr = new CppParenExpression();
                    visitChildren = true;
                    break;

                case CXCursorKind.CXCursor_UnaryOperator:
                    var tokens = new CppTokenUtil.Tokenizer(cursor);
                    expr = new CppUnaryExpression(CppExpressionKind.UnaryOperator)
                    {
                        Operator = tokens.Count > 0 ? tokens.GetString(0) : string.Empty
                    };
                    visitChildren = true;
                    break;

                case CXCursorKind.CXCursor_ArraySubscriptExpr:
                    expr = new CppRawExpression(CppExpressionKind.ArraySubscript);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_BinaryOperator:
                    expr = new CppBinaryExpression(CppExpressionKind.BinaryOperator);
                    visitChildren = true;
                    break;

                case CXCursorKind.CXCursor_CompoundAssignOperator:
                    expr = new CppRawExpression(CppExpressionKind.CompoundAssignOperator);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ConditionalOperator:
                    expr = new CppRawExpression(CppExpressionKind.ConditionalOperator);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CStyleCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.CStyleCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CompoundLiteralExpr:
                    expr = new CppRawExpression(CppExpressionKind.CompoundLiteral);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_InitListExpr:
                    expr = new CppInitListExpression();
                    visitChildren = true;
                    break;

                case CXCursorKind.CXCursor_AddrLabelExpr:
                    expr = new CppRawExpression(CppExpressionKind.AddrLabel);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_StmtExpr:
                    expr = new CppRawExpression(CppExpressionKind.Stmt);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_GenericSelectionExpr:
                    expr = new CppRawExpression(CppExpressionKind.GenericSelection);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_GNUNullExpr:
                    expr = new CppRawExpression(CppExpressionKind.GNUNull);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXStaticCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXStaticCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXDynamicCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXDynamicCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXReinterpretCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXReinterpretCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXConstCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXConstCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXFunctionalCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXFunctionalCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXTypeidExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXTypeid);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXBoolLiteralExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXBoolLiteral);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXNullPtrLiteralExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXNullPtrLiteral);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXThisExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXThis);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXThrowExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXThrow);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXNewExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXNew);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_CXXDeleteExpr:
                    expr = new CppRawExpression(CppExpressionKind.CXXDelete);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_UnaryExpr:
                    expr = new CppRawExpression(CppExpressionKind.Unary);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCStringLiteral:
                    expr = new CppRawExpression(CppExpressionKind.ObjCStringLiteral);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCEncodeExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCEncode);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCSelectorExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCSelector);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCProtocolExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCProtocol);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCBridgedCastExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCBridgedCast);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_PackExpansionExpr:
                    expr = new CppRawExpression(CppExpressionKind.PackExpansion);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_SizeOfPackExpr:
                    expr = new CppRawExpression(CppExpressionKind.SizeOfPack);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_LambdaExpr:
                    expr = new CppRawExpression(CppExpressionKind.Lambda);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCBoolLiteralExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCBoolLiteral);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCSelfExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCSelf);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_OMPArraySectionExpr:
                    expr = new CppRawExpression(CppExpressionKind.OMPArraySection);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_ObjCAvailabilityCheckExpr:
                    expr = new CppRawExpression(CppExpressionKind.ObjCAvailabilityCheck);
                    AppendTokensToExpression(cursor, expr);
                    break;

                case CXCursorKind.CXCursor_FixedPointLiteral:
                    expr = new CppLiteralExpression(CppExpressionKind.FixedPointLiteral, GetCursorAsText(cursor));
                    break;

                default:
                    return null;
            }

            expr.AssignSourceSpan(cursor);

            if (visitChildren)
            {
                cursor.VisitChildren((listCursor, initListCursor, clientData) =>
                {
                    var item = VisitExpression(listCursor, data);
                    if (item != null)
                    {
                        expr.AddArgument(item);
                    }

                    return CXChildVisitResult.CXChildVisit_Continue;
                }, new CXClientData((nint)data));
            }

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_BinaryOperator:
                    var beforeOperatorOffset = expr.Arguments[0].Span.End.Offset;
                    var afterOperatorOffset = expr.Arguments[1].Span.Start.Offset;
                    ((CppBinaryExpression)expr).Operator = GetCursorAsTextBetweenOffset(cursor, beforeOperatorOffset, afterOperatorOffset);
                    break;
            }

            return expr;
        }

        private void AppendTokensToExpression(CXCursor cursor, CppExpression expression)
        {
            if (expression is CppRawExpression tokensExpr)
            {
                var tokenizer = new CppTokenUtil.Tokenizer(cursor);
                for (int i = 0; i < tokenizer.Count; i++)
                {
                    tokensExpr.Tokens.Add(tokenizer[i]);
                }
                tokensExpr.UpdateTextFromTokens();
            }
        }

        public CppEnum VisitEnumDecl(CXCursor cursor, void* data)
        {
            var cppEnum = GetOrCreateDeclarationContainer<CppEnum>(cursor, data, out var context);
            if (cursor.IsDefinition && !context.IsChildrenVisited)
            {
                var integralType = cursor.EnumDecl_IntegerType;
                cppEnum.IntegerType = GetCppType(integralType.Declaration, integralType, cursor, data);
                cppEnum.IsScoped = cursor.EnumDecl_IsScoped;
                ParseAttributes(cursor, cppEnum, false);
                context.IsChildrenVisited = true;
                cursor.VisitChildren(VisitMember, new CXClientData((nint)data));
            }
            return cppEnum;
        }

        private List<CppAttribute> ParseSystemAndAnnotateAttributeInCursor(CXCursor cursor)
        {
            List<CppAttribute> collectAttributes = new List<CppAttribute>();
            cursor.VisitChildren((argCursor, parentCursor, clientData) =>
            {
                var sourceSpan = argCursor.GetSourceRange();
                var meta = CXUtil.GetCursorSpelling(argCursor);
                switch (argCursor.Kind)
                {
                    case CXCursorKind.CXCursor_VisibilityAttr:
                        {
                            CppAttribute attribute = new CppAttribute("visibility", AttributeKind.CxxSystemAttribute);
                            attribute.AssignSourceSpan(argCursor);
                            attribute.Arguments = string.Format("\"{0}\"", CXUtil.GetCursorDisplayName(argCursor));
                            collectAttributes.Add(attribute);
                        }
                        break;

                    case CXCursorKind.CXCursor_AnnotateAttr:
                        {
                            var attribute = new CppAttribute("annotate", AttributeKind.AnnotateAttribute)
                            {
                                Span = sourceSpan,
                                Scope = "",
                                Arguments = meta,
                                IsVariadic = false,
                            };

                            collectAttributes.Add(attribute);
                        }
                        break;

                    case CXCursorKind.CXCursor_AlignedAttr:
                        {
                            var attrKindSpelling = argCursor.AttrKindSpelling.ToLower();
                            var attribute = new CppAttribute("alignas", AttributeKind.CxxSystemAttribute)
                            {
                                Span = sourceSpan,
                                Scope = "",
                                Arguments = "",
                                IsVariadic = false,
                            };

                            collectAttributes.Add(attribute);
                        }
                        break;

                    case CXCursorKind.CXCursor_UnexposedAttr:
                        {
                            var attrKind = argCursor.AttrKind;
                            var attrKindSpelling = argCursor.AttrKindSpelling.ToLower();

                            var attribute = new CppAttribute(attrKindSpelling, AttributeKind.CxxSystemAttribute)
                            {
                                Span = sourceSpan,
                                Scope = "",
                                Arguments = "",
                                IsVariadic = false,
                            };

                            collectAttributes.Add(attribute);
                        }
                        break;

                    case CXCursorKind.CXCursor_DLLImport:
                    case CXCursorKind.CXCursor_DLLExport:
                        {
                            var attrKind = argCursor.AttrKind;
                            var attrKindSpelling = argCursor.AttrKindSpelling.ToLower();

                            var attribute = new CppAttribute(attrKindSpelling, AttributeKind.CxxSystemAttribute)
                            {
                                Span = sourceSpan,
                                Scope = "",
                                Arguments = "",
                                IsVariadic = false,
                            };

                            collectAttributes.Add(attribute);
                        }
                        break;

                    // Don't generate a warning for unsupported cursor
                    default:
                        break;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData(0));
            return collectAttributes;
        }

        private void TryToParseAttributesFromComment(CppComment comment, ICppAttributeContainer attrContainer)
        {
            if (comment == null) return;

            if (comment is CppCommentText ctxt)
            {
                var txt = ctxt.Text.Trim();
                if (txt.StartsWith("[[") && txt.EndsWith("]]"))
                {
                    attrContainer.Attributes.Add(new CppAttribute("comment", AttributeKind.CommentAttribute)
                    {
                        Arguments = txt,
                        Scope = "",
                        IsVariadic = false,
                    });
                }
            }

            if (comment.Children != null)
            {
                foreach (var child in comment.Children)
                {
                    TryToParseAttributesFromComment(child, attrContainer);
                }
            }
        }

        private void AppendToMetaAttributes(List<MetaAttribute> metaList, MetaAttribute metaAttr)
        {
            if (metaAttr is null)
            {
                return;
            }

            foreach (MetaAttribute meta in metaList)
            {
                foreach (KeyValuePair<string, object> kvp in meta.ArgumentMap)
                {
                    if (metaAttr.ArgumentMap.ContainsKey(kvp.Key))
                    {
                        metaAttr.ArgumentMap.Remove(kvp.Key);
                    }
                }
            }

            if (metaAttr.ArgumentMap.Count > 0)
            {
                metaList.Add(metaAttr);
            }
        }

        private void TryToConvertAttributesToMetaAttributes(ICppAttributeContainer attrContainer)
        {
            foreach (var attr in attrContainer.Attributes)
            {
                //Now we only handle for annotate attribute here
                if (attr.Kind == AttributeKind.AnnotateAttribute)
                {
                    MetaAttribute metaAttr = null;
                    string errorMessage = null;

                    metaAttr = CustomAttributeTool.ParseMetaStringFor(attr.Arguments, out errorMessage);

                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        var element = (CppElement)attrContainer;
                        throw new Exception($"handle meta not right, detail: `{errorMessage}, location: `{element.Span}`");
                    }

                    AppendToMetaAttributes(attrContainer.MetaAttributes.MetaList, metaAttr);
                }
            }
        }

        public void ParseAttributes(CXCursor cursor, ICppAttributeContainer attrContainer, bool needOnlineSeek = false)
        {
            //Try to handle annotate in cursor first
            //Low spend handle here, just open always
            attrContainer.Attributes.AddRange(ParseSystemAndAnnotateAttributeInCursor(cursor));

            //Low performance tokens handle here
            if (!ParseTokenAttributeEnabled) return;

            var tokenAttributes = new List<CppAttribute>();
            //Parse attributes online
            if (needOnlineSeek)
            {
                bool hasOnlineAttribute = CppTokenUtil.TryToSeekOnlineAttributes(cursor, out var onLineRange);
                if (hasOnlineAttribute)
                {
                    CppTokenUtil.ParseAttributesInRange(_rootContainerContext.Container as CppGlobalDeclarationContainer, cursor.TranslationUnit, onLineRange, ref tokenAttributes);
                }
            }

            //Parse attributes contains in cursor
            if (attrContainer is CppFunction)
            {
                var func = attrContainer as CppFunction;
                CppTokenUtil.ParseFunctionAttributes(_rootContainerContext.Container as CppGlobalDeclarationContainer, cursor, func.Name, ref tokenAttributes);
            }
            else
            {
                CppTokenUtil.ParseCursorAttributs(_rootContainerContext.Container as CppGlobalDeclarationContainer, cursor, ref tokenAttributes);
            }

            attrContainer.TokenAttributes.AddRange(tokenAttributes);
        }

        public void ParseTypedefAttribute(CXCursor cursor, CppType type, CppType underlyingTypeDefType)
        {
            if (type is CppTypedef typedef)
            {
                ParseAttributes(cursor, typedef, true);
                if (underlyingTypeDefType is CppClass targetClass)
                {
                    targetClass.Attributes.AddRange(typedef.Attributes);
                    TryToConvertAttributesToMetaAttributes(targetClass);
                }
            }
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

            ICppDeclarationContainer container = null;

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
                        container = (ICppDeclarationContainer)pair.Key.Parent;
                        _mapTemplateParameterTypeToTypedefKeys.Remove(pair.Key);
                        break;
                    }
                }
            }

            if (container != null)
            {
                container.Typedefs.Add((CppTypedef)type);
            }

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
            if (TryGetDeclarationContainer(cursor, data, out _, out var containerContext))
            {
                return (CppType)containerContext.Container;
            }

            // TODO: Pseudo fix, we are not supposed to land here, as the TryGet before should resolve an existing type already declared (but not necessarily defined)
            return GetCppType(type.CanonicalType.Declaration, type.CanonicalType, parent, data);
        }

        private static string GetCursorAsText(CXCursor cursor) => new CppTokenUtil.Tokenizer(cursor).TokensToString();

        private string GetCursorAsTextBetweenOffset(CXCursor cursor, int startOffset, int endOffset)
        {
            var tokenizer = new CppTokenUtil.Tokenizer(cursor);
            var builder = new StringBuilder();
            var previousTokenKind = CppTokenKind.Punctuation;
            for (int i = 0; i < tokenizer.Count; i++)
            {
                var token = tokenizer[i];
                if (previousTokenKind.IsIdentifierOrKeyword() && token.Kind.IsIdentifierOrKeyword())
                {
                    builder.Append(" ");
                }

                if (token.Span.Start.Offset >= startOffset && token.Span.End.Offset <= endOffset)
                {
                    builder.Append(token.Text);
                }
            }
            return builder.ToString();
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
            if (type.IsVolatileQualified)
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

                        var cppUnexposedType = new CppUnexposedType(CXUtil.GetTypeSpelling(type)) { SizeOf = (int)type.SizeOf };
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
                        CppTemplateParameterType templateArgType = null;
                        templateArgType = (CppTemplateParameterType)TryToCreateTemplateParametersObjC(cursor, data);

                        // Record that a typedef is using a template parameter type
                        // which will require to re-parent the typedef to the Obj-C interface it belongs to
                        if (_currentTypedefKey != default)
                        {
                            if (!_mapTemplateParameterTypeToTypedefKeys.TryGetValue(templateArgType, out var typedefKeys))
                            {
                                typedefKeys = new HashSet<CursorKey>();
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

        public void Unhandled(CXCursor cursor)
        {
            var cppLocation = cursor.GetSourceLocation();
            RootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}.", cppLocation);
        }

        public void WarningUnhandled(CXCursor cursor, CXCursor parent, CXType type)
        {
            var cppLocation = cursor.GetSourceLocation();
            if (cppLocation.Line == 0)
            {
                cppLocation = parent.GetSourceLocation();
            }
            RootCompilation.Diagnostics.Warning($"The type {cursor.Kind}/`{CXUtil.GetTypeSpelling(type)}` of kind `{CXUtil.GetTypeKindSpelling(type)}` is not supported in `{CXUtil.GetCursorSpelling(parent)}`", cppLocation);
        }

        public void WarningUnhandled(CXCursor cursor, CXCursor parent)
        {
            var cppLocation = cursor.GetSourceLocation();
            if (cppLocation.Line == 0)
            {
                cppLocation = parent.GetSourceLocation();
            }
            RootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)} in {CXUtil.GetCursorSpelling(parent)}.", cppLocation);
        }

        private List<CppType> ParseTemplateSpecializedArguments(CXCursor cursor, CXType type, CXClientData data)
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