
Describe design of using mongodb streaming as etl or aggregations to flatten a tree datastructure and storing results in a cache like document and collection for retrieving trees and subtrees and searching in an C#, angular project displaying data in an ag-grid grouping by columns with not aggregate functions

I'll describe a comprehensive design for using MongoDB streaming aggregations to flatten tree structures with caching for efficient retrieval in a C#/Angular application with ag-Grid.

Architecture Overview
Data Model Design
Source Tree Collection (TreeNodes)

csharp
public class TreeNode
{
    public ObjectId Id { get; set; }
    public ObjectId? ParentId { get; set; }
    public string Name { get; set; }
    public int Level { get; set; }
    public string Path { get; set; } // "/root/branch1/leaf1"
    public Dictionary<string, object> Data { get; set; }
    public DateTime LastModified { get; set; }
}
Flattened Cache Collection (FlatTreeCache)

csharp
public class FlatTreeNode
{
    public ObjectId Id { get; set; }
    public ObjectId OriginalNodeId { get; set; }
    public ObjectId? ParentId { get; set; }
    public string[] AncestorIds { get; set; }
    public string[] AncestorNames { get; set; }
    public string FullPath { get; set; }
    public int Level { get; set; }
    public string NodeName { get; set; }
    public Dictionary<string, object> FlattenedData { get; set; }
    public DateTime CacheTimestamp { get; set; }
    public string TreeVersion { get; set; } // For cache invalidation
}
MongoDB Streaming ETL Pipeline
C# Aggregation Pipeline Service
csharp
public class TreeFlatteningService
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly IMongoCollection<FlatTreeNode> _cacheCollection;
    
    public async Task StreamFlattenTreeAsync(CancellationToken cancellationToken = default)
    {
        var pipeline = new[]
        {
            // Stage 1: Match active nodes
            new BsonDocument("$match", new BsonDocument("IsActive", true)),
            
            // Stage 2: Add computed fields
            new BsonDocument("$addFields", new BsonDocument
            {
                ["pathArray"] = new BsonDocument("$split", new BsonArray { "$Path", "/" }),
                ["ancestorCount"] = new BsonDocument("$size", 
                    new BsonDocument("$split", new BsonArray { "$Path", "/" }))
            }),
            
            // Stage 3: Lookup ancestors
            new BsonDocument("$graphLookup", new BsonDocument
            {
                ["from"] = "TreeNodes",
                ["startWith"] = "$ParentId",
                ["connectFromField"] = "ParentId",
                ["connectToField"] = "_id",
                ["as"] = "ancestors",
                ["depthField"] = "ancestorDepth"
            }),
            
            // Stage 4: Project flattened structure
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
                ["treeVersion"] = await GetCurrentTreeVersionAsync()
            })
        };

        var options = new AggregateOptions
        {
            AllowDiskUse = true,
            BatchSize = 1000
        };

        using var cursor = await _treeCollection.AggregateAsync<BsonDocument>(pipeline, options, cancellationToken);
        
        var bulkOps = new List<WriteModel<FlatTreeNode>>();
        const int batchSize = 500;

        await cursor.ForEachAsync(async doc =>
        {
            var flatNode = BsonSerializer.Deserialize<FlatTreeNode>(doc);
            
            bulkOps.Add(new ReplaceOneModel<FlatTreeNode>(
                Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, flatNode.OriginalNodeId),
                flatNode)
            {
                IsUpsert = true
            });

            if (bulkOps.Count >= batchSize)
            {
                await _cacheCollection.BulkWriteAsync(bulkOps);
                bulkOps.Clear();
            }
        }, cancellationToken);

        if (bulkOps.Any())
        {
            await _cacheCollection.BulkWriteAsync(bulkOps);
        }
    }
}
C# API Layer
Repository with Optimized Queries
csharp
public class TreeCacheRepository
{
    private readonly IMongoCollection<FlatTreeNode> _collection;
    
    public async Task<List<FlatTreeNode>> GetTreeAsync(ObjectId? rootId = null, 
        int? maxLevels = null, string searchTerm = null)
    {
        var filterBuilder = Builders<FlatTreeNode>.Filter;
        var filter = filterBuilder.Empty;
        
        if (rootId.HasValue)
        {
            filter &= filterBuilder.Or(
                filterBuilder.Eq(x => x.OriginalNodeId, rootId.Value),
                filterBuilder.AnyEq(x => x.AncestorIds, rootId.Value.ToString())
            );
        }
        
        if (maxLevels.HasValue)
        {
            filter &= filterBuilder.Lte(x => x.Level, maxLevels.Value);
        }
        
        if (!string.IsNullOrEmpty(searchTerm))
        {
            filter &= filterBuilder.Or(
                filterBuilder.Regex(x => x.NodeName, new BsonRegularExpression(searchTerm, "i")),
                filterBuilder.Regex(x => x.FullPath, new BsonRegularExpression(searchTerm, "i"))
            );
        }
        
        return await _collection
            .Find(filter)
            .Sort(Builders<FlatTreeNode>.Sort.Ascending(x => x.FullPath))
            .ToListAsync();
    }
    
    public async Task<List<FlatTreeNode>> GetSubtreeAsync(ObjectId parentId, int depth = int.MaxValue)
    {
        var filter = Builders<FlatTreeNode>.Filter.And(
            Builders<FlatTreeNode>.Filter.AnyEq(x => x.AncestorIds, parentId.ToString()),
            Builders<FlatTreeNode>.Filter.Lte(x => x.Level, depth)
        );
        
        return await _collection
            .Find(filter)
            .Sort(Builders<FlatTreeNode>.Sort.Ascending(x => x.Level).Ascending(x => x.NodeName))
            .ToListAsync();
    }
}
API Controller
csharp
[ApiController]
[Route("api/[controller]")]
public class TreeController : ControllerBase
{
    private readonly TreeCacheRepository _repository;
    private readonly TreeFlatteningService _flatteningService;
    
    [HttpGet("hierarchy")]
    public async Task<ActionResult<TreeGridResponse>> GetHierarchy(
        [FromQuery] string? rootId = null,
        [FromQuery] int? maxLevels = null,
        [FromQuery] string? search = null,
        [FromQuery] string[]? groupColumns = null)
    {
        ObjectId? rootObjectId = null;
        if (!string.IsNullOrEmpty(rootId) && ObjectId.TryParse(rootId, out var parsed))
            rootObjectId = parsed;
            
        var nodes = await _repository.GetTreeAsync(rootObjectId, maxLevels, search);
        
        var response = new TreeGridResponse
        {
            Data = nodes.Select(MapToGridRow).ToList(),
            GroupColumns = groupColumns ?? new[] { "level", "ancestorNames" },
            TotalCount = nodes.Count
        };
        
        return Ok(response);
    }
    
    [HttpPost("refresh-cache")]
    public async Task<ActionResult> RefreshCache()
    {
        await _flatteningService.StreamFlattenTreeAsync();
        return Ok(new { message = "Cache refresh initiated" });
    }
}
Angular Frontend Implementation
TypeScript Models
typescript
export interface TreeGridRow {
  id: string;
  originalNodeId: string;
  parentId?: string;
  ancestorIds: string[];
  ancestorNames: string[];
  fullPath: string;
  level: number;
  nodeName: string;
  flattenedData: Record<string, any>;
  
  // Computed properties for ag-Grid
  _groupLevel?: number;
  _groupKey?: string;
  _isGroupRow?: boolean;
}

export interface TreeGridResponse {
  data: TreeGridRow[];
  groupColumns: string[];
  totalCount: number;
}
Angular Service
typescript
@Injectable()
export class TreeDataService {
  private readonly apiUrl = '/api/tree';
  
  constructor(private http: HttpClient) {}
  
  getHierarchyData(params: {
    rootId?: string;
    maxLevels?: number;
    search?: string;
    groupColumns?: string[];
  }): Observable<TreeGridResponse> {
    const queryParams = new HttpParams({ fromObject: params as any });
    return this.http.get<TreeGridResponse>(`${this.apiUrl}/hierarchy`, { params: queryParams });
  }
  
  refreshCache(): Observable<any> {
    return this.http.post(`${this.apiUrl}/refresh-cache`, {});
  }
}
ag-Grid Component
typescript
@Component({
  template: `
    <div class="tree-grid-container">
      <div class="controls">
        <input [(ngModel)]="searchTerm" 
               (input)="onSearchChange()" 
               placeholder="Search tree...">
        <button (click)="refreshData()">Refresh</button>
        <button (click)="expandAll()">Expand All</button>
        <button (click)="collapseAll()">Collapse All</button>
      </div>
      
      <ag-grid-angular
        class="ag-theme-alpine"
        [gridOptions]="gridOptions"
        [columnDefs]="columnDefs"
        [rowData]="rowData"
        [autoGroupColumnDef]="autoGroupColumnDef"
        [groupDefaultExpanded]="groupDefaultExpanded"
        [getDataPath]="getDataPath"
        [treeData]="true"
        (gridReady)="onGridReady($event)">
      </ag-grid-angular>
    </div>
  `
})
export class TreeGridComponent implements OnInit {
  gridApi!: GridApi;
  searchTerm = '';
  rowData: TreeGridRow[] = [];
  
  columnDefs: ColDef[] = [
    {
      field: 'nodeName',
      headerName: 'Node Name',
      flex: 1,
      cellRenderer: 'agGroupCellRenderer'
    },
    {
      field: 'level',
      headerName: 'Level',
      width: 100,
      cellClass: 'number-cell'
    },
    {
      field: 'fullPath',
      headerName: 'Full Path',
      flex: 2,
      tooltipField: 'fullPath'
    },
    // Dynamic columns based on flattenedData
    ...this.getDynamicColumns()
  ];
  
  autoGroupColumnDef: ColDef = {
    headerName: 'Tree Structure',
    field: 'nodeName',
    cellRenderer: 'agGroupCellRenderer',
    flex: 1
  };
  
  gridOptions: GridOptions = {
    groupDefaultExpanded: 1,
    suppressAggFuncInHeader: true,
    animateRows: true,
    enableRangeSelection: true
  };
  
  constructor(private treeService: TreeDataService) {}
  
  ngOnInit() {
    this.loadData();
  }
  
  getDataPath = (data: TreeGridRow): string[] => {
    // Convert the hierarchical path to ag-Grid's expected format
    const pathParts = data.fullPath.split('/').filter(part => part.length > 0);
    return pathParts;
  };
  
  onGridReady(params: GridReadyEvent) {
    this.gridApi = params.api;
  }
  
  loadData() {
    this.treeService.getHierarchyData({
      search: this.searchTerm || undefined,
      maxLevels: 10
    }).subscribe(response => {
      this.rowData = response.data;
      this.updateDynamicColumns(response.data);
    });
  }
  
  onSearchChange() {
    debounceTime(300);
    this.loadData();
  }
  
  private getDynamicColumns(): ColDef[] {
    // Extract unique keys from flattenedData to create dynamic columns
    const dynamicKeys = new Set<string>();
    this.rowData.forEach(row => {
      Object.keys(row.flattenedData || {}).forEach(key => {
        if (!key.startsWith('_')) { // Skip internal fields
          dynamicKeys.add(key);
        }
      });
    });
    
    return Array.from(dynamicKeys).map(key => ({
      field: `flattenedData.${key}`,
      headerName: this.formatHeaderName(key),
      flex: 1,
      valueGetter: (params: ValueGetterParams) => {
        return params.data?.flattenedData?.[key];
      }
    }));
  }
  
  private formatHeaderName(key: string): string {
    return key.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());
  }
  
  expandAll() {
    this.gridApi.expandAll();
  }
  
  collapseAll() {
    this.gridApi.collapseAll();
  }
  
  refreshData() {
    this.treeService.refreshCache().subscribe(() => {
      this.loadData();
    });
  }
}
Performance Optimizations
MongoDB Indexing Strategy
javascript
// Compound indexes for efficient querying
db.FlatTreeCache.createIndex({ "originalNodeId": 1, "treeVersion": 1 });
db.FlatTreeCache.createIndex({ "ancestorIds": 1, "level": 1 });
db.FlatTreeCache.createIndex({ "fullPath": 1 });
db.FlatTreeCache.createIndex({ "nodeName": "text", "fullPath": "text" });
db.FlatTreeCache.createIndex({ "cacheTimestamp": 1 });

// For the source collection
db.TreeNodes.createIndex({ "ParentId": 1, "Level": 1 });
db.TreeNodes.createIndex({ "Path": 1 });
db.TreeNodes.createIndex({ "LastModified": 1 });
Cache Invalidation Strategy
csharp
public class TreeCacheInvalidationService
{
    public async Task InvalidateSubtreeAsync(ObjectId nodeId)
    {
        var filter = Builders<FlatTreeNode>.Filter.Or(
            Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, nodeId),
            Builders<FlatTreeNode>.Filter.AnyEq(x => x.AncestorIds, nodeId.ToString())
        );
        
        await _cacheCollection.DeleteManyAsync(filter);
        
        // Trigger background refresh for affected subtree
        _ = Task.Run(() => _flatteningService.StreamFlattenSubtreeAsync(nodeId));
    }
}
This design provides efficient tree flattening using MongoDB's aggregation pipeline, maintains a searchable cache, and delivers data optimized for ag-Grid's tree/grouping capabilities without aggregate functions. The streaming approach handles large datasets efficiently while the flattened cache structure enables fast retrieval and searching operations.

When node changes it trigger streaming to update cach collection

I'll design a comprehensive change detection and streaming system that automatically triggers cache updates when tree nodes change.

Change Detection Architecture
1. MongoDB Change Streams Implementation
csharp
public class TreeChangeStreamService : BackgroundService
{
    private readonly IMongoCollection<TreeNode> _treeCollection;
    private readonly TreeFlatteningService _flatteningService;
    private readonly TreeCacheInvalidationService _invalidationService;
    private readonly ILogger<TreeChangeStreamService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<TreeNode>>()
            .Match(change => change.OperationType == ChangeStreamOperationType.Insert ||
                           change.OperationType == ChangeStreamOperationType.Update ||
                           change.OperationType == ChangeStreamOperationType.Delete ||
                           change.OperationType == ChangeStreamOperationType.Replace);

        var options = new ChangeStreamOptions
        {
            FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
            BatchSize = 100
        };

        try
        {
            using var changeStream = await _treeCollection.WatchAsync(pipeline, options, stoppingToken);
            
            await changeStream.ForEachAsync(async change =>
            {
                await ProcessChangeAsync(change, stoppingToken);
            }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Change stream monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in change stream monitoring");
            throw;
        }
    }

    private async Task ProcessChangeAsync(ChangeStreamDocument<TreeNode> change, CancellationToken cancellationToken)
    {
        try
        {
            switch (change.OperationType)
            {
                case ChangeStreamOperationType.Insert:
                    await HandleNodeInsertAsync(change.FullDocument, cancellationToken);
                    break;

                case ChangeStreamOperationType.Update:
                case ChangeStreamOperationType.Replace:
                    await HandleNodeUpdateAsync(change.DocumentKey["_id"].AsObjectId, 
                                              change.FullDocument, cancellationToken);
                    break;

                case ChangeStreamOperationType.Delete:
                    await HandleNodeDeleteAsync(change.DocumentKey["_id"].AsObjectId, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing change for document {DocumentId}", 
                           change.DocumentKey["_id"]);
        }
    }
}
2. Change Processing Logic
csharp
public class TreeChangeProcessor
{
    private readonly TreeFlatteningService _flatteningService;
    private readonly TreeCacheInvalidationService _invalidationService;
    private readonly ILogger<TreeChangeProcessor> _logger;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    public async Task HandleNodeInsertAsync(TreeNode newNode, CancellationToken cancellationToken)
    {
        await _processingLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Processing insert for node {NodeId}: {NodeName}", 
                                 newNode.Id, newNode.Name);

            // Stream flatten the new node and its impact
            await _flatteningService.StreamFlattenNodeAsync(newNode.Id, cancellationToken);

            // If this node has children, they might need path updates
            await InvalidateChildrenPathsAsync(newNode.Id, cancellationToken);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task HandleNodeUpdateAsync(ObjectId nodeId, TreeNode updatedNode, 
                                          CancellationToken cancellationToken)
    {
        await _processingLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Processing update for node {NodeId}", nodeId);

            // Get the old cached version to determine what changed
            var oldCachedNode = await GetCachedNodeAsync(nodeId);
            
            var changeType = DetermineChangeType(oldCachedNode, updatedNode);
            
            switch (changeType)
            {
                case NodeChangeType.DataOnly:
                    // Only data changed, update single node
                    await _flatteningService.StreamFlattenNodeAsync(nodeId, cancellationToken);
                    break;

                case NodeChangeType.StructuralChange:
                    // Parent changed or path changed, need to update entire subtree
                    await HandleStructuralChangeAsync(nodeId, updatedNode, oldCachedNode, cancellationToken);
                    break;

                case NodeChangeType.NameChange:
                    // Name changed, affects paths of all descendants
                    await HandleNameChangeAsync(nodeId, updatedNode, cancellationToken);
                    break;
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task HandleNodeDeleteAsync(ObjectId nodeId, CancellationToken cancellationToken)
    {
        await _processingLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Processing delete for node {NodeId}", nodeId);

            // Remove the node and all its descendants from cache
            await _invalidationService.InvalidateSubtreeAsync(nodeId);

            // Check if any other nodes were affected (reparenting scenarios)
            await RecalculateOrphanedNodesAsync(nodeId, cancellationToken);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private NodeChangeType DetermineChangeType(FlatTreeNode oldNode, TreeNode newNode)
    {
        if (oldNode == null) return NodeChangeType.StructuralChange;

        if (oldNode.ParentId != newNode.ParentId)
            return NodeChangeType.StructuralChange;

        if (!oldNode.FullPath.Equals(newNode.Path, StringComparison.OrdinalIgnoreCase))
            return NodeChangeType.StructuralChange;

        if (!oldNode.NodeName.Equals(newNode.Name, StringComparison.OrdinalIgnoreCase))
            return NodeChangeType.NameChange;

        return NodeChangeType.DataOnly;
    }
}

public enum NodeChangeType
{
    DataOnly,
    NameChange,
    StructuralChange
}
3. Optimized Streaming Service for Individual Nodes
csharp
public partial class TreeFlatteningService
{
    public async Task StreamFlattenNodeAsync(ObjectId nodeId, CancellationToken cancellationToken = default)
    {
        var pipeline = CreateNodeFlatteningPipeline(nodeId);
        
        var options = new AggregateOptions { AllowDiskUse = true };
        using var cursor = await _treeCollection.AggregateAsync<BsonDocument>(pipeline, options, cancellationToken);
        
        await cursor.ForEachAsync(async doc =>
        {
            var flatNode = BsonSerializer.Deserialize<FlatTreeNode>(doc);
            await UpsertFlatNodeAsync(flatNode);
        }, cancellationToken);
    }

    public async Task StreamFlattenSubtreeAsync(ObjectId rootNodeId, CancellationToken cancellationToken = default)
    {
        // First, invalidate existing cache for the subtree
        await _invalidationService.InvalidateSubtreeAsync(rootNodeId);

        // Create pipeline for the entire subtree
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("_id", rootNodeId),
                new BsonDocument("Path", new BsonDocument("$regex", $".*{rootNodeId}.*"))
            })),
            
            // Rest of the flattening pipeline...
            CreateFlatteningStages()
        }.SelectMany(x => x is BsonArray array ? array.Cast<BsonDocument>() : new[] { x }).ToArray();

        var options = new AggregateOptions 
        { 
            AllowDiskUse = true,
            BatchSize = 500 
        };

        using var cursor = await _treeCollection.AggregateAsync<BsonDocument>(pipeline, options, cancellationToken);
        
        var bulkOps = new List<WriteModel<FlatTreeNode>>();
        const int batchSize = 200;

        await cursor.ForEachAsync(async doc =>
        {
            var flatNode = BsonSerializer.Deserialize<FlatTreeNode>(doc);
            
            bulkOps.Add(new ReplaceOneModel<FlatTreeNode>(
                Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, flatNode.OriginalNodeId),
                flatNode) { IsUpsert = true });

            if (bulkOps.Count >= batchSize)
            {
                await _cacheCollection.BulkWriteAsync(bulkOps, cancellationToken: cancellationToken);
                bulkOps.Clear();
            }
        }, cancellationToken);

        if (bulkOps.Any())
        {
            await _cacheCollection.BulkWriteAsync(bulkOps, cancellationToken: cancellationToken);
        }
    }

    private BsonDocument[] CreateNodeFlatteningPipeline(ObjectId nodeId)
    {
        return new[]
        {
            new BsonDocument("$match", new BsonDocument("_id", nodeId)),
            
            // Add the standard flattening stages
            new BsonDocument("$addFields", new BsonDocument
            {
                ["pathArray"] = new BsonDocument("$split", new BsonArray { "$Path", "/" }),
                ["ancestorCount"] = new BsonDocument("$size", 
                    new BsonDocument("$split", new BsonArray { "$Path", "/" }))
            }),
            
            new BsonDocument("$graphLookup", new BsonDocument
            {
                ["from"] = "TreeNodes",
                ["startWith"] = "$ParentId",
                ["connectFromField"] = "ParentId", 
                ["connectToField"] = "_id",
                ["as"] = "ancestors",
                ["depthField"] = "ancestorDepth"
            }),
            
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
                        ["_lastModified"] = "$LastModified"
                    }
                }),
                ["cacheTimestamp"] = "$$NOW",
                ["treeVersion"] = new BsonDocument("$toString", "$$NOW")
            })
        };
    }

    private async Task UpsertFlatNodeAsync(FlatTreeNode flatNode)
    {
        await _cacheCollection.ReplaceOneAsync(
            Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, flatNode.OriginalNodeId),
            flatNode,
            new ReplaceOptions { IsUpsert = true });
    }
}
4. Structural Change Handling
csharp
public partial class TreeChangeProcessor
{
    private async Task HandleStructuralChangeAsync(ObjectId nodeId, TreeNode updatedNode, 
                                                 FlatTreeNode oldCachedNode, CancellationToken cancellationToken)
    {
        // If parent changed, we need to recalculate the entire subtree
        if (oldCachedNode?.ParentId != updatedNode.ParentId)
        {
            _logger.LogInformation("Parent changed for node {NodeId}, recalculating subtree", nodeId);
            
            // Remove old subtree from cache
            await _invalidationService.InvalidateSubtreeAsync(nodeId);
            
            // Recalculate the moved subtree with new parent hierarchy
            await _flatteningService.StreamFlattenSubtreeAsync(nodeId, cancellationToken);
        }
        else
        {
            // Path or level changed but parent didn't - update subtree paths
            await UpdateSubtreePathsAsync(nodeId, cancellationToken);
        }
    }

    private async Task HandleNameChangeAsync(ObjectId nodeId, TreeNode updatedNode, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Name changed for node {NodeId}, updating paths for subtree", nodeId);
        
        // Name change affects the full path of all descendants
        await _invalidationService.InvalidateSubtreeAsync(nodeId);
        await _flatteningService.StreamFlattenSubtreeAsync(nodeId, cancellationToken);
    }

    private async Task InvalidateChildrenPathsAsync(ObjectId parentId, CancellationToken cancellationToken)
    {
        // Find all nodes that have this as a parent and might need path recalculation
        var childrenFilter = Builders<TreeNode>.Filter.Eq(x => x.ParentId, parentId);
        var children = await _treeCollection.Find(childrenFilter).ToListAsync(cancellationToken);

        foreach (var child in children)
        {
            await _flatteningService.StreamFlattenSubtreeAsync(child.Id, cancellationToken);
        }
    }

    private async Task RecalculateOrphanedNodesAsync(ObjectId deletedNodeId, CancellationToken cancellationToken)
    {
        // Find nodes that might have been reparented after deletion
        var potentialOrphansFilter = Builders<TreeNode>.Filter.Eq(x => x.ParentId, deletedNodeId);
        var orphans = await _treeCollection.Find(potentialOrphansFilter).ToListAsync(cancellationToken);

        foreach (var orphan in orphans)
        {
            await _flatteningService.StreamFlattenSubtreeAsync(orphan.Id, cancellationToken);
        }
    }

    private async Task<FlatTreeNode> GetCachedNodeAsync(ObjectId nodeId)
    {
        return await _cacheCollection
            .Find(Builders<FlatTreeNode>.Filter.Eq(x => x.OriginalNodeId, nodeId))
            .FirstOrDefaultAsync();
    }
}
5. Real-time Notifications to Frontend
csharp
public class TreeChangeNotificationHub : Hub
{
    public async Task JoinTreeGroup(string treeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tree_{treeId}");
    }

    public async Task LeaveTreeGroup(string treeId)  
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tree_{treeId}");
    }
}

public class TreeChangeNotificationService
{
    private readonly IHubContext<TreeChangeNotificationHub> _hubContext;

    public async Task NotifyTreeChanged(ObjectId nodeId, string changeType, ObjectId? treeRootId = null)
    {
        var notification = new
        {
            NodeId = nodeId.ToString(),
            ChangeType = changeType,
            Timestamp = DateTime.UtcNow,
            TreeRootId = treeRootId?.ToString()
        };

        var groupName = treeRootId.HasValue ? $"tree_{treeRootId}" : "tree_global";
        await _hubContext.Clients.Group(groupName).SendAsync("TreeNodeChanged", notification);
    }
}
6. Performance Optimization with Batching
csharp
public class BatchedTreeChangeProcessor
{
    private readonly ConcurrentQueue<TreeChangeEvent> _changeQueue = new();
    private readonly Timer _batchTimer;
    private readonly TreeFlatteningService _flatteningService;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    public BatchedTreeChangeProcessor(TreeFlatteningService flatteningService)
    {
        _flatteningService = flatteningService;
        _batchTimer = new Timer(ProcessBatch, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void QueueChange(TreeChangeEvent changeEvent)
    {
        _changeQueue.Enqueue(changeEvent);
    }

    private async void ProcessBatch(object state)
    {
        if (!await _processingLock.WaitAsync(100)) return;

        try
        {
            var changesToProcess = new List<TreeChangeEvent>();
            
            // Dequeue up to 100 changes
            for (int i = 0; i < 100 && _changeQueue.TryDequeue(out var change); i++)
            {
                changesToProcess.Add(change);
            }

            if (!changesToProcess.Any()) return;

            // Group changes by subtree to optimize processing
            var groupedChanges = changesToProcess
                .GroupBy(c => GetSubtreeRoot(c.NodeId))
                .ToList();

            foreach (var group in groupedChanges)
            {
                await _flatteningService.StreamFlattenSubtreeAsync(group.Key, CancellationToken.None);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }
}

public class TreeChangeEvent
{
    public ObjectId NodeId { get; set; }
    public string ChangeType { get; set; }
    public DateTime Timestamp { get; set; }
    public TreeNode NodeData { get; set; }
}
7. Frontend Real-time Updates
typescript
@Injectable()
export class TreeChangeNotificationService {
  private hubConnection: HubConnection;
  private treeChanges$ = new Subject<TreeChangeNotification>();

  constructor() {
    this.hubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/tree-changes')
      .build();

    this.hubConnection.start();
    
    this.hubConnection.on('TreeNodeChanged', (notification: TreeChangeNotification) => {
      this.treeChanges$.next(notification);
    });
  }

  joinTreeGroup(treeId: string) {
    return this.hubConnection.invoke('JoinTreeGroup', treeId);
  }

  getTreeChanges(): Observable<TreeChangeNotification> {
    return this.treeChanges$.asObservable();
  }
}

// In your tree grid component
export class TreeGridComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  ngOnInit() {
    this.treeChangeService.getTreeChanges()
      .pipe(takeUntil(this.destroy$))
      .subscribe(change => {
        this.handleTreeChange(change);
      });

    this.treeChangeService.joinTreeGroup(this.currentTreeId);
  }

  private handleTreeChange(change: TreeChangeNotification) {
    switch (change.changeType) {
      case 'insert':
      case 'update':
        this.refreshSpecificNode(change.nodeId);
        break;
      case 'delete':
        this.removeNodeFromGrid(change.nodeId);
        break;
      case 'structural':
        this.refreshEntireGrid();
        break;
    }
  }
}
This design ensures that any change to the tree structure automatically triggers the appropriate streaming aggregation to update the cache, with optimizations for batching, real-time notifications, and minimal impact on performance.






