Here's the implementation for GetCurrentTreeVersionAsync() with multiple versioning strategies:
Tree Version Management Implementation
1. Basic Implementation with Global Version Counter
csharppublic class TreeVersionService
{
    private readonly IMongoCollection<TreeVersionDocument> _versionCollection;
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _versionLock = new(1, 1);

    public async Task<string> GetCurrentTreeVersionAsync()
    {
        const string cacheKey = "current_tree_version";
        
        // Try cache first for performance
        if (_cache.TryGetValue(cacheKey, out string cachedVersion))
        {
            return cachedVersion;
        }

        var versionDoc = await _versionCollection
            .Find(Builders<TreeVersionDocument>.Filter.Eq(x => x.Id, "global"))
            .FirstOrDefaultAsync();

        var version = versionDoc?.Version ?? "1.0.0";
        
        // Cache for 30 seconds
        _cache.Set(cacheKey, version, TimeSpan.FromSeconds(30));
        
        return version;
    }

    public async Task<string> IncrementTreeVersionAsync()
    {
        await _versionLock.WaitAsync();
        try
        {
            var filter = Builders<TreeVersionDocument>.Filter.Eq(x => x.Id, "global");
            var update = Builders<TreeVersionDocument>.Update
                .Inc(x => x.VersionNumber, 1)
                .Set(x => x.LastUpdated, DateTime.UtcNow)
                .Set(x => x.Version, $"{DateTime.UtcNow:yyyyMMddHHmmss}");

            var options = new FindOneAndUpdateOptions<TreeVersionDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var result = await _versionCollection.FindOneAndUpdateAsync(filter, update, options);
            
            // Clear cache
            _cache.Remove("current_tree_version");
            
            return result.Version;
        }
        finally
        {
            _versionLock.Release();
        }
    }
}

public class TreeVersionDocument
{
    public string Id { get; set; } = "global";
    public string Version { get; set; }
    public long VersionNumber { get; set; }
    public DateTime LastUpdated { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
2. Hash-Based Version (More Sophisticated)
csharppublic class HashBasedTreeVersionService
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly IMongoCollection<TreeHashDocument> _hashCollection;
    private readonly IMemoryCache _cache;

    public async Task<string> GetCurrentTreeVersionAsync()
    {
        const string cacheKey = "tree_hash_version";
        
        if (_cache.TryGetValue(cacheKey, out string cachedHash))
        {
            return cachedHash;
        }

        // Calculate hash based on all tree nodes' last modified dates and structure
        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                ["_id"] = 1,
                ["ParentId"] = 1,
                ["LastModified"] = 1,
                ["Name"] = 1
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1)),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["concatenated"] = new BsonDocument("$push", new BsonDocument("$concat", new BsonArray
                {
                    new BsonDocument("$toString", "$_id"),
                    "|",
                    new BsonDocument("$toString", "$ParentId"),
                    "|", 
                    new BsonDocument("$toString", "$LastModified"),
                    "|",
                    "$Name"
                }))
            }),
            new BsonDocument("$project", new BsonDocument
            {
                ["hashInput"] = new BsonDocument("$reduce", new BsonDocument
                {
                    ["input"] = "$concatenated",
                    ["initialValue"] = "",
                    ["in"] = new BsonDocument("$concat", new BsonArray { "$$value", "$$this" })
                })
            })
        };

        var result = await _treeCollection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        var hashInput = result?["hashInput"]?.AsString ?? "";
        
        var hash = ComputeSHA256Hash(hashInput);
        
        // Cache for 60 seconds
        _cache.Set(cacheKey, hash, TimeSpan.FromSeconds(60));
        
        return hash;
    }

    public async Task<string> RecalculateAndStoreTreeVersionAsync()
    {
        var newVersion = await GetCurrentTreeVersionAsync();
        
        await _hashCollection.ReplaceOneAsync(
            Builders<TreeHashDocument>.Filter.Eq(x => x.Id, "current"),
            new TreeHashDocument
            {
                Id = "current",
                Hash = newVersion,
                CalculatedAt = DateTime.UtcNow,
                NodeCount = await _treeCollection.CountDocumentsAsync(FilterDefinition<TreeNode>.Empty)
            },
            new ReplaceOptions { IsUpsert = true });

        return newVersion;
    }

    private string ComputeSHA256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public class TreeHashDocument
{
    public string Id { get; set; }
    public string Hash { get; set; }
    public DateTime CalculatedAt { get; set; }
    public long NodeCount { get; set; }
}
3. Timestamp-Based Version (Simple and Effective)
csharppublic class TimestampTreeVersionService
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly IMemoryCache _cache;

    public async Task<string> GetCurrentTreeVersionAsync()
    {
        const string cacheKey = "tree_timestamp_version";
        
        if (_cache.TryGetValue(cacheKey, out string cachedVersion))
        {
            return cachedVersion;
        }

        // Get the latest modification timestamp across all nodes
        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["maxLastModified"] = new BsonDocument("$max", "$LastModified"),
                ["nodeCount"] = new BsonDocument("$sum", 1)
            }),
            new BsonDocument("$project", new BsonDocument
            {
                ["version"] = new BsonDocument("$concat", new BsonArray
                {
                    new BsonDocument("$toString", "$maxLastModified"),
                    "_",
                    new BsonDocument("$toString", "$nodeCount")
                })
            })
        };

        var result = await _treeCollection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        var version = result?["version"]?.AsString ?? DateTime.UtcNow.ToString("O");
        
        // Cache for 10 seconds since this is timestamp-based and changes frequently
        _cache.Set(cacheKey, version, TimeSpan.FromSeconds(10));
        
        return version;
    }

    public async Task<string> GetTreeVersionForNodeAsync(ObjectId nodeId)
    {
        // Get version for a specific subtree rooted at nodeId
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("_id", nodeId),
                new BsonDocument("Path", new BsonDocument("$regex", $".*{nodeId}.*"))
            })),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["maxLastModified"] = new BsonDocument("$max", "$LastModified"),
                ["nodeCount"] = new BsonDocument("$sum", 1)
            }),
            new BsonDocument("$project", new BsonDocument
            {
                ["version"] = new BsonDocument("$concat", new BsonArray
                {
                    new BsonDocument("$toString", "$maxLastModified"),
                    "_",
                    new BsonDocument("$toString", "$nodeCount"),
                    "_",
                    new BsonDocument("$toString", nodeId)
                })
            })
        };

        var result = await _treeCollection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        return result?["version"]?.AsString ?? $"{DateTime.UtcNow:O}_{nodeId}";
    }
}
4. Integrated Version Service with Multiple Strategies
csharppublic class TreeVersionService
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly IMongoCollection<TreeVersionDocument> _versionCollection;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    public async Task<string> GetCurrentTreeVersionAsync()
    {
        var strategy = _configuration.GetValue<string>("TreeVersioning:Strategy", "timestamp");
        
        return strategy.ToLowerInvariant() switch
        {
            "hash" => await GetHashBasedVersionAsync(),
            "counter" => await GetCounterBasedVersionAsync(),
            "timestamp" => await GetTimestampBasedVersionAsync(),
            _ => await GetTimestampBasedVersionAsync()
        };
    }

    private async Task<string> GetTimestampBasedVersionAsync()
    {
        const string cacheKey = "tree_version_timestamp";
        
        if (_cache.TryGetValue(cacheKey, out string cached))
            return cached;

        var maxModified = await _treeCollection
            .Find(FilterDefinition<TreeNode>.Empty)
            .SortByDescending(x => x.LastModified)
            .Project(x => x.LastModified)
            .FirstOrDefaultAsync();

        var version = maxModified != default 
            ? maxModified.ToString("yyyyMMddHHmmssfff")
            : DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

        _cache.Set(cacheKey, version, TimeSpan.FromMinutes(1));
        return version;
    }

    private async Task<string> GetHashBasedVersionAsync()
    {
        const string cacheKey = "tree_version_hash";
        
        if (_cache.TryGetValue(cacheKey, out string cached))
            return cached;

        // Aggregate all node signatures for hashing
        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                ["signature"] = new BsonDocument("$concat", new BsonArray
                {
                    new BsonDocument("$toString", "$_id"),
                    new BsonDocument("$toString", "$ParentId"),
                    new BsonDocument("$toString", "$LastModified")
                })
            }),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["allSignatures"] = new BsonDocument("$push", "$signature")
            }),
            new BsonDocument("$project", new BsonDocument
            {
                ["combinedSignature"] = new BsonDocument("$reduce", new BsonDocument
                {
                    ["input"] = "$allSignatures",
                    ["initialValue"] = "",
                    ["in"] = new BsonDocument("$concat", new BsonArray { "$$value", "$$this" })
                })
            })
        };

        var result = await _treeCollection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        var signature = result?["combinedSignature"]?.AsString ?? "";
        
        var hash = ComputeSHA256Hash(signature)[..16]; // Use first 16 chars
        
        _cache.Set(cacheKey, hash, TimeSpan.FromMinutes(5));
        return hash;
    }

    private async Task<string> GetCounterBasedVersionAsync()
    {
        var versionDoc = await _versionCollection
            .Find(Builders<TreeVersionDocument>.Filter.Eq(x => x.Id, "global"))
            .FirstOrDefaultAsync();

        return versionDoc?.Version ?? await InitializeVersionAsync();
    }

    private async Task<string> InitializeVersionAsync()
    {
        var initialVersion = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        
        await _versionCollection.InsertOneAsync(new TreeVersionDocument
        {
            Id = "global",
            Version = initialVersion,
            VersionNumber = 1,
            LastUpdated = DateTime.UtcNow
        });

        return initialVersion;
    }

    public async Task InvalidateVersionCacheAsync()
    {
        _cache.Remove("tree_version_timestamp");
        _cache.Remove("tree_version_hash");
        _cache.Remove("tree_version_counter");
    }

    private string ComputeSHA256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
5. Updated TreeFlatteningService Integration
csharppublic partial class TreeFlatteningService
{
    private readonly TreeVersionService _versionService;

    public async Task StreamFlattenTreeAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = await _versionService.GetCurrentTreeVersionAsync();
        
        var pipeline = new[]
        {
            // ... existing pipeline stages ...
            
            // Updated project stage with version
            new BsonDocument("$project", new BsonDocument
            {
                ["originalNodeId"] = "$_id",
                ["parentId"] = "$ParentId",
                ["ancestorIds"] = new BsonDocument("$map", new BsonDocument
                {
                    ["input"] = "$ancestors",
                    ["as"] = "ancestor",
                    ["in"] = "$$ancestor._id"
                }),
                ["ancestorNames"] = new BsonDocument("$map", new BsonDocument
                {
                    ["input"] = "$ancestors",
                    ["as"] = "ancestor", 
                    ["in"] = "$$ancestor.Name"
                }),
                ["fullPath"] = "$Path",
                ["level"] = "$Level",
                ["nodeName"] = "$Name",
                ["flattenedData"] = new BsonDocument("$mergeObjects", new BsonArray
                {
                    "$Data",
                    new BsonDocument
                    {
                        ["_computedLevel"] = "$Level",
                        ["_pathDepth"] = new BsonDocument("$size", "$pathArray")
                    }
                }),
                ["cacheTimestamp"] = "$$NOW",
                ["treeVersion"] = currentVersion  // Use the current version
            })
        };

        // ... rest of the streaming logic ...
    }
}
6. Configuration Options
json{
  "TreeVersioning": {
    "Strategy": "timestamp", // "hash", "counter", "timestamp"
    "CacheDurationSeconds": 60,
    "HashAlgorithm": "SHA256"
  }
}
The timestamp-based approach is generally recommended for most use cases as it's simple, efficient, and provides good cache invalidation characteristics. The hash-based approach is more sophisticated but computationally expensive. The counter-based approach is good for audit trails but requires additional coordination.
New File at / · lunarud/MongoDbC-AngularAg-Grid
