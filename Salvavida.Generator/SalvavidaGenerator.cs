using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Salvavida.Generator
{
    public record CodeGenerationContext
    {
        public CodeGenerationContext(ClassDeclarationSyntax classNode, Compilation compilation,
            SourceProductionContext spc, SemanticModel semanticModel, INamedTypeSymbol typeSymbol,
            LanguageVersion langVer, IDebug debugger)
        {
            ClassNode = classNode;
            Compilation = compilation;
            SourceProductionContext = spc;
            SemanticModel = semanticModel;
            TypeSymbol = typeSymbol;
            LanguageVersion = langVer;
            Debugger = debugger;
        }

        public ClassDeclarationSyntax ClassNode { get; }

        public Compilation Compilation { get; }

        public SourceProductionContext SourceProductionContext { get; }

        public SemanticModel SemanticModel { get; }

        public INamedTypeSymbol TypeSymbol { get; }

        public LanguageVersion LanguageVersion { get; }

        public IDebug Debugger { get; }
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
    public class SalvavidaGenerator : IIncrementalGenerator
    {
        internal const string DEBUG_FILE = "d:";
        public const string SALVAVIDA_ATTRIBUTE = "Salvavida.SavableAttribute";
        public const string SALVAVIDA_SAVE_SEPERATELY_ATTRIBUTE = "Salvavida.SaveSeparatelyAttribute";
        public const string MEMORY_PACKABLE_ATTRIBUTE = "MemoryPack.MemoryPackableAttribute";
        public const string BASIC_GENERATOR_NAME = "BasicCodeGenerator";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var generators = BuildGenerators();
            var defaultGenerator = new BasicCodeGenerator();
            var debugger = context.AdditionalTextsProvider
                .SelectMany((addText, token) =>
                {
                    var sourceText = addText.GetText(token);
                    if (sourceText == null)
                        return Array.Empty<string>();
                    var lines = sourceText.Lines;
                    return lines.Select((textLine, _) => sourceText.GetSubText(textLine.Span).ToString().Trim()).Where(t => !string.IsNullOrEmpty(t) && !t.StartsWith("#")).ToArray();
                })
                .Collect()
                .Select((lines, _) =>
                {
                    string? debugFile = null;
                    foreach (var l in lines)
                    {
                        if (l.StartsWith(DEBUG_FILE))
                        {
                            debugFile = l[DEBUG_FILE.Length..].Trim();
                        }
                    }
                    IDebug debug = string.IsNullOrEmpty(debugFile) ? new TraceDebug() : new SourceDebug(debugFile!);
                    return debug;
                });
            var parseOptions = context.ParseOptionsProvider.Select((options, _) =>
            {
                var csOptions = (CSharpParseOptions)options;
                return new GenerationConfig(generators, defaultGenerator, csOptions.LanguageVersion);
            });
            var source = context.SyntaxProvider.ForAttributeWithMetadataName(SALVAVIDA_ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ctx.TargetNode as ClassDeclarationSyntax)
                .Where(static n => n is not null)
                .Combine(context.CompilationProvider)
                .Combine(parseOptions)
                .Combine(debugger);
            context.RegisterSourceOutput(source, static (spc, src) =>
            {
                var ((classNode, compilation), config) = src.Left;
                var debugger = src.Right;
                Execute(classNode!, compilation, spc, config, debugger);


            });

            context.RegisterSourceOutput(debugger, static (spc, src) =>
            {
                if (src is not SourceDebug sd)
                    return;
                var text = sd.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    text = "/*\n" + text + "\n*/";
                    spc.AddSource(sd.SourceFile, text);
                }
            });
        }

        static void Execute(ClassDeclarationSyntax classNode, Compilation compilation, SourceProductionContext spc,
            GenerationConfig config, IDebug debugger)
        {
            var semanticModel = compilation.GetSemanticModel(classNode.SyntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(classNode);
            if (typeSymbol == null)
                return;

            var ctx = new CodeGenerationContext(classNode, compilation, spc, semanticModel, typeSymbol, config.LanguageVersion, debugger);

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
            debugger.Log($"[[Generation Start: {fileName}]]");
            string? code = null;
            try
            {
                code = generator.Generate(ctx);
            }
            catch (Exception x)
            {
                debugger.Log(x.ToString());
            }

            if (!string.IsNullOrEmpty(code))
            {
                var log = $"Generated Code:\r\n{code ?? "[empty]"}";
                debugger.Log(log);
                spc.AddSource(fileName, code!);
            }
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
