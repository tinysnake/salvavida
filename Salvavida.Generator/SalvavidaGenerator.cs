using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Salvavida.Generator
{
    public record CodeGenerationContext
    {
        public CodeGenerationContext(ClassDeclarationSyntax classNode, Compilation compilation,
            SourceProductionContext spc, SemanticModel semanticModel, INamedTypeSymbol typeSymbol,
            LanguageVersion langVer, string? debugOutputFile)
        {
            ClassNode = classNode;
            Compilation = compilation;
            SourceProductionContext = spc;
            SemanticModel = semanticModel;
            TypeSymbol = typeSymbol;
            LanguageVersion = langVer;
            DebugOutputFile = debugOutputFile;

        }

        public ClassDeclarationSyntax ClassNode { get; }

        public Compilation Compilation { get; }

        public SourceProductionContext SourceProductionContext { get; }

        public SemanticModel SemanticModel { get; }

        public INamedTypeSymbol TypeSymbol { get; }

        public LanguageVersion LanguageVersion { get; }

        public string? DebugOutputFile { get; }
    }

    public record GenerationConfig
    {
        public GenerationConfig(IEnumerable<ICodeGenerator> generators, ICodeGenerator defaultGenerator,
            LanguageVersion langVer)
        {
            Generators = generators;
            DefaultGenerator = defaultGenerator;
            LanguageVersion = langVer;
        }

        public IEnumerable<ICodeGenerator> Generators { get; }
        public ICodeGenerator DefaultGenerator { get; }
        public LanguageVersion LanguageVersion { get; }
    }

    [Generator(LanguageNames.CSharp)]
#pragma warning disable RS1036 // Specify analyzer banned API enforcement setting
    public class SalvavidaGenerator : IIncrementalGenerator
#pragma warning restore RS1036 // Specify analyzer banned API enforcement setting
    {
        public const string SALVAVIDA_ATTRIBUTE = "Salvavida.SavableAttribute";
        public const string SALVAVIDA_SAVE_SEPERATELY_ATTRIBUTE = "Salvavida.SaveSeparatelyAttribute";
        public const string MEMORY_PACKABLE_ATTRIBUTE = "MemoryPack.MemoryPackableAttribute";
        public const string BASIC_GENERATOR_NAME = "BasicCodeGenerator";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            Trace.WriteLine("begin");
            var generators = BuildGenerators();
            var defaultGenerator = new BasicCodeGenerator();
            var debugOutputFile = context.AnalyzerConfigOptionsProvider.Select((config, _) =>
            {
                if (config.GlobalOptions.TryGetValue("build_property.Salvavida_Generator_Debug_Output", out var outputFile))
                    return outputFile;
                return null;
            });
            var paseOptions = context.ParseOptionsProvider.Select((options, _) =>
            {
                var csOptions = (CSharpParseOptions)options;
                return new GenerationConfig(generators, defaultGenerator, csOptions.LanguageVersion);
            });
            var source = context.SyntaxProvider.ForAttributeWithMetadataName(SALVAVIDA_ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ctx.TargetNode as ClassDeclarationSyntax)
                .Where(static n => n is not null)
                .Combine(debugOutputFile)
                .Combine(context.CompilationProvider)
                .Combine(paseOptions);
            context.RegisterSourceOutput(source, static (spc, src) =>
            {
                var ((classNode, debugOutput), compilation) = src.Left;
                var config = src.Right;
                Execute(classNode!, compilation, spc, config, debugOutput);
            });
            Trace.WriteLine("end");
        }

        static void Execute(ClassDeclarationSyntax classNode, Compilation compilation, SourceProductionContext spc, 
            GenerationConfig config, string? debugOutputFile)
        {
            var sb = debugOutputFile != null ? new StringBuilder() : null;
            var semanticModel = compilation.GetSemanticModel(classNode.SyntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(classNode);
            if (typeSymbol == null)
                return;

            var ctx = new CodeGenerationContext(classNode, compilation, spc, semanticModel, typeSymbol, config.LanguageVersion, debugOutputFile);

            if (!IsPartial(classNode))
            {
                spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MustBePartial, classNode.Identifier.GetLocation(), typeSymbol.Name));
                return;
            }

            if (IsNested(classNode))
            {
                spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NestingNotAllowed, classNode.Identifier.GetLocation(), typeSymbol.Name));
                return;
            }

            ICodeGenerator? generator = null;
            foreach (var gen in config.Generators)
            {
                if (gen.CanGenerate(ctx))
                {
                    if (generator == null)
                        generator = gen;
                    else
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.TooManySerializers, classNode.Identifier.GetLocation(), typeSymbol.Name));
                        return;
                    }
                }
            }
            generator ??= config.DefaultGenerator;

            var fileName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_");

            fileName += ".sv.g.cs";
            string? code = null;
            Exception? ex = null;
            try
            {
                code = generator.Generate(ctx);
            }
            catch (Exception x)
            {
                ex = x;
            }

            if (sb != null)
            {
                sb.AppendLine(fileName);
                if (ex == null)
                    sb.AppendLine(code??"[empty]");
                else
                    sb.AppendLine(ex.ToString());
                sb.AppendLine("=============================");
                sb.AppendLine();
                File.AppendAllText(debugOutputFile, sb.ToString());
            }

            if (!string.IsNullOrEmpty(code))
                spc.AddSource(fileName, code!);
        }

        public static bool ClassHasAttribute(ClassDeclarationSyntax classNode, SemanticModel semanticModel, string attributeName)
        {
            var attrsList = classNode.AttributeLists;
            foreach (var attrs in attrsList)
            {
                foreach (var attrNode in attrs.Attributes)
                {
                    if (semanticModel.GetSymbolInfo(attrNode).Symbol is not IMethodSymbol attrSymbol)
                        continue;
                    var attrName = attrSymbol.ContainingType.ToDisplayString();
                    if (attrName == attributeName)
                        return true;
                }
            }
            return false;
        }

        static List<ICodeGenerator> BuildGenerators()
        {
            var generators = new List<ICodeGenerator>();
            var types = typeof(ICodeGenerator).Assembly.GetTypes();
            foreach (var type in types)
            {
                if (typeof(ICodeGenerator).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                {
                    if (type.Name == BASIC_GENERATOR_NAME)
                        continue;
                    var ctor = type.GetConstructor(new Type[0]);
                    if (ctor?.Invoke(null) is ICodeGenerator generator)
                        generators.Add(generator);
                }
            }
            foreach (var gen in generators)
            {
                Trace.WriteLine("generator: " + gen.GetType().FullName);
            }
            return generators;
        }

        static bool IsPartial(ClassDeclarationSyntax typeDeclaration)
        {
            return typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }

        static bool IsNested(TypeDeclarationSyntax typeDeclaration)
        {
            return typeDeclaration.Parent is TypeDeclarationSyntax;
        }
    }
}
