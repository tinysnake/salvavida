using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Salvavida.Generator
{
    internal class SystemTextJsonCodeGenerator : BasicCodeGenerator
    {
        public const string USE_JSON_ATTRIBUTE = "Salvavida.UseSystemTextJsonAttribute";
        public const string JSON_IGNORE_ATTRIBUTE = "System.Text.Json.Serialization.JsonIgnoreAttribute";
        public const string JSON_INCLUDE_ATTRIBUTE = "System.Text.Json.Serialization.JsonIncludeAttribute";

        public override bool CanGenerate(CodeGenerationContext ctx)
        {
            var attr = CodeGenHelper.TryGetAttribute(ctx, ctx.ClassNode.AttributeLists, USE_JSON_ATTRIBUTE);
            return attr != null;
        }

        protected override void HandleField(ScriptBuilder sb, IFieldSymbol field, CodeGenerationContext ctx)
        {
            AttributeData? includeAttr = null;
            AttributeData? ignoreAttr = null;
            AttributeData? saveSeparatelyAttr = null;
            foreach (var attr in field.GetAttributes())
            {
                var attrName = attr.AttributeClass!.ToDisplayString();
                switch (attrName)
                {
                    case JSON_IGNORE_ATTRIBUTE:
                        ignoreAttr = attr;
                        break;
                    case JSON_INCLUDE_ATTRIBUTE:
                        includeAttr = attr;
                        break;
                    case CodeGenHelper.IGNORE_ATTRIBUTE_NAME:
                        ignoreAttr = attr;
                        break;
                    case CodeGenHelper.SAVE_SEPARATELY_ATTRIBUTE_NAME:
                        saveSeparatelyAttr = attr;
                        break;
                }
            }

            if (CheckHasProblem(ctx, field, field.Name, includeAttr, ignoreAttr, saveSeparatelyAttr))
                return;

            base.HandleField(sb, field, ctx);
        }

        protected override void HandleProperty(ScriptBuilder sb, IPropertySymbol prop, CodeGenerationContext ctx)
        {            
            AttributeData? includeAttr = null;
            AttributeData? ignoreAttr = null;
            AttributeData? saveSeparatelyAttr = null;
            foreach (var attr in prop.GetAttributes())
            {
                var attrName = attr.AttributeClass!.ToDisplayString();
                switch (attrName)
                {
                    case JSON_IGNORE_ATTRIBUTE:
                        ignoreAttr = attr;
                        break;
                    case JSON_INCLUDE_ATTRIBUTE:
                        includeAttr = attr;
                        break;
                    case CodeGenHelper.IGNORE_ATTRIBUTE_NAME:
                        ignoreAttr = attr;
                        break;
                    case CodeGenHelper.SAVE_SEPARATELY_ATTRIBUTE_NAME:
                        saveSeparatelyAttr = attr;
                        break;
                }
            }

            if (CheckHasProblem(ctx, prop, prop.Name, includeAttr, ignoreAttr, saveSeparatelyAttr))
                return;

            base.HandleProperty(sb, prop, ctx);
        }

        protected bool CheckHasProblem(CodeGenerationContext ctx, ISymbol member, string name,
            AttributeData? includeAttr, AttributeData? ignoreAttr, AttributeData? saveSeparatelyAttr)
        {
            var location = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
            if (member.DeclaredAccessibility == Accessibility.Public)
            {
                if (saveSeparatelyAttr != null)
                {
                    if (ignoreAttr == null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.STJ_JsonIgnoreAttributeRequired,
                            location, name));
                        return true;
                    }
                }
                else
                {
                    ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.STJ_PrivateKeywoardRecommended,
                        location, name));
                }
            }
            else
            {
                if (saveSeparatelyAttr != null)
                {
                    if (includeAttr != null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.STJ_HaveToRemoveJsonIncludeAttribute,
                            location, name));
                        return true;
                    }
                }
                else
                {
                    if (includeAttr == null)
                    {
                        if (ignoreAttr == null)
                        {
                            ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.STJ_ForgetJsonIncludeAttribute,
                                location, name));
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        protected override void AddAttributePreventSerialize(ScriptBuilder sb, bool isOnProperty)
        {
            sb.WriteLine($"[{JSON_IGNORE_ATTRIBUTE}]");
        }
    }
}