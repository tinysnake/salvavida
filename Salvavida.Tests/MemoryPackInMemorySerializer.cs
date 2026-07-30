using MemoryPack;

namespace Salvavida.Tests;

/// <summary>Minimal MemoryPack-backed store for generated persistence integration tests.</summary>
public sealed class MemoryPackInMemorySerializer : Serializer
{
    private readonly Dictionary<string, byte[]> _storage = new();

    protected override void DoSaveObjectImpl<T>(T obj, Type type, SerializeContext ctx) =>
        DoSaveObjectImpl(obj, ctx);

    protected override void DoSaveObjectImpl<T>(T obj, SerializeContext ctx) =>
        _storage[ctx.Path.ToString()] = MemoryPackSerializer.Serialize(obj);

    protected override T DoReadImpl<T>(SerializeContext ctx) =>
        _storage.TryGetValue(ctx.Path.ToString(), out var bytes)
            ? MemoryPackSerializer.Deserialize<T>(bytes)!
            : default!;

    protected override bool DoHas(SerializeContext ctx) => _storage.ContainsKey(ctx.Path.ToString());

    protected override void DoDelete(SerializeContext ctx) => _storage.Remove(ctx.Path.ToString());

    protected override void DoDeleteAll(SerializeContext ctx)
    {
        var prefix = ctx.Path.ToString();
        foreach (var path in _storage.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _storage.Remove(path);
    }

    public override IEnumerable<string> ListCollectionIds(SerializeContext ctx) => Array.Empty<string>();

    public override IEnumerable<string> ListCollectionIdsPrefix(SerializeContext ctx, string prefix) => Array.Empty<string>();

    public override IEnumerable<string> ListCollectionIdsMinMax(SerializeContext ctx, string? minValue, string? maxValue) => Array.Empty<string>();
}
