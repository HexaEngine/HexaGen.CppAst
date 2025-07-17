namespace TestApp
{
    using HexaGen.CppAst.Parsing;

    internal class Program
    {
        private static void Main(string[] args)
        {
            var opt = new CppParserOptions() { AutoSquashTypedef = false };
            opt.ConfigureForWindowsMsvc(CppTargetCpu.X86_64);
            var res = CppParser.Parse("#include <stddef.h>\nsize_t Foo();", opt);
        }
    }
}