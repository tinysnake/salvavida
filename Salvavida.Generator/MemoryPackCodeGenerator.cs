using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Salvavida.Generator
{
    public class MemoryPackCodeGenerator : BasicCodeGenerator
    {
        public const string MP_ATTRIBUTE = "MemoryPack.MemoryPackableAttribute";
        public const string MP_INCLUDE_ATTRIBUTE = "MemoryPack.MemoryPackIncludeAttribute";
        public const string MP_IGNORE_ATTRIBUTE = "MemoryPack.MemoryPackIgnoreAttribute";

        public override bool CanGenerate(CodeGenerationContext ctx)
        {
            var attr = CodeGenHelper.TryGetAttribute(ctx, ctx.ClassNode.AttributeLists, MP_ATTRIBUTE);
            return attr != null;
        }

        protected override void HandleField(ScriptBuilder sb, FieldDeclarationSyntax field, CodeGenerationContext ctx)
        {
            AttributeSyntax? includeAttr = null;
            AttributeSyntax? mpIgnoreAttr = null;
            AttributeSyntax? svIgnoreAttr = null;
            AttributeSyntax? saveSeparatelyAttr = null;
            foreach (var attrList in field.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    if (ctx.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                        continue;
                    var attrName = attrSymbol.ContainingType.ToDisplayString();
                    switch (attrName)
                    {
                        case MP_INCLUDE_ATTRIBUTE:
                            includeAttr = attr;
                            break;
                        case MP_IGNORE_ATTRIBUTE:
                            mpIgnoreAttr = attr;
                            break;
                        case CodeGenHelper.IGNORE_ATTRIBUTE_NAME:
                            svIgnoreAttr = attr;
                            break;
                        case CodeGenHelper.SAVE_SEPARATELY_ATTRIBUTE_NAME:
                            saveSeparatelyAttr = attr;
                            break;
                    }
                }
            }
            if (CheckHasProblem(ctx, field, field.Declaration.GetLocation(), field.Declaration.Variables.ToString(),
                includeAttr, mpIgnoreAttr, saveSeparatelyAttr))
                return;
            base.HandleField(sb, field, ctx);
        }

        protected override void HandleProperty(ScriptBuilder sb, PropertyDeclarationSyntax prop, CodeGenerationContext ctx)
        {
            AttributeSyntax? includeAttr = null;
            AttributeSyntax? mpIgnoreAttr = null;
            AttributeSyntax? saveSeparatelyAttr = null;
            foreach (var attrList in prop.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    if (ctx.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                        continue;
                    var attrName = attrSymbol.ContainingType.ToDisplayString();
                    switch (attrName)
                    {
                        case MP_INCLUDE_ATTRIBUTE:
                            includeAttr = attr;
                            break;
                        case MP_IGNORE_ATTRIBUTE:
                            mpIgnoreAttr = attr;
                            break;
                        case CodeGenHelper.SAVE_SEPARATELY_ATTRIBUTE_NAME:
                            saveSeparatelyAttr = attr;
                            break;
                    }
                }
            }
            if (CheckHasProblem(ctx, prop, prop.Identifier.GetLocation(), prop.Identifier.ToString(),
                includeAttr, mpIgnoreAttr, saveSeparatelyAttr))
                return;
            base.HandleProperty(sb, prop, ctx);
        }

        private bool CheckHasProblem(CodeGenerationContext ctx, MemberDeclarationSyntax member, Location problematicLocation, string name,
            AttributeSyntax? includeAttr, AttributeSyntax? mpIgnoreAttr, AttributeSyntax? saveSeparatelyAttr)
        {
            if (mpIgnoreAttr != null && saveSeparatelyAttr == null)
                return true;

            if (member.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)))
            {
                if (saveSeparatelyAttr != null)
                {
                    if (mpIgnoreAttr == null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MP_IgnoreAttributeRequired,
                            problematicLocation, name));
                        return true;
                    }
                }
                else
                {
                    ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MP_PrivateKeywoardRecommended,
                        problematicLocation, name));
                }
            }
            else
            {
                if (saveSeparatelyAttr != null)
                {
                    if (includeAttr != null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MP_HaveToRemoveIncludeAttribute,
                            problematicLocation, name));
                        return true;
                    }
                }
                else
                {
                    if (includeAttr == null)
                    {
                        if (mpIgnoreAttr == null)
                        {
                            ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MP_ForgetMemoryPackableIncludeAttribute,
                                problematicLocation, name));
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        protected override void AddAttributePreventSerialize(ScriptBuilder sb, bool isOnProperty)
        {
            sb.WriteLine("[MemoryPack.MemoryPackIgnore]");
        }
    }
}
