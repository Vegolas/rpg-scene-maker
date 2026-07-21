using System.Text.Json;
using AmbientDirector.Api.Services.Ai;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>
/// Strongly-typed base for a <see cref="IShareDescriptor"/>: does the single cast from the boxed
/// <see cref="object"/> the registry hands around, so every concrete descriptor works purely in terms of its
/// own model. Concrete descriptors override the typed members; media/dependency/light-key enumerations default
/// to empty (most kinds have none of one or another).
/// </summary>
public abstract class ShareDescriptor<T> : IShareDescriptor where T : class
{
    public abstract string Kind { get; }

    // ---- store seam (each concrete descriptor wires its own store) ----
    protected abstract Task<T?> GetAsync(string id);
    protected abstract Task UpsertAsync(T entity);
    protected abstract void Validate(T entity);

    // ---- identity ----
    protected abstract string GetId(T entity);
    protected abstract void SetId(T entity, string id);
    protected abstract string GetName(T entity);

    // ---- graph (default empty) ----
    protected virtual IEnumerable<MediaRef> Media(T entity) => [];
    protected virtual IEnumerable<DepRef> Dependencies(T entity) => [];
    protected virtual IEnumerable<LightKeySite> LightKeys(T entity) => [];

    /// <summary>Rewrite ids/media/light-keys in place. Default no-op (kinds with nothing to rewrite).</summary>
    protected virtual void Rewrite(T entity, ShareRewriteContext ctx) { }

    /// <summary>Repair a fixable-but-invalid field so the entity imports; return a locale key for the report,
    /// or null. Default no-op — only kinds with such a field (e.g. a scene's music id) override this.</summary>
    protected virtual string? Sanitize(T entity) => null;

    // ---- boxing shims: one cast, then delegate to the typed members above ----
    async Task<object?> IShareDescriptor.LoadAsync(string id) => await GetAsync(id);
    async Task<bool> IShareDescriptor.ExistsAsync(string id) => await GetAsync(id) is not null;
    string IShareDescriptor.IdOf(object entity) => GetId((T)entity);
    string IShareDescriptor.NameOf(object entity) => GetName((T)entity);
    void IShareDescriptor.SetId(object entity, string id) => SetId((T)entity, id);
    IEnumerable<MediaRef> IShareDescriptor.Media(object entity) => Media((T)entity);
    IEnumerable<DepRef> IShareDescriptor.Dependencies(object entity) => Dependencies((T)entity);
    IEnumerable<LightKeySite> IShareDescriptor.LightKeys(object entity) => LightKeys((T)entity);
    JsonElement IShareDescriptor.ToJson(object entity) => JsonSerializer.SerializeToElement((T)entity, AiJson.Options);
    object IShareDescriptor.FromJson(JsonElement json) => json.Deserialize<T>(AiJson.Options)!;
    void IShareDescriptor.Rewrite(object entity, ShareRewriteContext ctx) => Rewrite((T)entity, ctx);
    string? IShareDescriptor.Sanitize(object entity) => Sanitize((T)entity);
    void IShareDescriptor.Validate(object entity) => Validate((T)entity);
    Task IShareDescriptor.UpsertAsync(object entity) => UpsertAsync((T)entity);
}
