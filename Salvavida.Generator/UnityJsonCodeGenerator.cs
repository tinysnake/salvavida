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

        protected override void HandleField(ScriptBuilder sb, IFieldSymbol field, CodeGenerationContext ctx)
        {
            AttributeData? nonSerializedAttr = null;
            AttributeData? serializeFieldAttr = null;
            AttributeData? ignoreAttr = null;
            AttributeData? saveSeparatelyAttr = null;
            foreach (var attr in field.GetAttributes())
            {
                var attrName = attr.AttributeClass!.ToDisplayString();
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

            if (nonSerializedAttr != null && saveSeparatelyAttr == null)
                return;

            var location = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
            if (field.DeclaredAccessibility == Accessibility.Public)
            {
                if (saveSeparatelyAttr != null)
                {
                    if (nonSerializedAttr == null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_NonSerializedAttributeRequired,
                            location, field.Name));
                        return;
                    }
                }
                else
                {
                    ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_PrivateKeywoardRecommended,
                        location, field.Name));
                }
            }
            else
            {
                if (saveSeparatelyAttr != null)
                {
                    if (serializeFieldAttr != null)
                    {
                        ctx.SourceProductionContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UJ_HaveToRemoveSerializeFieldAttribute,
                            location, field.Name));
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
                                location, field.Name));
                        }

                        return;
                    }
                }
            }

            base.HandleField(sb, field, ctx);
        }

        protected override void HandleProperty(ScriptBuilder sb, IPropertySymbol prop, CodeGenerationContext ctx)
        {
            //UnityJsonUtility不支持属性
        }

        protected override void AddAttributePreventSerialize(ScriptBuilder sb, bool isOnProperty)
        {
        }
    }
}