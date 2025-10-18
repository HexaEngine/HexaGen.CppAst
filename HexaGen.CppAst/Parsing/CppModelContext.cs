namespace HexaGen.CppAst.Parsing
{
    using ClangSharp.Interop;
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Model.Metadata;
    using HexaGen.CppAst.Model.Templates;
    using System.Runtime.InteropServices;

    public unsafe partial class CppModelContext
    {
        private readonly Dictionary<CursorKey, CppContainerContext> _containers;

        public CppModelContext(Dictionary<CursorKey, CppContainerContext> containers, CppCompilation rootCompilation, CppModelBuilder builder)
        {
            _containers = containers;
            RootCompilation = rootCompilation;
            Builder = builder;
        }

        public CppCompilation RootCompilation { get; }

        public CppModelBuilder Builder { get; }

        public CppClass? CurrentClassBeingVisited { get; set; }

        public Dictionary<CppTemplateParameterType, HashSet<CursorKey>> MapTemplateParameterTypeToTypedefKeys { get; } = [];

        public CursorKey CurrentTypedefKey { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate CXChildVisitResult CXCursorBlockVisitor(CXCursor cursor, CXCursor parent);
}