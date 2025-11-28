using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Salvavida.Generator
{
    public class CodeGenInfoStore
    {
        public string? className;
        public readonly HashSet<string> existingPropertyNames = new();
        public readonly HashSet<string> nonSeparatedCollections = new();
        public readonly HashSet<string> separatedCollections = new();
        public readonly HashSet<string> separatedProperties = new();
        public readonly HashSet<string> savableMembers = new();
        //public readonly HashSet<string> internalSeparatedMembers = new();
        public readonly Dictionary<string, string> nameMappings = new();
        public readonly Dictionary<string, ITypeSymbol> propTypeMappings = new();
        public readonly Dictionary<string, (CollectionType, ImmutableArray<ITypeSymbol>)> collectionParameterMappings = new();
        public readonly Dictionary<ITypeSymbol, bool> savableTypes = new(SymbolEqualityComparer.Default);
        /// <summary>
        /// 0 = no generate, 1 = generate by inheritance 2 = generate by implementing
        /// </summary>
        public int generateSerializeRootMode;
    }
}
