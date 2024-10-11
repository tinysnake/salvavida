using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Salvavida.Generator
{
    internal class UnityJsonCodeGenerator : BasicCodeGenerator
    {
        public const string SERIALIZABLE_ATTRIBUTE = "System.SerializableAttribute";
        public const string NON_SERIALIZE_ATTRIBUTE = "System.NonSerializedAttribute";
        public const string SERIALIZE_FIELD_ATTRIBUTE = "UnityEngine.SerializeField";

        public override bool CanGenerate(CodeGenerationContext ctx)
        {
            var attr = CodeGenHelper.TryGetAttribute(ctx, ctx.ClassNode.AttributeLists, SERIALIZABLE_ATTRIBUTE);
            return attr != null;
        }

        protected override void HandleField(ScriptBuilder sb, FieldDeclarationSyntax field, CodeGenerationContext ctx)
        {
            AttributeSyntax? nonSerializedAttr = null;
            AttributeSyntax? serializeFieldAttr = null;
            AttributeSyntax? ignoreAttr = null;
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
                        case NON_SERIALIZE_ATTRIBUTE:
                            nonSerializedAttr = attr;
                            break;
                        case SERIALIZE_FIELD_ATTRIBUTE:
                            serializeFieldAttr = attr;
                            break;
                        case CodeGenHelper.IGNORE_ATTRIBUTE_NAME:
                            ignoreAttr = attr;
                            break;
                        case CodeGenHelper.SAVE_SEPARATELY_ATTRIBUTE_NAME:
                            saveSeparatelyAttr = attr;
                            break;
                    }
                }
            }

            if (nonSerializedAttr != null && saveSeparatelyAttr == null)
                return;

            if (field.Modifiers.Any(modifer => modifer.IsKind(SyntaxKind.PublicKeyword)))
            {
                if (saveSeparatelyAttr != null)
                {
                    if (nonSerializedAttr == null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_NonSerializedAttributeRequired,
                            field.Declaration.GetLocation(), field.Declaration.Variables.ToString()));
                        return;
                    }
                }
                else
                {
                    ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_PrivateKeywoardRecommended,
                        field.Declaration.GetLocation(), field.Declaration.Variables.ToString()));
                }
            }
            else
            {
                if (saveSeparatelyAttr != null)
                {
                    if (serializeFieldAttr != null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_HaveToRemoveSerializeFieldAttribute,
                            field.Declaration.GetLocation(), field.Declaration.Variables.ToString()));
                        return;
                    }
                }
                else
                {
                    if (serializeFieldAttr == null)
                    {
                        if (ignoreAttr == null)
                        {
                            ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_ForgetSerializerFieldAttribute,
                                field.Declaration.GetLocation(), field.Declaration.Variables.ToString()));
                        }
                        return;
                    }
                }
            }
            base.HandleField(sb, field, ctx);
        }

        protected override void HandleProperty(ScriptBuilder sb, PropertyDeclarationSyntax prop, CodeGenerationContext ctx)
        {
            //UnityJsonUtility不支持属性
        }

        protected override void AddAttributePreventSerialize(ScriptBuilder sb, bool isOnProperty)
        {
        }
    }
}
