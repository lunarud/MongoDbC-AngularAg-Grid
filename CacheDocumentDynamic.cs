using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq.Expressions;
using System.Dynamic;

// Base cache document for dynamic polymorphism
[BsonDiscriminator(RootClass = true)]
public class CacheDocument : DynamicObject
{
    private readonly Dictionary<string, object> _dynamicData = new Dictionary<string, object>();
    
    [BsonId]
    public ObjectId Id { get; set; }
    
    [BsonElement("_type")]
    public string DocumentType { get; set; }
    
    [BsonElement("cacheKey")]
    public string CacheKey { get; set; }
    
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
    
    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }
    
    [BsonElement("version")]
    public int Version { get; set; }
    
    [BsonElement("data")]
    public BsonDocument Data { get; set; }
    
    [BsonExtraElements]
    public Dictionary<string, object> ExtraElements { get; set; } = new Dictionary<string, object>();

    public CacheDocument()
    {
        Id = ObjectId.GenerateNewId();
        CreatedAt = DateTime.UtcNow;
        Version = 1;
        Data = new BsonDocument();
    }

    public CacheDocument(string documentType, Dictionary<string, object> data = null) : this()
    {
        DocumentType = documentType;
        if (data != null)
        {
            SetData(data);
        }
    }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    // Dynamic property access
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        if (_dynamicData.TryGetValue(binder.Name, out result))
            return true;
            
        if (Data?.Contains(binder.Name) == true)
        {
            result = BsonTypeMapper.MapToDotNetValue(Data[binder.Name]);
            return true;
        }
        
        if (ExtraElements.TryGetValue(binder.Name, out result))
            return true;
            
        result = null;
        return false;
    }

    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        _dynamicData[binder.Name] = value;
        
        // Also store in Data BsonDocument for MongoDB persistence
        if (value != null)
        {
            Data[binder.Name] = BsonValue.Create(value);
        }
        else
        {
            Data.Remove(binder.Name);
        }
        
        return true;
    }

    // Get all dynamic member names
    public override IEnumerable<string> GetDynamicMemberNames()
    {
        var names = new HashSet<string>();
        names.UnionWith(_dynamicData.Keys);
        names.UnionWith(Data?.Names ?? Enumerable.Empty<string>());
        names.UnionWith(ExtraElements.Keys);
        return names;
    }

    // Bulk data operations
    public void SetData(Dictionary<string, object> data)
    {
        if (data == null) return;
        
        foreach (var kvp in data)
        {
            SetValue(kvp.Key, kvp.Value);
        }
    }

    public void SetData(BsonDocument bsonData)
    {
        if (bsonData == null) return;
        
        foreach (var element in bsonData)
        {
            SetValue(element.Name, BsonTypeMapper.MapToDotNetValue(element.Value));
        }
    }

    public void SetValue(string key, object value)
    {
        _dynamicData[key] = value;
        
        if (value != null)
        {
            Data[key] = BsonValue.Create(value);
        }
        else
        {
            Data.Remove(key);
        }
    }

    public T GetValue<T>(string key, T defaultValue = default(T))
    {
        if (_dynamicData.TryGetValue(key, out var result))
        {
            return ConvertValue<T>(result);
        }
        
        if (Data?.Contains(key) == true)
        {
            var bsonValue = Data[key];
            var dotNetValue = BsonTypeMapper.MapToDotNetValue(bsonValue);
            return ConvertValue<T>(dotNetValue);
        }
        
        return defaultValue;
    }

    public bool HasValue(string key)
    {
        return _dynamicData.ContainsKey(key) || 
               Data?.Contains(key) == true ||
               ExtraElements.ContainsKey(key);
    }

    public Dictionary<string, object> GetAllData()
    {
        var result = new Dictionary<string, object>(_dynamicData);
        
        if (Data != null)
        {
            foreach (var element in Data)
            {
                if (!result.ContainsKey(element.Name))
                {
                    result[element.Name] = BsonTypeMapper.MapToDotNetValue(element.Value);
                }
            }
        }
        
        foreach (var kvp in ExtraElements)
        {
            if (!result.ContainsKey(kvp.Key))
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result;
    }

    private T ConvertValue<T>(object value)
    {
        if (value == null) return default(T);
        if (value is T directCast) return directCast;
        
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default(T);
        }
    }
}

// Document type registry for validation and schema management
public class CacheDocumentSchema
{
    public string DocumentType { get; set; }
    public Dictionary<string, Type> RequiredFields { get; set; } = new Dictionary<string, Type>();
    public Dictionary<string, Type> OptionalFields { get; set; } = new Dictionary<string, Type>();
    public HashSet<string> IndexedFields { get; set; } = new HashSet<string>();
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(1);
}

public class CacheDocumentRegistry
{
    private readonly Dictionary<string, CacheDocumentSchema> _schemas = new Dictionary<string, CacheDocumentSchema>();

    public void RegisterSchema(CacheDocumentSchema schema)
    {
        _schemas[schema.DocumentType] = schema;
    }

    public CacheDocumentSchema GetSchema(string documentType)
    {
        return _schemas.TryGetValue(documentType, out var schema) ? schema : null;
    }

    public bool IsValidDocument(CacheDocument document)
    {
        var schema = GetSchema(document.DocumentType);
        if (schema == null) return true; // No schema means no validation
        
        // Check required fields
        foreach (var requiredField in schema.RequiredFields)
        {
            if (!document.HasValue(requiredField.Key))
                return false;
                
            var value = document.GetValue<object>(requiredField.Key);
            if (value != null && !requiredField.Value.IsAssignableFrom(value.GetType()))
                return false;
        }
        
        return true;
    }

    public IEnumerable<string> GetRegisteredTypes()
    {
        return _schemas.Keys;
    }
}

// Factory for creating documents from dynamic data
public class CacheDocumentFactory
{
    private readonly CacheDocumentRegistry _registry;

    public CacheDocumentFactory(CacheDocumentRegistry registry = null)
    {
        _registry = registry ?? new CacheDocumentRegistry();
    }

    public CacheDocument CreateDocument(string documentType, Dictionary<string, object> data, 
        TimeSpan? expiry = null)
    {
        var document = new CacheDocument(documentType, data);
        
        var schema = _registry.GetSchema(documentType);
        var expiryTime = expiry ?? schema?.DefaultExpiry ?? TimeSpan.FromHours(1);
        document.ExpiresAt = DateTime.UtcNow.Add(expiryTime);
        
        if (!_registry.IsValidDocument(document))
        {
            throw new ArgumentException($"Document does not meet schema requirements for type: {documentType}");
        }
        
        return document;
    }

    public CacheDocument CreateDocumentFromBson(string documentType, BsonDocument bsonData, 
        TimeSpan? expiry = null)
    {
        var document = new CacheDocument(documentType);
        document.SetData(bsonData);
        
        var schema = _registry.GetSchema(documentType);
        var expiryTime = expiry ?? schema?.DefaultExpiry ?? TimeSpan.FromHours(1);
        document.ExpiresAt = DateTime.UtcNow.Add(expiryTime);
        
        return document;
    }

    public CacheDocument CreateDocumentFromJson(string documentType, string jsonData, 
        TimeSpan? expiry = null)
    {
        var bsonDoc = BsonDocument.Parse(jsonData);
        return CreateDocumentFromBson(documentType, bsonDoc, expiry);
    }
}

// Repository interface for dynamic documents
public interface ICacheRepository
{
    Task<CacheDocument> CreateAsync(CacheDocument document);
    Task<CacheDocument> GetByIdAsync(ObjectId id);
    Task<CacheDocument> GetByCacheKeyAsync(string cacheKey);
    Task<List<CacheDocument>> GetByTypeAsync(string documentType);
    Task<List<CacheDocument>> GetAllAsync();
    Task<List<CacheDocument>> FindAsync(FilterDefinition<CacheDocument> filter);
    Task<List<CacheDocument>> FindByFieldAsync(string fieldName, object value, string documentType = null);
    Task<CacheDocument> UpdateAsync(CacheDocument document);
    Task<bool> DeleteAsync(ObjectId id);
    Task<bool> DeleteByCacheKeyAsync(string cacheKey);
    Task<long> DeleteExpiredAsync();
    Task<long> DeleteByTypeAsync(string documentType);
    Task<bool> ExistsAsync(string cacheKey);
    Task CreateIndexAsync(string fieldName, bool ascending = true);
}

// Dynamic repository implementation
public class DynamicCacheRepository : ICacheRepository
{
    private readonly IMongoCollection<CacheDocument> _collection;
    private readonly CacheDocumentRegistry _registry;

    public DynamicCacheRepository(IMongoDatabase database, string collectionName = "cache_documents",
        CacheDocumentRegistry registry = null)
    {
        _collection = database.GetCollection<CacheDocument>(collectionName);
        _registry = registry ?? new CacheDocumentRegistry();
        
        CreateBasicIndexes();
    }

    private void CreateBasicIndexes()
    {
        // Basic indexes for common queries
        var cacheKeyIndex = Builders<CacheDocument>.IndexKeys.Ascending(x => x.CacheKey);
        var typeIndex = Builders<CacheDocument>.IndexKeys.Ascending(x => x.DocumentType);
        var expiryIndex = Builders<CacheDocument>.IndexKeys.Ascending(x => x.ExpiresAt);
        
        var cacheKeyIndexModel = new CreateIndexModel<CacheDocument>(cacheKeyIndex,
            new CreateIndexOptions { Unique = true, Name = "CacheKey_Index" });
        var typeIndexModel = new CreateIndexModel<CacheDocument>(typeIndex,
            new CreateIndexOptions { Name = "DocumentType_Index" });
        var expiryIndexModel = new CreateIndexModel<CacheDocument>(expiryIndex,
            new CreateIndexOptions { Name = "ExpiresAt_Index" });

        _collection.Indexes.CreateMany(new[] { cacheKeyIndexModel, typeIndexModel, expiryIndexModel });
    }

    public async Task<CacheDocument> CreateAsync(CacheDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        
        if (!_registry.IsValidDocument(document))
        {
            throw new ArgumentException($"Document validation failed for type: {document.DocumentType}");
        }
        
        document.CreatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(document);
        return document;
    }

    public async Task<CacheDocument> GetByIdAsync(ObjectId id)
    {
        var filter = Builders<CacheDocument>.Filter.Eq(x => x.Id, id);
        var result = await _collection.Find(filter).FirstOrDefaultAsync();
        
        if (result?.IsExpired == true)
        {
            await DeleteAsync(id);
            return null;
        }
        
        return result;
    }

    public async Task<CacheDocument> GetByCacheKeyAsync(string cacheKey)
    {
        var filter = Builders<CacheDocument>.Filter.Eq(x => x.CacheKey, cacheKey);
        var result = await _collection.Find(filter).FirstOrDefaultAsync();
        
        if (result?.IsExpired == true)
        {
            await DeleteByCacheKeyAsync(cacheKey);
            return null;
        }
        
        return result;
    }

    public async Task<List<CacheDocument>> GetByTypeAsync(string documentType)
    {
        var filter = Builders<CacheDocument>.Filter.And(
            Builders<CacheDocument>.Filter.Eq(x => x.DocumentType, documentType),
            Builders<CacheDocument>.Filter.Gt(x => x.ExpiresAt, DateTime.UtcNow)
        );
        
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task<List<CacheDocument>> GetAllAsync()
    {
        var filter = Builders<CacheDocument>.Filter.Gt(x => x.ExpiresAt, DateTime.UtcNow);
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task<List<CacheDocument>> FindAsync(FilterDefinition<CacheDocument> filter)
    {
        var notExpiredFilter = Builders<CacheDocument>.Filter.Gt(x => x.ExpiresAt, DateTime.UtcNow);
        var combinedFilter = Builders<CacheDocument>.Filter.And(notExpiredFilter, filter);
        
        return await _collection.Find(combinedFilter).ToListAsync();
    }

    public async Task<List<CacheDocument>> FindByFieldAsync(string fieldName, object value, string documentType = null)
    {
        var filterBuilder = Builders<CacheDocument>.Filter;
        var filters = new List<FilterDefinition<CacheDocument>>
        {
            filterBuilder.Gt(x => x.ExpiresAt, DateTime.UtcNow),
            filterBuilder.Eq($"data.{fieldName}", value)
        };

        if (!string.IsNullOrEmpty(documentType))
        {
            filters.Add(filterBuilder.Eq(x => x.DocumentType, documentType));
        }

        var combinedFilter = filterBuilder.And(filters);
        return await _collection.Find(combinedFilter).ToListAsync();
    }

    public async Task<CacheDocument> UpdateAsync(CacheDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        
        if (!_registry.IsValidDocument(document))
        {
            throw new ArgumentException($"Document validation failed for type: {document.DocumentType}");
        }
        
        document.Version++;
        
        var filter = Builders<CacheDocument>.Filter.Eq(x => x.Id, document.Id);
        var result = await _collection.ReplaceOneAsync(filter, document);
        
        return result.ModifiedCount > 0 ? document : null;
    }

    public async Task<bool> DeleteAsync(ObjectId id)
    {
        var filter = Builders<CacheDocument>.Filter.Eq(x => x.Id, id);
        var result = await _collection.DeleteOneAsync(filter);
        return result.DeletedCount > 0;
    }

    public async Task<bool> DeleteByCacheKeyAsync(string cacheKey)
    {
        var filter = Builders<CacheDocument>.Filter.Eq(x => x.CacheKey, cacheKey);
        var result = await _collection.DeleteOneAsync(filter);
        return result.DeletedCount > 0;
    }

    public async Task<long> DeleteExpiredAsync()
    {
        var filter = Builders<CacheDocument>.Filter.Lt(x => x.ExpiresAt, DateTime.UtcNow);
        var result = await _collection.DeleteManyAsync(filter);
        return result.DeletedCount;
    }

    public async Task<long> DeleteByTypeAsync(string documentType)
    {
        var filter = Builders<CacheDocument>.Filter.Eq(x => x.DocumentType, documentType);
        var result = await _collection.DeleteManyAsync(filter);
        return result.DeletedCount;
    }

    public async Task<bool> ExistsAsync(string cacheKey)
    {
        var filter = Builders<CacheDocument>.Filter.And(
            Builders<CacheDocument>.Filter.Eq(x => x.CacheKey, cacheKey),
            Builders<CacheDocument>.Filter.Gt(x => x.ExpiresAt, DateTime.UtcNow)
        );
        
        return await _collection.Find(filter).AnyAsync();
    }

    public async Task CreateIndexAsync(string fieldName, bool ascending = true)
    {
        var indexKey = ascending 
            ? Builders<CacheDocument>.IndexKeys.Ascending($"data.{fieldName}")
            : Builders<CacheDocument>.IndexKeys.Descending($"data.{fieldName}");
        
        var indexModel = new CreateIndexModel<CacheDocument>(indexKey,
            new CreateIndexOptions { Name = $"Data_{fieldName}_Index" });
        
        await _collection.Indexes.CreateOneAsync(indexModel);
    }
}

// Service layer for easy usage
public class DynamicCacheService
{
    private readonly ICacheRepository _repository;
    private readonly CacheDocumentFactory _factory;

    public DynamicCacheService(ICacheRepository repository, CacheDocumentFactory factory)
    {
        _repository = repository;
        _factory = factory;
    }

    public async Task<CacheDocument> SetAsync(string cacheKey, string documentType, 
        Dictionary<string, object> data, TimeSpan? expiry = null)
    {
        var document = _factory.CreateDocument(documentType, data, expiry);
        document.CacheKey = cacheKey;
        return await _repository.CreateAsync(document);
    }

    public async Task<CacheDocument> SetFromJsonAsync(string cacheKey, string documentType, 
        string jsonData, TimeSpan? expiry = null)
    {
        var document = _factory.CreateDocumentFromJson(documentType, jsonData, expiry);
        document.CacheKey = cacheKey;
        return await _repository.CreateAsync(document);
    }

    public async Task<CacheDocument> GetAsync(string cacheKey)
    {
        return await _repository.GetByCacheKeyAsync(cacheKey);
    }

    public async Task<T> GetValueAsync<T>(string cacheKey, string fieldName, T defaultValue = default(T))
    {
        var document = await GetAsync(cacheKey);
        return document?.GetValue(fieldName, defaultValue) ?? defaultValue;
    }

    public async Task<List<CacheDocument>> FindByTypeAsync(string documentType)
    {
        return await _repository.GetByTypeAsync(documentType);
    }

    public async Task<List<CacheDocument>> FindByFieldAsync(string fieldName, object value, string documentType = null)
    {
        return await _repository.FindByFieldAsync(fieldName, value, documentType);
    }

    public async Task<bool> UpdateFieldAsync(string cacheKey, string fieldName, object value)
    {
        var document = await GetAsync(cacheKey);
        if (document == null) return false;
        
        document.SetValue(fieldName, value);
        var updated = await _repository.UpdateAsync(document);
        return updated != null;
    }

    public async Task<bool> RemoveAsync(string cacheKey)
    {
        return await _repository.DeleteByCacheKeyAsync(cacheKey);
    }

    public async Task<long> CleanupExpiredAsync()
    {
        return await _repository.DeleteExpiredAsync();
    }
}
