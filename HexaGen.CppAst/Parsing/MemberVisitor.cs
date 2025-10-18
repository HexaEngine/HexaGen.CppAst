using ClangSharp.Interop;
using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Metadata;
using System.Runtime.CompilerServices;

namespace HexaGen.CppAst.Parsing
{
    public abstract class MemberVisitor : CursorVisitor
    {
    }

    public abstract class CursorVisitor
    {
        public CppModelContext Context { get; internal set; } = null!;

        public CppModelBuilder Builder => Context.Builder;

        public CppContainerContext Container { get; internal set; } = null!;

        public CppContainerContext CurrentRootContainer => Builder.CurrentRootContainer;

        public TypedefResolver TypedefResolver => Builder.TypedefResolver;

        public CppCompilation RootCompilation => Builder.RootCompilation;

        public abstract IEnumerable<CXCursorKind> Kinds { get; }

        public virtual bool CreateContainerContext => false;

        public virtual CXChildVisitResult VisitResult { get; } = CXChildVisitResult.CXChildVisit_Continue;

        public unsafe CppElement? Visit(CppModelContext context, CXCursor cursor, CXCursor parent, void* data)
        {
            Context = context;
            if (CreateContainerContext)
            {
                Container = Builder.GetOrCreateDeclarationContainer(parent, data);
            }
            return VisitCore(cursor, parent, data);
        }

        protected abstract unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent, void* data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetVisitor<T>() where T : CursorVisitor
        {
            return CursorVisitorRegistry.GetVisitor<T>();
        }
    }
}