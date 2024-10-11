namespace Salvavida.Generator
{
    public interface ICodeGenerator
    {
        bool CanGenerate(CodeGenerationContext ctx);
        string Generate(CodeGenerationContext ctx);
    }
}
