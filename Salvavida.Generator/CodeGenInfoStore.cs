using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Salvavida.Generator
{
    /// <summary>
    /// Lazy loading mode for collection properties (mirrors Salvavida.LazyLoadMode).
    /// </summary>
    public enum LazyLoadMode
    {
        None = 0,
        LoadAll = 1,
        LoadIndividual = 2
    }

    public struct LazyLoadConfig
    {
        public LazyLoadMode Mode;
        public bool UseAbstractType;
    }

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
        public readonly Dictionary<string, LazyLoadConfig> lazyLoadConfigs = new();
        /// <summary>
        /// 0 = no generate, 1 = generate by inheritance 2 = generate by implementing
        /// </summary>
        public int generateSerializeRootMode;
    }
}
