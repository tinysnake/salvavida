using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Salvavida.Generator
{
    public static class CodeGenHelper
    {
        public const string PROPERTY_NAME_ATTRIBUTE_NAME = "Salvavida.PropertyNameAttribute";
        public const string IGNORE_ATTRIBUTE_NAME = "Salvavida.IgnoreAttribute";
        public const string SAVE_SEPARATELY_ATTRIBUTE_NAME = "Salvavida.SaveSeparatelyAttribute";
        public const string SAVABLE_ATTRIBUTE_NAME = "Salvavida.SavableAttribute";
        public const string CLASS_NAME_SYSTEM_OBJECT = "object";
        public const string INTERFACE_NAME_ISAVABLE = "Salvavida.ISavable";
        public const string INTERFACE_NAME_ISERIALIZEROOT = "Salvavida.ISerializeRoot";

        public static bool IsOrderedClass(CodeGenerationContext ctx)
        {
            var salvavidaAttr = ctx.TypeSymbol.GetAttributes().Where(ad => ad.AttributeClass?.ToDisplayString() == SalvavidaGenerator.SALVAVIDA_ATTRIBUTE).First();
            foreach (var nameArg in salvavidaAttr.NamedArguments)
            {
                if (nameArg.Key == "SerializeWithOrder")
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsValidFirstChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
        }

        public static string? GetPropertyName(ReadOnlySpan<char> fieldName)
        {
            if (fieldName.IsEmpty || fieldName.Length == 0)
                return null;
            if (fieldName[0] == '@')
                fieldName = fieldName[1..];
            var c = fieldName[0];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_'))
                return null;
            if (c == '_')
                fieldName = fieldName[1..];
            else if (fieldName.StartsWith("m_".AsSpan(), StringComparison.Ordinal))
                fieldName = fieldName[2..];
            Span<char> span = stackalloc char[fieldName.Length];
            var letter = fieldName[0];
            letter = char.ToUpperInvariant(letter);
            span[0] = letter;
            fieldName[1..].CopyTo(span[1..]);
            return span.ToString();
        }

        public static AttributeSyntax? TryGetAttribute(CodeGenerationContext ctx, SyntaxList<AttributeListSyntax> attrListSyntaxList, string attrFullName)
        {
            foreach (var attrList in attrListSyntaxList)
            {
                foreach (var attr in attrList.Attributes)
                {
                    if (ctx.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                        continue;
                    var attrName = attrSymbol.ContainingType.ToDisplayString();
                    if (attrName == attrFullName)
                        return attr;
                }
            }

            return null;
        }

        public static AttributeSyntax? GetPropertyNameAttribute(CodeGenerationContext ctx, MemberDeclarationSyntax member)
        {
            var attrsList = member.AttributeLists;
            foreach (var attrs in attrsList)
            {
                foreach (var attrNode in attrs.Attributes)
                {
                    if (ctx.SemanticModel.GetSymbolInfo(attrNode).Symbol is not IMethodSymbol attrSymbol)
                        continue;
                    var attrName = attrSymbol.ContainingType.ToDisplayString();
                    if (attrName == PROPERTY_NAME_ATTRIBUTE_NAME)
                        return attrNode;
                }
            }

            return null;
        }

        public static CollectionType GetCollectionType(CodeGenerationContext ctx, ITypeSymbol typeSymbol, out ImmutableArray<ITypeSymbol> elemTypeSymbols)
        {
            if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
            {
                elemTypeSymbols = new[] { arrayTypeSymbol.ElementType }.ToImmutableArray();
                return CollectionType.Array;
            }
            else if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
            {
                var nameSpace = namedTypeSymbol.ContainingNamespace.ToDisplayString();
                var typeName = namedTypeSymbol.Name;
                if (nameSpace == "System.Collections.Generic")
                {
                    elemTypeSymbols = namedTypeSymbol.TypeArguments;

                    if (typeName == "List")
                    {
                        return CollectionType.List;
                    }

                    if (typeName == "Dictionary")
                    {
                        return CollectionType.Dictionary;
                    }
                }
            }

            elemTypeSymbols = ImmutableArray<ITypeSymbol>.Empty;
            return CollectionType.None;
        }

        public static bool GetIsSavable(CodeGenerationContext ctx, TypeSyntax typeSyntax)
        {
            var typeSymbol = ctx.SemanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol;
            if (typeSymbol == null)
                return false;
            return GetIsSavable(typeSymbol);
        }

        public static bool GetIsSavable(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.SpecialType != SpecialType.None)
                return false;
            foreach (var inf in typeSymbol.AllInterfaces)
            {
                if (inf.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == INTERFACE_NAME_ISAVABLE)
                    return true;
            }

            var parent = typeSymbol;
            while (parent != null)
            {
                if (parent.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == CLASS_NAME_SYSTEM_OBJECT)
                    break;
                if (GetIsSavableSingle(parent))
                    return true;
                parent = parent.BaseType;
            }

            return false;
        }

        private static bool GetIsSavableSingle(ITypeSymbol typeSymbol)
        {
            var attrDatas = typeSymbol.GetAttributes();
            foreach (var attrData in attrDatas)
            {
                if (attrData.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == SAVABLE_ATTRIBUTE_NAME)
                    return true;
            }

            return false;
        }

        public static bool GetIsSavableRoot(ITypeSymbol typeSymbol, bool codeGenCheckOnly)
        {
            var attrDatas = typeSymbol.GetAttributes();
            foreach (var attrData in attrDatas)
            {
                if (attrData.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == SAVABLE_ATTRIBUTE_NAME)
                {
                    foreach (var namedArg in attrData.NamedArguments)
                    {
                        var name = namedArg.Key;
                        var type = namedArg.Value;
                        if (name == "IsRootObject")
                            return type.Value != null && (bool)type.Value;
                    }
                }
            }
            return !codeGenCheckOnly && GetIsAlreadySavableRoot(typeSymbol);
        }

        public static bool GetIsAlreadySavableRoot(ITypeSymbol typeSymbol)
        {
            var parent = typeSymbol?.BaseType;
            return parent?.AllInterfaces.Any(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == INTERFACE_NAME_ISERIALIZEROOT) ?? false;
        }

        public static bool GetHasBaseClasse(ITypeSymbol typeSymbol)
        {
            var parent = typeSymbol?.BaseType;
            return parent != null && parent.SpecialType != SpecialType.System_Object;
        }
    }
}