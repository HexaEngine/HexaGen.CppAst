using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Parsing.Visitors.MemberVisitors;

namespace HexaGen.CppAst.Parsing
{
    public class CursorVisitorRegistry<TVisitor, TResult> where TVisitor : CursorVisitor<TResult>
    {
        private readonly Dictionary<CXCursorKind, TVisitor> visitors = [];
        private readonly Dictionary<Type, TVisitor> visitorTypes = [];

        public T Register<T>() where T : TVisitor, new()
        {
            var t = new T();
            Register(t);
            return t;
        }

        public void Register<T>(T visitor) where T : TVisitor
        {
            visitorTypes.Add(typeof(T), visitor);
            foreach (var kind in visitor.Kinds)
            {
                visitors[kind] = visitor;
            }
        }

        public void Override<T>(TVisitor visitor) where T : TVisitor
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
        }

        public void Unregister<T>() where T : TVisitor
        {
            var old = GetVisitor<T>();
            foreach (var kind in old.Kinds)
            {
                visitors.Remove(kind);
            }
            visitorTypes.Remove(typeof(T));
        }

        public T GetVisitor<T>() where T : TVisitor
        {
            return (T)visitorTypes[typeof(T)];
        }

        public TVisitor? GetVisitorByKind(CXCursorKind kind)
        {
            visitors.TryGetValue(kind, out var visitor);
            return visitor;
        }
    }

    public static class MemberVisitorRegistry
    {
        private static readonly CursorVisitorRegistry<MemberVisitor, CppElement> registry = new();

        static MemberVisitorRegistry()
        {
            Register<ClassStructUnionObjCVisitor>();
            Register<CXXAccessSpecifierVisitor>();
            Register<CXXBaseSpecifierVisitor>();
            Register<EnumConstantVisitor>();
            Register<EnumDeclMemberVisitor>();
            Register<FieldVariableVisitor>();
            Register<FlagEnumVisitor>();
            Register<FunctionDeclVisitor>();
            Register<InclusionDirectiveVisitor>();
            Register<LinkageSpecVisitor>();
            Register<MacroDefinitionVisitor>();
            Register<NamespaceMemberVisitor>();
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

        public static T GetVisitor<T>() where T : MemberVisitor => registry.GetVisitor<T>();

        public static MemberVisitor Register<T>() where T : MemberVisitor, new() => registry.Register<T>();

        public static void Register<T>(T visitor) where T : MemberVisitor => registry.Register(visitor);

        public static void Override<T>(MemberVisitor visitor) where T : MemberVisitor => registry.Override<T>(visitor);

        public static void Unregister<T>() where T : MemberVisitor => registry.Unregister<T>();

        public static MemberVisitor? GetVisitor(CXCursorKind kind) => registry.GetVisitorByKind(kind);
    }
}