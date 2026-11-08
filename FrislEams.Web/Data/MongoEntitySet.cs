using System.Reflection;
using MongoDB.Driver;

namespace FrislEams.Web.Data;

public sealed class MongoEntitySet<T> where T : class
{
    private readonly IMongoCollection<T> collection;
    private readonly MongoIdGenerator idGenerator;
    private readonly string sequenceName;
    private readonly List<T> pendingInserts = [];
    private readonly Dictionary<int, T> pendingUpdates = [];
    private readonly HashSet<int> pendingDeletes = [];

    public MongoEntitySet(IMongoCollection<T> collection, MongoIdGenerator idGenerator, string sequenceName)
    {
        this.collection = collection;
        this.idGenerator = idGenerator;
        this.sequenceName = sequenceName;
    }

    public IQueryable<T> AsQueryable() => LoadMerged().AsQueryable();

    public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        => AsQueryable().ToListAsync(cancellationToken);

    public IMongoCollection<T> Collection => collection;

    public void Add(T entity) => pendingInserts.Add(entity);

    public void AddRange(IEnumerable<T> entities) => pendingInserts.AddRange(entities);

    public void Update(T entity)
    {
        var id = GetId(entity);
        if (id > 0)
        {
            pendingUpdates[id] = entity;
        }
    }

    public void Remove(T entity)
    {
        var id = GetId(entity);
        if (id > 0)
        {
            pendingDeletes.Add(id);
            pendingUpdates.Remove(id);
        }

        pendingInserts.Remove(entity);
    }

    public async Task<T?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        if (pendingDeletes.Contains(id))
        {
            return null;
        }

        if (pendingUpdates.TryGetValue(id, out var updated))
        {
            return updated;
        }

        var pending = pendingInserts.FirstOrDefault(e => GetId(e) == id);
        if (pending is not null)
        {
            return pending;
        }

        var loaded = await collection.Find(BuildIdFilter(id)).FirstOrDefaultAsync(cancellationToken);
        if (loaded is not null)
        {
            // Track the loaded entity so changes made to it are written by SaveChangesAsync,
            // the same way EF Core does. Without this, "find, change, save" silently did nothing.
            pendingUpdates[id] = loaded;
        }

        return loaded;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entity in pendingInserts)
        {
            if (GetId(entity) == 0)
            {
                SetId(entity, await idGenerator.NextAsync(sequenceName, cancellationToken));
            }

            await collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
        }

        foreach (var (id, entity) in pendingUpdates)
        {
            await collection.ReplaceOneAsync(BuildIdFilter(id), entity, cancellationToken: cancellationToken);
        }

        foreach (var id in pendingDeletes)
        {
            await collection.DeleteOneAsync(BuildIdFilter(id), cancellationToken);
        }

        pendingInserts.Clear();
        pendingUpdates.Clear();
        pendingDeletes.Clear();
    }

    public bool Any() => LoadMerged().Count > 0;

    private List<T> LoadMerged()
    {
        var persisted = collection.Find(FilterDefinition<T>.Empty).ToList();
        var byId = persisted
            .Where(e => !pendingDeletes.Contains(GetId(e)))
            .ToDictionary(GetId);

        foreach (var entity in pendingInserts)
        {
            var id = GetId(entity);
            if (id == 0)
            {
                persisted.Add(entity);
            }
            else
            {
                byId[id] = entity;
            }
        }

        foreach (var (id, entity) in pendingUpdates)
        {
            byId[id] = entity;
        }

        return byId.Values.ToList();
    }

    private FilterDefinition<T> BuildIdFilter(int id)
        => Builders<T>.Filter.Eq("Id", id);

    private static int GetId(T entity)
        => (int)(typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) ?? 0);

    private static void SetId(T entity, int id)
        => typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.SetValue(entity, id);
}
