using ClangSharp.Interop;
using HexaGen.CppAst.Parsing.Visitors.MemberVisitors;

namespace HexaGen.CppAst.Parsing
{
    public static class CursorVisitorRegistry
    {
        private static readonly Dictionary<Type, CursorVisitor> visitorTypes = [];

        public static void Register<T>() where T : CursorVisitor, new()
        {
            Register(new T());
        }

        public static void Register<T>(T visitor) where T : CursorVisitor
        {
            visitorTypes.Add(typeof(T), visitor);
        }

        public static void Override<T>(CursorVisitor visitor) where T : CursorVisitor
        {
            visitorTypes[typeof(T)] = visitor;
        }

        public static void Unregister<T>() where T : MemberVisitor
        {
            visitorTypes.Remove(typeof(T));
        }

        public static T GetVisitor<T>() where T : CursorVisitor
        {
            return (T)visitorTypes[typeof(T)];
        }
    }

    public static class MemberVisitorRegistry
    {
        private static readonly Dictionary<CXCursorKind, MemberVisitor> visitors = [];
        private static readonly Dictionary<Type, MemberVisitor> visitorTypes = [];

        static MemberVisitorRegistry()
        {
            Register<ClassStructUnionObjCVisitor>();
            Register<CXXAccessSpecifierVisitor>();
            Register<CXXBaseSpecifierVisitor>();
            Register<EnumConstantVisitor>();
            Register<EnumDeclVisitor>();
            Register<FieldVariableVisitor>();
            Register<FlagEnumVisitor>();
            Register<FunctionDeclVisitor>();
            Register<InclusionDirectiveVisitor>();
            Register<LinkageSpecVisitor>();
            Register<MacroDefinitionVisitor>();
            Register<NamespaceVisitor>();
            Register<ObjCClassProtocolRefVisitor>();
            Register<ObjCPropertyDeclVisitor>();
            Register<TypeAliasDeclVisitor>();
            Register<TypedefDeclVisitor>();
            Register<TypeRefVisitor>();
            Register<UsingDirectiveVisitor>();
            Register<MacroExpansionVisitor>();
            Register<FirstRefVisitor>();
            Register<ObjCIvarDeclVisitor>();
            Register<TemplateTypeParameterVisitor>();
        }

        public static T GetVisitor<T>() where T : MemberVisitor
        {
            return (T)visitorTypes[typeof(T)];
        }

        public static void Register<T>() where T : MemberVisitor, new()
        {
            Register(new T());
        }

        public static void Register<T>(T visitor) where T : MemberVisitor
        {
            visitorTypes.Add(typeof(T), visitor);
            foreach (var kind in visitor.Kinds)
            {
                visitors[kind] = visitor;
            }
            CursorVisitorRegistry.Register(visitor);
        }

        public static void Override<T>(MemberVisitor visitor) where T : MemberVisitor
        {
            var old = GetVisitor<T>();
            foreach (var kind in old.Kinds)
            {
                visitors.Remove(kind);
            }
            visitorTypes[typeof(T)] = visitor;
            foreach (var kind in visitor.Kinds)
            {
                visitors[kind] = visitor;
            }
            CursorVisitorRegistry.Override<T>(visitor);
        }

        public static void Unregister<T>() where T : MemberVisitor
        {
            var old = GetVisitor<T>();
            foreach (var kind in old.Kinds)
            {
                visitors.Remove(kind);
            }
            visitorTypes.Remove(typeof(T));
            CursorVisitorRegistry.Unregister<T>();
        }

        public static MemberVisitor? GetVisitor(CXCursorKind kind)
        {
            visitors.TryGetValue(kind, out var visitor);
            return visitor;
        }
    }
}