namespace TestApp
{
    using HexaGen.CppAst.Model.Declarations;
    using HexaGen.CppAst.Parsing;

    internal class Program
    {
        private static void Main(string[] args)
        {
            var opt = new CppParserOptions() { AutoSquashTypedef = false };
            opt.ConfigureForWindowsMsvc(CppTargetCpu.X86_64);
            using var res = CppParser.Parse(File.ReadAllText("test1.h"), opt);

            Dictionary<string, CppEnum> enums = res.Enums.ToDictionary(x => x.Name.AsSpan().TrimEnd('_').TrimEnd("_t").ToString());
            foreach (var typedef in res.Typedefs)
            {
                if (enums.TryGetValue(typedef.Name, out var enumDecl))
                {
                    Console.WriteLine($"Typedef '{typedef.Name}' maps to Enum '{enumDecl.Name}'");
                }
            }
        }
    }
}