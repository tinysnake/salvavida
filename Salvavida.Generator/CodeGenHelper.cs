using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Salvavida.Generator
{
    public static class CodeGenHelper
    {
        public const string PROPERTY_NAME_ATTRIBUTE_NAME = "Salvavida.PropertyNameAttribute";
        public const string IGNORE_ATTRIBUTE_NAME = "Salvavida.IgnoreAttribute";
        public const string SAVE_SEPARATELY_ATTRIBUTE_NAME = "Salvavida.SaveSeparatelyAttribute";
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

        public static CollectionType GetCollectionType(CodeGenerationContext ctx, TypeSyntax typeSyntax, ref ITypeSymbol typeSymbol, out ISymbol[]? elemTypeSymbols)
        {
            typeSymbol ??= (ctx.SemanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol) ?? throw new System.InvalidCastException("targetSyntax is not a typeSyntax");
            if (typeSyntax is ArrayTypeSyntax ats)
            {
                elemTypeSymbols = new[] { ctx.SemanticModel.GetSymbolInfo(ats.ElementType).Symbol }!;
                return CollectionType.Array;
            }
            else if (typeSyntax is GenericNameSyntax gns)
            {
                var nameSpace = typeSymbol.ContainingSymbol.ToDisplayString();
                var typeName = typeSymbol.Name;
                if (nameSpace == "System.Collections.Generic")
                {
                    if (typeName == "List")
                    {
                        elemTypeSymbols = new[] { ctx.SemanticModel.GetSymbolInfo(gns.TypeArgumentList.Arguments[0]).Symbol }!;
                        return CollectionType.List;
                    }
                    if (typeName == "Dictionary")
                    {
                        elemTypeSymbols = gns.TypeArgumentList.Arguments.Select(arg => ctx.SemanticModel.GetSymbolInfo(arg).Symbol).ToArray()!;
                        return CollectionType.Dictionary;
                    }
                }
            }
            elemTypeSymbols = null;
            return CollectionType.None;
        }
    }
}
