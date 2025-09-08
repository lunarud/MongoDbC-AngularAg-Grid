Here's the implementation for UpdateSubtreePathsAsync to handle path/level changes when the parent relationship remains the same:
UpdateSubtreePathsAsync Implementation
csharppublic partial class TreeChangeProcessor
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly TreeFlatteningService _flatteningService;
    private readonly TreeCacheInvalidationService _invalidationService;
    private readonly ILogger<TreeChangeProcessor> _logger;

    private async Task UpdateSubtreePathsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating subtree paths for node {NodeId}", nodeId);

        try
        {
            // Step 1: Get the updated node to understand the new path structure
            var updatedNode = await _treeCollection
                .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, nodeId))
                .FirstOrDefaultAsync(cancellationToken);

            if (updatedNode == null)
            {
                _logger.LogWarning("Node {NodeId} not found for path update", nodeId);
                return;
            }

            // Step 2: Find all descendants that need path updates
            var affectedNodes = await FindAffectedDescendantsAsync(nodeId, cancellationToken);
            
            if (!affectedNodes.Any())
            {
                _logger.LogDebug("No descendants found for node {NodeId}", nodeId);
                // Still need to update the node itself
                await _flatteningService.StreamFlattenNodeAsync(nodeId, cancellationToken);
                return;
            }

            // Step 3: Process in batches to avoid overwhelming the system
            const int batchSize = 100;
            var batches = affectedNodes.Batch(batchSize);

            foreach (var batch in batches)
            {
                await ProcessPathUpdateBatchAsync(batch.ToList(), updatedNode, cancellationToken);
            }

            _logger.LogInformation("Completed path updates for {Count} nodes under {NodeId}", 
                                 affectedNodes.Count, nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subtree paths for node {NodeId}", nodeId);
            throw;
        }
    }

    private async Task<List<TreeNode>> FindAffectedDescendantsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        // Find all nodes that have this node in their ancestry path
        // This includes direct children and all deeper descendants
        var pipeline = new[]
        {
            // Match nodes that are descendants of the changed node
            new BsonDocument("$match", new BsonDocument("$or", new BsonArray
            {
                // Direct children
                new BsonDocument("ParentId", nodeId),
                // Deeper descendants (path contains the node ID)
                new BsonDocument("Path", new BsonDocument("$regex", $".*{nodeId}.*"))
            })),
            
            // Sort by level to process parents before children
            new BsonDocument("$sort", new BsonDocument("Level", 1)),
            
            // Project relevant fields
            new BsonDocument("$project", new BsonDocument
            {
                ["_id"] = 1,
                ["ParentId"] = 1,
                ["Name"] = 1,
                ["Level"] = 1,
                ["Path"] = 1,
                ["Data"] = 1,
                ["LastModified"] = 1
            })
        };

        var options = new AggregateOptions { AllowDiskUse = true };
        return await _treeCollection.Aggregate<TreeNode>(pipeline, options)
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessPathUpdateBatchAsync(List<TreeNode> batch, TreeNode changedAncestor, 
                                                 CancellationToken cancellationToken)
    {
        var tasks = batch.Select(async node =>
        {
            try
            {
                // Check if this node's cached path information is outdated
                var cachedNode = await GetCachedNodeAsync(node.Id);
                
                if (cachedNode != null && IsPathOutdated(node, cachedNode, changedAncestor))
                {
                    // Invalidate and recalculate this specific node
                    await InvalidateAndRecalculateNodeAsync(node.Id, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing path update for node {NodeId}", node.Id);
            }
        });

        await Task.WhenAll(tasks);
    }

    private bool IsPathOutdated(TreeNode sourceNode, FlatTreeNode cachedNode, TreeNode changedAncestor)
    {
        // Check if the cached path information is inconsistent with current source
        
        // 1. Check if the full path matches
        if (!string.Equals(cachedNode.FullPath, sourceNode.Path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Check if the level matches
        if (cachedNode.Level != sourceNode.Level)
        {
            return true;
        }

        // 3. Check if ancestor information is outdated
        // This happens when an ancestor node's name or path changed
        if (cachedNode.AncestorIds?.Contains(changedAncestor.Id.ToString()) == true)
        {
            return true;
        }

        return false;
    }

    private async Task InvalidateAndRecalculateNodeAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        // Remove the outdated cache entry
        await _invalidationService.InvalidateNodeAsync(nodeId);
        
        // Recalculate with current data
        await _flatteningService.StreamFlattenNodeAsync(nodeId, cancellationToken);
    }

    /// <summary>
    /// Alternative approach: Bulk update using aggregation pipeline
    /// More efficient for large subtrees
    /// </summary>
    private async Task BulkUpdateSubtreePathsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing bulk path update for subtree rooted at {NodeId}", nodeId);

        // Step 1: Remove all cached entries for the subtree
        await _invalidationService.InvalidateSubtreeAsync(nodeId);

        // Step 2: Use streaming aggregation to recalculate the entire subtree
        // This is more efficient than node-by-node updates for large subtrees
        await _flatteningService.StreamFlattenSubtreeAsync(nodeId, cancellationToken);
    }

    /// <summary>
    /// Optimized version that determines the best strategy based on subtree size
    /// </summary>
    private async Task OptimizedUpdateSubtreePathsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        // Count descendants to determine strategy
        var descendantCount = await CountDescendantsAsync(nodeId, cancellationToken);
        
        const int bulkUpdateThreshold = 50;

        if (descendantCount > bulkUpdateThreshold)
        {
            _logger.LogInformation("Using bulk update strategy for {Count} descendants", descendantCount);
            await BulkUpdateSubtreePathsAsync(nodeId, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Using incremental update strategy for {Count} descendants", descendantCount);
            await UpdateSubtreePathsAsync(nodeId, cancellationToken);
        }
    }

    private async Task<long> CountDescendantsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        var filter = Builders<TreeNode>.Filter.Or(
            Builders<TreeNode>.Filter.Eq(x => x.ParentId, nodeId),
            Builders<TreeNode>.Filter.Regex(x => x.Path, new BsonRegularExpression($".*{nodeId}.*"))
        );

        return await _treeCollection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }
}
Enhanced TreeCacheInvalidationService
csharppublic class TreeCacheInvalidationService
{
    private readonly IMongoCollection<FlatTreeNode> _cacheCollection;
    private readonly ILogger<TreeCacheInvalidationService> _logger;

    public async Task InvalidateNodeAsync(ObjectId nodeId)
    {
        var filter = Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, nodeId);
        var result = await _cacheCollection.DeleteOneAsync(filter);
        
        _logger.LogDebug("Invalidated cache for node {NodeId}, deleted {Count} entries", 
                        nodeId, result.DeletedCount);
    }

    public async Task InvalidateSubtreeAsync(ObjectId nodeId)
    {
        var filter = Builders<FlatTreeNode>.Filter.Or(
            Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, nodeId),
            Builders<FlatTreeNode>.Filter.AnyEq(x => x.AncestorIds, nodeId.ToString())
        );

        var result = await _cacheCollection.DeleteManyAsync(filter);
        
        _logger.LogInformation("Invalidated cache for subtree {NodeId}, deleted {Count} entries", 
                              nodeId, result.DeletedCount);
    }

    public async Task InvalidateByPathPrefixAsync(string pathPrefix)
    {
        var filter = Builders<FlatTreeNode>.Filter.Regex(x => x.FullPath, 
            new BsonRegularExpression($"^{Regex.Escape(pathPrefix)}"));

        var result = await _cacheCollection.DeleteManyAsync(filter);
        
        _logger.LogInformation("Invalidated cache by path prefix {PathPrefix}, deleted {Count} entries", 
                              pathPrefix, result.DeletedCount);
    }

    public async Task InvalidateStaleEntriesAsync(string currentTreeVersion)
    {
        var filter = Builders<FlatTreeNode>.Filter.Ne(x => x.TreeVersion, currentTreeVersion);
        var result = await _cacheCollection.DeleteManyAsync(filter);
        
        _logger.LogInformation("Invalidated {Count} stale cache entries", result.DeletedCount);
    }
}
Utility Extension for Batching
csharppublic static class EnumerableExtensions
{
    public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        if (batchSize <= 0)
            throw new ArgumentException("Batch size must be positive", nameof(batchSize));

        var batch = new List<T>(batchSize);
        
        foreach (var item in source)
        {
            batch.Add(item);
            
            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<T>(batchSize);
            }
        }
        
        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
Advanced Path Update Pipeline
csharppublic class AdvancedPathUpdateService
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly IMongoCollection<FlatTreeNode> _cacheCollection;

    public async Task UpdateDescendantPathsWithPipelineAsync(ObjectId changedNodeId, 
                                                           CancellationToken cancellationToken)
    {
        // Use MongoDB aggregation to efficiently recalculate paths
        var pipeline = new[]
        {
            // Stage 1: Find the changed node and all its descendants
            new BsonDocument("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("_id", changedNodeId),
                new BsonDocument("Path", new BsonDocument("$regex", $".*{changedNodeId}.*"))
            })),

            // Stage 2: Add fields for path recalculation
            new BsonDocument("$addFields", new BsonDocument
            {
                ["pathSegments"] = new BsonDocument("$split", new BsonArray { "$Path", "/" }),
                ["needsUpdate"] = new BsonDocument("$ne", new BsonArray { "$_id", changedNodeId })
            }),

            // Stage 3: Lookup current cached version
            new BsonDocument("$lookup", new BsonDocument
            {
                ["from"] = "FlatTreeCache",
                ["localField"] = "_id",
                ["foreignField"] = "originalNodeId", 
                ["as"] = "cachedVersion"
            }),

            // Stage 4: Determine if update is needed
            new BsonDocument("$addFields", new BsonDocument
            {
                ["shouldUpdate"] = new BsonDocument("$or", new BsonArray
                {
                    // No cached version exists
                    new BsonDocument("$eq", new BsonArray 
                    { 
                        new BsonDocument("$size", "$cachedVersion"), 0 
                    }),
                    // Cached path doesn't match current path
                    new BsonDocument("$ne", new BsonArray
                    {
                        new BsonDocument("$arrayElemAt", new BsonArray { "$cachedVersion.fullPath", 0 }),
                        "$Path"
                    }),
                    // Cached level doesn't match current level
                    new BsonDocument("$ne", new BsonArray
                    {
                        new BsonDocument("$arrayElemAt", new BsonArray { "$cachedVersion.level", 0 }),
                        "$Level"
                    })
                })
            }),

            // Stage 5: Filter only nodes that need updates
            new BsonDocument("$match", new BsonDocument("shouldUpdate", true)),

            // Stage 6: Project node IDs that need recalculation
            new BsonDocument("$project", new BsonDocument
            {
                ["_id"] = 1,
                ["updatePriority"] = new BsonDocument("$cond", new BsonDocument
                {
                    ["if"] = new BsonDocument("$eq", new BsonArray { "$_id", changedNodeId }),
                    ["then"] = 1,
                    ["else"] = "$Level"
                })
            }),

            // Stage 7: Sort by priority (changed node first, then by level)
            new BsonDocument("$sort", new BsonDocument
            {
                ["updatePriority"] = 1,
                ["_id"] = 1
            })
        };

        var nodesToUpdate = await _treeCollection.Aggregate<BsonDocument>(pipeline)
            .ToListAsync(cancellationToken);

        // Process the identified nodes
        foreach (var nodeDoc in nodesToUpdate)
        {
            var nodeId = nodeDoc["_id"].AsObjectId;
            await _flatteningService.StreamFlattenNodeAsync(nodeId, cancellationToken);
        }
    }
}
Usage in TreeChangeProcessor
csharppublic partial class TreeChangeProcessor
{
    // Choose the appropriate strategy based on your needs
    private async Task UpdateSubtreePathsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        // Option 1: Simple incremental approach (good for small subtrees)
        await SimpleUpdateSubtreePathsAsync(nodeId, cancellationToken);
        
        // Option 2: Optimized approach (automatically chooses strategy)
        // await OptimizedUpdateSubtreePathsAsync(nodeId, cancellationToken);
        
        // Option 3: Advanced pipeline approach (most efficient for complex scenarios)
        // await _advancedPathUpdateService.UpdateDescendantPathsWithPipelineAsync(nodeId, cancellationToken);
    }

    private async Task SimpleUpdateSubtreePathsAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        // This is the main implementation shown above
        var updatedNode = await _treeCollection
            .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, nodeId))
            .FirstOrDefaultAsync(cancellationToken);

        if (updatedNode == null) return;

        var affectedNodes = await FindAffectedDescendantsAsync(nodeId, cancellationToken);
        
        const int batchSize = 100;
        var batches = affectedNodes.Batch(batchSize);

        foreach (var batch in batches)
        {
            await ProcessPathUpdateBatchAsync(batch.ToList(), updatedNode, cancellationToken);
        }
    }
}
This implementation provides multiple strategies for updating subtree paths efficiently, from simple incremental updates to sophisticated pipeline-based approaches, allowing you to choose the best method based on your specific use case and data size.RetryRLdefine GetSubtreeRootEditHere's the implementation for GetSubtreeRoot used in the batched change processor:
GetSubtreeRoot Implementation
csharppublic class BatchedTreeChangeProcessor
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly TreeFlatteningService _flatteningService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BatchedTreeChangeProcessor> _logger;

    /// <summary>
    /// Determines the subtree root for efficient batch processing of changes
    /// </summary>
    private ObjectId GetSubtreeRoot(ObjectId nodeId)
    {
        // Strategy 1: Simple approach - use the node itself as root for small changes
        return nodeId;
    }

    /// <summary>
    /// Advanced subtree root determination with caching and hierarchy analysis
    /// </summary>
    private async Task<ObjectId> GetSubtreeRootAsync(ObjectId nodeId)
    {
        var cacheKey = $"subtree_root_{nodeId}";
        
        if (_cache.TryGetValue(cacheKey, out ObjectId cachedRoot))
        {
            return cachedRoot;
        }

        var subtreeRoot = await DetermineOptimalSubtreeRootAsync(nodeId);
        
        // Cache for 5 minutes
        _cache.Set(cacheKey, subtreeRoot, TimeSpan.FromMinutes(5));
        
        return subtreeRoot;
    }

    /// <summary>
    /// Determines optimal subtree root based on hierarchy depth and sibling count
    /// </summary>
    private async Task<ObjectId> DetermineOptimalSubtreeRootAsync(ObjectId nodeId)
    {
        try
        {
            var node = await GetNodeAsync(nodeId);
            if (node == null) return nodeId;

            // Strategy: Find a good batching root based on tree structure
            var strategy = await AnalyzeHierarchyForBatchingAsync(node);
            
            return strategy switch
            {
                BatchingStrategy.Self => nodeId,
                BatchingStrategy.Parent => node.ParentId ?? nodeId,
                BatchingStrategy.GrandParent => await GetGrandParentAsync(node) ?? nodeId,
                BatchingStrategy.NearestBranchRoot => await GetNearestBranchRootAsync(node) ?? nodeId,
                _ => nodeId
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error determining subtree root for {NodeId}, using node itself", nodeId);
            return nodeId;
        }
    }

    private async Task<TreeNode> GetNodeAsync(ObjectId nodeId)
    {
        return await _treeCollection
            .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, nodeId))
            .FirstOrDefaultAsync();
    }

    private async Task<BatchingStrategy> AnalyzeHierarchyForBatchingAsync(TreeNode node)
    {
        // Analyze the tree structure to determine the best batching strategy
        
        // Count siblings at current level
        var siblingCount = await CountSiblingsAsync(node);
        
        // Count direct children
        var childCount = await CountDirectChildrenAsync(node.Id);
        
        // Determine depth from root
        var depthFromRoot = CalculateDepthFromPath(node.Path);

        // Decision logic for batching strategy
        if (childCount > 20)
        {
            // Node has many children, use itself as root
            return BatchingStrategy.Self;
        }
        
        if (siblingCount > 10 && depthFromRoot > 2)
        {
            // Many siblings and not too close to root, use parent as root
            return BatchingStrategy.Parent;
        }
        
        if (depthFromRoot > 4 && siblingCount < 5)
        {
            // Deep in tree with few siblings, consider grandparent
            return BatchingStrategy.GrandParent;
        }
        
        if (depthFromRoot > 6)
        {
            // Very deep, find nearest branch root
            return BatchingStrategy.NearestBranchRoot;
        }

        return BatchingStrategy.Self;
    }

    private async Task<long> CountSiblingsAsync(TreeNode node)
    {
        var filter = Builders<TreeNode>.Filter.And(
            Builders<TreeNode>.Filter.Eq(x => x.ParentId, node.ParentId),
            Builders<TreeNode>.Filter.Ne(x => x.Id, node.Id)
        );
        
        return await _treeCollection.CountDocumentsAsync(filter);
    }

    private async Task<long> CountDirectChildrenAsync(ObjectId nodeId)
    {
        var filter = Builders<TreeNode>.Filter.Eq(x => x.ParentId, nodeId);
        return await _treeCollection.CountDocumentsAsync(filter);
    }

    private async Task<ObjectId?> GetGrandParentAsync(TreeNode node)
    {
        if (node.ParentId == null) return null;
        
        var parent = await GetNodeAsync(node.ParentId.Value);
        return parent?.ParentId;
    }

    private async Task<ObjectId?> GetNearestBranchRootAsync(TreeNode node)
    {
        // Find the nearest ancestor with significant branching (multiple children)
        var currentNode = node;
        const int significantBranchThreshold = 3;
        
        while (currentNode?.ParentId != null)
        {
            var parent = await GetNodeAsync(currentNode.ParentId.Value);
            if (parent == null) break;
            
            var childrenCount = await CountDirectChildrenAsync(parent.Id);
            if (childrenCount >= significantBranchThreshold)
            {
                return parent.Id;
            }
            
            currentNode = parent;
        }
        
        return currentNode?.Id ?? node.Id;
    }

    private int CalculateDepthFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private enum BatchingStrategy
    {
        Self,
        Parent, 
        GrandParent,
        NearestBranchRoot
    }
}
Alternative Implementations
1. Simple Level-Based Grouping
csharppublic class SimpleBatchedTreeChangeProcessor
{
    /// <summary>
    /// Simple implementation that groups by tree level for batching
    /// </summary>
    private ObjectId GetSubtreeRoot(ObjectId nodeId)
    {
        // For simple scenarios, just return the node itself
        // The batching will happen naturally by processing changes in order
        return nodeId;
    }

    /// <summary>
    /// Level-based grouping for more sophisticated batching
    /// </summary>
    private async Task<ObjectId> GetSubtreeRootByLevelAsync(ObjectId nodeId)
    {
        var node = await _treeCollection
            .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, nodeId))
            .FirstOrDefaultAsync();

        if (node == null) return nodeId;

        // Group changes by moving up the hierarchy based on level
        return node.Level switch
        {
            <= 2 => nodeId, // Root level nodes, process individually
            <= 4 => node.ParentId ?? nodeId, // Mid-level, group by parent
            _ => await GetAncestorAtLevelAsync(node, 3) ?? nodeId // Deep nodes, group by level-3 ancestor
        };
    }

    private async Task<ObjectId?> GetAncestorAtLevelAsync(TreeNode node, int targetLevel)
    {
        if (node.Level <= targetLevel) return node.Id;

        var pathSegments = node.Path?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments == null || pathSegments.Length <= targetLevel) return node.Id;

        // Extract ancestor ID from path at target level
        if (ObjectId.TryParse(pathSegments[targetLevel], out var ancestorId))
        {
            return ancestorId;
        }

        // Fallback: traverse up the hierarchy
        var current = node;
        while (current != null && current.Level > targetLevel && current.ParentId.HasValue)
        {
            current = await _treeCollection
                .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, current.ParentId.Value))
                .FirstOrDefaultAsync();
        }

        return current?.Id ?? node.Id;
    }
}
2. Hash-Based Grouping
csharppublic class HashBasedBatchedTreeChangeProcessor
{
    /// <summary>
    /// Hash-based grouping to distribute load evenly across batches
    /// </summary>
    private ObjectId GetSubtreeRoot(ObjectId nodeId)
    {
        // Use hash-based grouping for even distribution
        var hash = nodeId.ToString().GetHashCode();
        var groupSize = 1000; // Adjust based on your needs
        
        // This creates virtual groups for batching purposes
        // In practice, you'd need to map this back to actual tree structure
        return nodeId;
    }

    /// <summary>
    /// More sophisticated hash-based grouping with actual tree awareness
    /// </summary>
    private async Task<ObjectId> GetHashBasedSubtreeRootAsync(ObjectId nodeId)
    {
        var node = await GetNodeAsync(nodeId);
        if (node == null) return nodeId;

        // Create hash groups while respecting tree hierarchy
        var pathHash = node.Path?.GetHashCode() ?? 0;
        var bucketSize = 50; // Nodes per batch bucket
        
        // Find a suitable ancestor for this hash bucket
        var targetLevel = Math.Max(1, (Math.Abs(pathHash) % 4) + 1);
        return await GetAncestorAtLevelAsync(node, targetLevel) ?? nodeId;
    }

    private async Task<TreeNode> GetNodeAsync(ObjectId nodeId)
    {
        return await _treeCollection
            .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, nodeId))
            .FirstOrDefaultAsync();
    }

    private async Task<ObjectId?> GetAncestorAtLevelAsync(TreeNode node, int targetLevel)
    {
        if (node.Level <= targetLevel) return node.Id;

        var current = node;
        while (current?.ParentId != null && current.Level > targetLevel)
        {
            current = await GetNodeAsync(current.ParentId.Value);
        }

        return current?.Id ?? node.Id;
    }
}
3. Dynamic Batching with Load Awareness
csharppublic class LoadAwareBatchedTreeChangeProcessor
{
    private readonly ConcurrentDictionary<ObjectId, int> _activeProcessing = new();
    private readonly ConcurrentDictionary<ObjectId, DateTime> _lastProcessed = new();

    /// <summary>
    /// Dynamic subtree root selection based on current processing load
    /// </summary>
    private async Task<ObjectId> GetSubtreeRootAsync(ObjectId nodeId)
    {
        var node = await GetNodeAsync(nodeId);
        if (node == null) return nodeId;

        // Check if this subtree is already being processed
        var potentialRoots = await GetPotentialBatchingRootsAsync(node);
        
        foreach (var root in potentialRoots)
        {
            // Check if this root is not currently being heavily processed
            var currentLoad = _activeProcessing.GetOrAdd(root, 0);
            var lastProcessed = _lastProcessed.GetOrAdd(root, DateTime.MinValue);
            
            // Avoid roots that are currently busy or recently processed
            if (currentLoad < 3 && DateTime.UtcNow - lastProcessed > TimeSpan.FromSeconds(30))
            {
                return root;
            }
        }

        // Default to the node itself if all potential roots are busy
        return nodeId;
    }

    private async Task<List<ObjectId>> GetPotentialBatchingRootsAsync(TreeNode node)
    {
        var roots = new List<ObjectId> { node.Id };

        // Add parent as potential root
        if (node.ParentId.HasValue)
        {
            roots.Add(node.ParentId.Value);
            
            // Add grandparent if exists
            var parent = await GetNodeAsync(node.ParentId.Value);
            if (parent?.ParentId != null)
            {
                roots.Add(parent.ParentId.Value);
            }
        }

        return roots;
    }

    public void IncrementProcessingLoad(ObjectId subtreeRoot)
    {
        _activeProcessing.AddOrUpdate(subtreeRoot, 1, (key, value) => value + 1);
    }

    public void DecrementProcessingLoad(ObjectId subtreeRoot)
    {
        _activeProcessing.AddOrUpdate(subtreeRoot, 0, (key, value) => Math.Max(0, value - 1));
        _lastProcessed[subtreeRoot] = DateTime.UtcNow;
    }

    private async Task<TreeNode> GetNodeAsync(ObjectId nodeId)
    {
        return await _treeCollection
            .Find(Builders<TreeNode>.Filter.Eq(x => x.Id, nodeId))
            .FirstOrDefaultAsync();
    }
}
4. Configuration-Driven Approach
csharppublic class ConfigurableBatchedTreeChangeProcessor
{
    private readonly BatchingConfiguration _config;
    
    public class BatchingConfiguration
    {
        public BatchingMode Mode { get; set; } = BatchingMode.LevelBased;
        public int MaxBatchSize { get; set; } = 50;
        public int MinBatchingLevel { get; set; } = 3;
        public TimeSpan BatchingWindow { get; set; } = TimeSpan.FromSeconds(5);
    }

    public enum BatchingMode
    {
        None,           // Process each node individually
        LevelBased,     // Group by tree level
        ParentBased,    // Group by parent node
        Adaptive        // Dynamic based on tree structure
    }

    private ObjectId GetSubtreeRoot(ObjectId nodeId)
    {
        return _config.Mode switch
        {
            BatchingMode.None => nodeId,
            BatchingMode.LevelBased => GetLevelBasedRoot(nodeId),
            BatchingMode.ParentBased => GetParentBasedRoot(nodeId),
            BatchingMode.Adaptive => GetAdaptiveRoot(nodeId),
            _ => nodeId
        };
    }

    private ObjectId GetLevelBasedRoot(ObjectId nodeId)
    {
        // Implementation similar to level-based approach above
        return nodeId;
    }

    private ObjectId GetParentBasedRoot(ObjectId nodeId)
    {
        // Group changes by parent for better locality
        return nodeId; // Would need async version to get actual parent
    }

    private ObjectId GetAdaptiveRoot(ObjectId nodeId)
    {
        // Use heuristics to determine best grouping
        return nodeId; // Would need async version for tree analysis
    }
}
Updated ProcessBatch Method
csharpprivate async void ProcessBatch(object state)
{
    if (!await _processingLock.WaitAsync(100)) return;

    try
    {
        var changesToProcess = new List<TreeChangeEvent>();
        
        // Dequeue changes
        for (int i = 0; i < 100 && _changeQueue.TryDequeue(out var change); i++)
        {
            changesToProcess.Add(change);
        }

        if (!changesToProcess.Any()) return;

        // Group changes by subtree root for efficient processing
        var groupedChanges = await GroupChangesBySubtreeAsync(changesToProcess);

        foreach (var group in groupedChanges)
        {
            try
            {
                await _flatteningService.StreamFlattenSubtreeAsync(group.Key, CancellationToken.None);
                _logger.LogDebug("Processed batch for subtree {SubtreeRoot} with {Count} changes", 
                               group.Key, group.Value.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch for subtree {SubtreeRoot}", group.Key);
            }
        }
    }
    finally
    {
        _processingLock.Release();
    }
}

private async Task<Dictionary<ObjectId, List<TreeChangeEvent>>> GroupChangesBySubtreeAsync(
    List<TreeChangeEvent> changes)
{
    var groups = new Dictionary<ObjectId, List<TreeChangeEvent>>();

    foreach (var change in changes)
    {
        var subtreeRoot = await GetSubtreeRootAsync(change.NodeId);
        
        if (!groups.ContainsKey(subtreeRoot))
        {
            groups[subtreeRoot] = new List<TreeChangeEvent>();
        }
        
        groups[subtreeRoot].Add(change);
    }

    return groups;
}
The choice of GetSubtreeRoot implementation depends on your specific use case:

Simple approach: Just return the node ID for individual processing
Level-based: Group by tree hierarchy levels for balanced batches
Hash-based: Distribute load evenly across processing buckets
Load-aware: Adapt to current system load dynamically
Configurable: Allow runtime configuration of batching strategy

For most scenarios, the level-based approach provides a good balance of efficiency and simplicity.
