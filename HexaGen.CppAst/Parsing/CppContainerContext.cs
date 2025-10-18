using HexaGen.CppAst.Model;
using HexaGen.CppAst.Model.Interfaces;

namespace HexaGen.CppAst.Parsing
{
    public class CppContainerContext
    {
        public CppContainerContext(ICppContainer container, CppContainerContextType type)
        {
            Container = container;
            Type = type;
        }

        public ICppContainer Container;

        public ICppDeclarationContainer DeclarationContainer => (ICppDeclarationContainer)Container;

        public CppVisibility CurrentVisibility;

        public CppContainerContextType Type { get; }

        public bool IsChildrenVisited;
    }
}