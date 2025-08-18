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

        protected override void HandleField(ScriptBuilder sb, IFieldSymbol field, CodeGenerationContext ctx)
        {
            AttributeData? includeAttr = null;
            AttributeData? mpIgnoreAttr = null;
            AttributeData? svIgnoreAttr = null;
            AttributeData? saveSeparatelyAttr = null;

            foreach (var attrData in field.GetAttributes())
            {
                var attr = attrData.AttributeClass;
                var attrName = attr!.ToDisplayString();
                switch (attrName)
                {
                    case MP_INCLUDE_ATTRIBUTE:
                        includeAttr = attrData;
                        break;
                    case MP_IGNORE_ATTRIBUTE:
                        mpIgnoreAttr = attrData;
                        break;
                    case CodeGenHelper.IGNORE_ATTRIBUTE_NAME:
                        svIgnoreAttr = attrData;
                        break;
                    case CodeGenHelper.SAVE_SEPARATELY_ATTRIBUTE_NAME:
                        saveSeparatelyAttr = attrData;
                        break;
                }
            }

            if (CheckHasProblem(ctx, field, field.Name, includeAttr, mpIgnoreAttr, saveSeparatelyAttr))
                return;
            base.HandleField(sb, field, ctx);
        }

        protected override void HandleProperty(ScriptBuilder sb, IPropertySymbol prop, CodeGenerationContext ctx)
        {
            AttributeData? includeAttr = null;
            AttributeData? mpIgnoreAttr = null;
            AttributeData? saveSeparatelyAttr = null;
            foreach (var attr in prop.GetAttributes())
            {
                var attrName = attr.AttributeClass!.ToDisplayString();
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

            if (CheckHasProblem(ctx, prop, prop.Name, includeAttr, mpIgnoreAttr, saveSeparatelyAttr))
                return;
            base.HandleProperty(sb, prop, ctx);
        }

        private bool CheckHasProblem(CodeGenerationContext ctx, ISymbol member, string name,
            AttributeData? includeAttr, AttributeData? mpIgnoreAttr, AttributeData? saveSeparatelyAttr)
        {
            if (mpIgnoreAttr != null && saveSeparatelyAttr == null)
                return true;

            var problematicLocation = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();

            if (member.DeclaredAccessibility == Accessibility.Public)
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