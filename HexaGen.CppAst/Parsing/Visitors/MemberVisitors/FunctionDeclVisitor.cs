namespace HexaGen.CppAst.Parsing.Visitors.MemberVisitors
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Model.Types;
    using HexaGen.CppAst.Utilities;
    using System.Collections.Generic;
    using System.Diagnostics;

    public unsafe class FunctionDeclVisitor : MemberVisitor
    {
        public override IEnumerable<CXCursorKind> Kinds { get; } = [
            CXCursorKind.CXCursor_FunctionTemplate,
                CXCursorKind.CXCursor_FunctionDecl,
                CXCursorKind.CXCursor_Constructor,
                CXCursorKind.CXCursor_Destructor,
                CXCursorKind.CXCursor_CXXMethod,
                CXCursorKind.CXCursor_ObjCClassMethodDecl,
                CXCursorKind.CXCursor_ObjCInstanceMethodDecl
        ];

        protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data)
        {
            var contextContainer = Builder.GetOrCreateDeclContainer(cursor.SemanticParent, data);
            var container = contextContainer.DeclarationContainer;

            if (container == null)
            {
                Builder.WarningUnhandled(cursor, parent);
                return null;
            }

            var cppClass = container as CppClass;
            var functionName = CXUtil.GetCursorSpelling(cursor);

            //We need ignore the function define out in the class definition here(Otherwise it will has two same functions here~)!
            var semKind = cursor.SemanticParent.Kind;
            if ((semKind == CXCursorKind.CXCursor_StructDecl ||
                semKind == CXCursorKind.CXCursor_ClassDecl ||
                semKind == CXCursorKind.CXCursor_ClassTemplate)
                && cursor.LexicalParent != cursor.SemanticParent)
            {
                return null;
            }

            var cppFunction = new CppFunction(functionName)
            {
                Visibility = cursor.GetVisibility(),
                StorageQualifier = cursor.GetStorageQualifier(),
                LinkageKind = cursor.GetLinkageKind(),
            };

            if (cursor.Kind == CXCursorKind.CXCursor_Constructor)
            {
                Debug.Assert(cppClass != null);
                cppFunction.IsConstructor = true;
                cppClass.Constructors.Add(cppFunction);
            }
            else if (cursor.Kind == CXCursorKind.CXCursor_Destructor)
            {
                Debug.Assert(cppClass != null);
                cppFunction.IsDestructor = true;
                cppClass.Destructors.Add(cppFunction);
            }
            else
            {
                container.Functions.Add(cppFunction);
            }

            switch (cursor.Kind)
            {
                case CXCursorKind.CXCursor_FunctionTemplate:
                    cppFunction.Flags |= CppFunctionFlags.FunctionTemplate;
                    //Handle template argument here~
                    cursor.VisitChildren((childCursor, funcCursor, clientData) =>
                    {
                        var tmplParam = Builder.TryToCreateTemplateParameters(childCursor, clientData);
                        if (tmplParam != null)
                        {
                            cppFunction.TemplateParameters.Add(tmplParam);
                        }
                        return CXChildVisitResult.CXChildVisit_Continue;
                    }, new CXClientData((nint)data));
                    break;

                case CXCursorKind.CXCursor_ObjCInstanceMethodDecl:
                case CXCursorKind.CXCursor_CXXMethod:
                    cppFunction.Flags |= CppFunctionFlags.Method;
                    break;

                case CXCursorKind.CXCursor_ObjCClassMethodDecl:
                    cppFunction.Flags |= CppFunctionFlags.ClassMethod;
                    break;

                case CXCursorKind.CXCursor_Constructor:
                    cppFunction.Flags |= CppFunctionFlags.Constructor;
                    break;

                case CXCursorKind.CXCursor_Destructor:
                    cppFunction.Flags |= CppFunctionFlags.Destructor;
                    break;
            }

            if (cursor.IsFunctionInlined)
            {
                cppFunction.Flags |= CppFunctionFlags.Inline;
            }

            if (cursor.IsVariadic)
            {
                cppFunction.Flags |= CppFunctionFlags.Variadic;
            }

            if (cursor.CXXMethod_IsConst)
            {
                cppFunction.Flags |= CppFunctionFlags.Const;
            }
            if (cursor.CXXMethod_IsDefaulted)
            {
                cppFunction.Flags |= CppFunctionFlags.Defaulted;
            }
            if (cursor.CXXMethod_IsVirtual)
            {
                cppFunction.Flags |= CppFunctionFlags.Virtual;
            }
            if (cursor.CXXMethod_IsPureVirtual)
            {
                cppFunction.Flags |= CppFunctionFlags.Pure | CppFunctionFlags.Virtual;
            }
            if (clang.CXXMethod_isDeleted(cursor) != 0)
            {
                cppFunction.Flags |= CppFunctionFlags.Deleted;
            }

            // Gets the return type
            var returnType = Builder.GetCppType(cursor.ResultType.Declaration, cursor.ResultType, cursor, data);
            if (cppClass != null && cppClass.ClassKind == CppClassKind.ObjCInterface)
            {
                if (returnType is CppTypedef typedef && typedef.Name == "instancetype")
                {
                    returnType = new CppPointerType(cppClass);
                }
            }
            cppFunction.ReturnType = returnType;

            Builder.ParseAttributes(cursor, cppFunction, true);
            cppFunction.CallingConvention = cursor.Type.GetCallingConvention();

            int i = 0;
            cursor.VisitChildren((argCursor, functionCursor, clientData) =>
            {
                switch (argCursor.Kind)
                {
                    case CXCursorKind.CXCursor_ParmDecl:
                        var argName = CXUtil.GetCursorSpelling(argCursor);

                        var parameter = new CppParameter(Builder.GetCppType(argCursor.Type.Declaration, argCursor.Type, argCursor, clientData), argName);

                        cppFunction.Parameters.Add(parameter);

                        // Visit default parameter value
                        Builder.VisitInitValue(argCursor, out var paramExpr, out var paramValue);
                        parameter.InitValue = paramValue;
                        parameter.InitExpression = paramExpr;

                        i++;
                        break;

                    // Don't generate a warning for unsupported cursor
                    default:
                        //// Attributes should be parsed by ParseAttributes()
                        //if (!(argCursor.Kind >= CXCursorKind.CXCursor_FirstAttr && argCursor.Kind <= CXCursorKind.CXCursor_LastAttr))
                        //{
                        //    WarningUnhandled(cursor, parent);
                        //}
                        break;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            }, new CXClientData((nint)data));

            return cppFunction;
        }
    }
}