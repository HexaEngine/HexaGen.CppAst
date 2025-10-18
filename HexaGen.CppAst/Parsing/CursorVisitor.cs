using ClangSharp.Interop;
using HexaGen.CppAst.Model.Metadata;

namespace HexaGen.CppAst.Parsing
{
    public abstract class CursorVisitor<TResult>
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

        public unsafe TResult Visit(CppModelContext context, CXCursor cursor, CXCursor parent)
        {
            Context = context;
            if (CreateContainerContext)
            {
                Container = Builder.GetOrCreateDeclContainer(parent);
            }
            return VisitCore(cursor, parent);
        }

        protected abstract unsafe TResult VisitCore(CXCursor cursor, CXCursor parent);
    }
}