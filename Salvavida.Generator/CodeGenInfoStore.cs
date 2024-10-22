using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Salvavida.Generator
{
    public class CodeGenInfoStore
    {
        public string className;
        public readonly HashSet<string> existingPropertyNames = new();
        public readonly Dictionary<string, bool> separatedCollections = new();
        public readonly HashSet<string> separatedProperties = new();
        public readonly List<(string, string)> nameMappings = new();
        public readonly Dictionary<string, ITypeSymbol> propTypeMappings = new();
        public readonly Dictionary<string, (CollectionType, ISymbol[])> collectionParameterMappings = new();
        public readonly Dictionary<ITypeSymbol, bool> savableTypes = new();
        public bool isOrderedClass;
    }
}
