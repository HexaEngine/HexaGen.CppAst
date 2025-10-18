using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Interfaces;

namespace HexaGen.CppAst.Parsing
{
    public class CppContainerContext
    {
        public CppContainerContext(ICppContainer container, CppContainerContextType type, CppVisibility visibility = CppVisibility.Default)
        {
            Container = container;
            Type = type;
            CurrentVisibility = visibility;
        }

        public CppContainerContext(ICppContainer container, CppVisibility visibility = CppVisibility.Default)
        {
            Container = container;
            Type = CppContainerContextType.Unspecified;
            CurrentVisibility = visibility;
        }

        public ICppContainer Container { get; }

        public ICppDeclarationContainer DeclarationContainer => (ICppDeclarationContainer)Container;

        public ICppGlobalDeclarationContainer GlobalDeclarationContainer => (ICppGlobalDeclarationContainer)Container;

        public CppVisibility CurrentVisibility { get; set; }

        public CppContainerContextType Type { get; }

        public bool IsChildrenVisited { get; set; }
    }
}