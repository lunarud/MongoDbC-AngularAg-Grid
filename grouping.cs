
difference client vs server SSRM ag-grid Row Column Grouping angular

In AG-Grid's Server-Side Row Model (SSRM), row and column grouping can be handled either on the client-side or server-side, each with distinct implications for performance and implementation.

Client-Side Grouping in SSRM
How it works:

The server returns flat data to AG-Grid
AG-Grid performs the grouping logic in the browser
All group operations (expand/collapse, aggregations) happen client-side
Pros:

Simpler server implementation - just return raw data
Rich interactive features work out-of-the-box
Good for smaller datasets (thousands of rows)
Cons:

Performance degrades with large datasets
All data must be loaded to perform grouping
Memory consumption increases significantly
Angular Implementation:

typescript
gridOptions: GridOptions = {
  rowModelType: 'serverSide',
  rowGroupPanelShow: 'always',
  groupDefaultExpanded: 1,
  
  columnDefs: [
    { field: 'country', rowGroup: true, hide: true },
    { field: 'sport', rowGroup: true, hide: true },
    { field: 'athlete' },
    { field: 'gold', aggFunc: 'sum' }
  ],

  onGridReady: (params) => {
    const datasource: IServerSideDatasource = {
      getRows: (params: IServerSideGetRowsParams) => {
        // Server returns flat data, AG-Grid handles grouping
        this.httpClient.get('/api/data', { params: this.buildParams(params) })
          .subscribe(data => {
            params.success({
              rowData: data.rows,
              rowCount: data.totalCount
            });
          });
      }
    };
    params.api.setServerSideDatasource(datasource);
  }
};
Server-Side Grouping in SSRM
How it works:

The server receives grouping instructions from AG-Grid
Server performs grouping, aggregation, and pagination
AG-Grid receives pre-grouped data structure
Pros:

Handles massive datasets efficiently
Lower memory footprint on client
Faster initial load times
Leverages database optimization (indexes, etc.)
Cons:

Complex server-side implementation required
Must handle group expand/collapse requests
Server must maintain grouping state
Angular Implementation:

typescript
gridOptions: GridOptions = {
  rowModelType: 'serverSide',
  serverSideStoreType: 'partial',
  rowGroupPanelShow: 'always',
  
  columnDefs: [
    { field: 'country', rowGroup: true, hide: true },
    { field: 'sport', rowGroup: true, hide: true },
    { field: 'athlete' },
    { field: 'gold', aggFunc: 'sum' }
  ],

  onGridReady: (params) => {
    const datasource: IServerSideDatasource = {
      getRows: (params: IServerSideGetRowsParams) => {
        // Build request with grouping information
        const request = {
          startRow: params.request.startRow,
          endRow: params.request.endRow,
          rowGroupCols: params.request.rowGroupCols,
          groupKeys: params.request.groupKeys,
          valueCols: params.request.valueCols
        };

        this.httpClient.post('/api/grouped-data', request)
          .subscribe(data => {
            params.success({
              rowData: data.rows, // Pre-grouped data
              rowCount: data.rowCount
            });
          });
      }
    };
    params.api.setServerSideDatasource(datasource);
  }
};
Server-Side Implementation Considerations
For server-side grouping, your API needs to handle:

csharp
// C# Example - handling the grouping request
public class GroupedDataRequest
{
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    public List<ColumnVO> RowGroupCols { get; set; }
    public List<string> GroupKeys { get; set; }
    public List<ColumnVO> ValueCols { get; set; }
}

// MongoDB aggregation pipeline example
public async Task<GroupedDataResponse> GetGroupedData(GroupedDataRequest request)
{
    var pipeline = new List<BsonDocument>();
    
    // Build grouping stages based on request.RowGroupCols
    foreach (var groupCol in request.RowGroupCols)
    {
        pipeline.Add(new BsonDocument("$group", new BsonDocument
        {
            { "_id", $"${groupCol.Field}" },
            { "count", new BsonDocument("$sum", 1) }
        }));
    }
    
    // Execute aggregation
    var results = await collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    
    return new GroupedDataResponse
    {
        Rows = TransformToGridFormat(results),
        RowCount = results.Count
    };
}
Recommendation
For your architecture:

Use client-side grouping for datasets < 10,000 rows or when you need rapid prototyping
Use server-side grouping for large datasets, especially when leveraging MongoDB's powerful aggregation framework for optimal performance
The choice often depends on your data volume, server capabilities, and the complexity you're willing to handle on the backend.

define TransformToGridFormat

Here's a comprehensive implementation of TransformToGridFormat for converting MongoDB aggregation results into AG-Grid's expected format for server-side row grouping:

csharp
public class GroupedDataResponse
{
    public List<object> Rows { get; set; }
    public int RowCount { get; set; }
}

public class GridRowData
{
    public string Group { get; set; }
    public Dictionary<string, object> Data { get; set; }
    public bool IsGroup { get; set; }
    public int ChildCount { get; set; }
    public string[] GroupKeys { get; set; }
}

private List<object> TransformToGridFormat(
    List<BsonDocument> mongoResults, 
    GroupedDataRequest request)
{
    var gridRows = new List<object>();
    
    // Determine the current grouping level based on GroupKeys
    var currentLevel = request.GroupKeys?.Count ?? 0;
    var totalLevels = request.RowGroupCols?.Count ?? 0;
    var isLeafLevel = currentLevel >= totalLevels;
    
    foreach (var doc in mongoResults)
    {
        if (isLeafLevel)
        {
            // Leaf level - return actual data rows
            gridRows.Add(TransformLeafRow(doc));
        }
        else
        {
            // Group level - return group headers
            gridRows.Add(TransformGroupRow(doc, request, currentLevel));
        }
    }
    
    return gridRows;
}

private object TransformLeafRow(BsonDocument doc)
{
    var row = new Dictionary<string, object>();
    
    foreach (var element in doc.Elements)
    {
        var key = element.Name;
        var value = element.Value;
        
        // Convert BSON types to .NET types
        row[key] = ConvertBsonValue(value);
    }
    
    return row;
}

private object TransformGroupRow(BsonDocument doc, GroupedDataRequest request, int level)
{
    var groupCol = request.RowGroupCols[level];
    var groupValue = doc["_id"].ToString();
    var childCount = doc.Contains("count") ? doc["count"].AsInt32 : 0;
    
    // Build the group keys array (parent groups + current group)
    var groupKeys = new List<string>();
    if (request.GroupKeys != null)
    {
        groupKeys.AddRange(request.GroupKeys);
    }
    groupKeys.Add(groupValue);
    
    var groupRow = new Dictionary<string, object>
    {
        // The field being grouped by should contain the group value
        [groupCol.Field] = groupValue,
        
        // AG-Grid specific properties for group rows
        ["ag-Grid-AutoColumn"] = groupValue,
        
        // Add aggregated values if they exist
    };
    
    // Add aggregated values from MongoDB results
    AddAggregatedValues(groupRow, doc, request.ValueCols);
    
    // Add AG-Grid group metadata
    groupRow["__agGridGroupData"] = new
    {
        isGroup = true,
        key = groupValue,
        field = groupCol.Field,
        childCount = childCount,
        level = level,
        expanded = false // Initially collapsed
    };
    
    return groupRow;
}

private void AddAggregatedValues(
    Dictionary<string, object> groupRow, 
    BsonDocument doc, 
    List<ColumnVO> valueCols)
{
    if (valueCols == null) return;
    
    foreach (var valueCol in valueCols)
    {
        var aggFieldName = GetAggregatedFieldName(valueCol);
        
        if (doc.Contains(aggFieldName))
        {
            groupRow[valueCol.Field] = ConvertBsonValue(doc[aggFieldName]);
        }
    }
}

private string GetAggregatedFieldName(ColumnVO valueCol)
{
    // Map AG-Grid aggregation function to MongoDB field name
    return valueCol.AggFunc switch
    {
        "sum" => $"{valueCol.Field}_sum",
        "avg" => $"{valueCol.Field}_avg", 
        "min" => $"{valueCol.Field}_min",
        "max" => $"{valueCol.Field}_max",
        "count" => $"{valueCol.Field}_count",
        _ => valueCol.Field
    };
}

private object ConvertBsonValue(BsonValue bsonValue)
{
    return bsonValue.BsonType switch
    {
        BsonType.String => bsonValue.AsString,
        BsonType.Int32 => bsonValue.AsInt32,
        BsonType.Int64 => bsonValue.AsInt64,
        BsonType.Double => bsonValue.AsDouble,
        BsonType.Decimal128 => bsonValue.AsDecimal,
        BsonType.Boolean => bsonValue.AsBoolean,
        BsonType.DateTime => bsonValue.AsDateTime,
        BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
        BsonType.Null => null,
        _ => bsonValue.ToString()
    };
}
Enhanced MongoDB Aggregation Pipeline
Here's an updated aggregation method that properly handles multi-level grouping:

csharp
public async Task<GroupedDataResponse> GetGroupedData(GroupedDataRequest request)
{
    var pipeline = new List<BsonDocument>();
    
    // Apply filters first (if any)
    if (request.FilterModel != null)
    {
        pipeline.Add(BuildFilterStage(request.FilterModel));
    }
    
    // Determine grouping level
    var currentLevel = request.GroupKeys?.Count ?? 0;
    var totalLevels = request.RowGroupCols?.Count ?? 0;
    
    if (currentLevel < totalLevels)
    {
        // We're at a group level - need to create group aggregation
        pipeline.AddRange(BuildGroupingPipeline(request, currentLevel));
    }
    else
    {
        // We're at leaf level - return filtered data
        pipeline.AddRange(BuildLeafPipeline(request));
    }
    
    // Execute pipeline
    var results = await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    
    return new GroupedDataResponse
    {
        Rows = TransformToGridFormat(results, request),
        RowCount = results.Count
    };
}

private List<BsonDocument> BuildGroupingPipeline(GroupedDataRequest request, int level)
{
    var pipeline = new List<BsonDocument>();
    var groupCol = request.RowGroupCols[level];
    
    // Match stage for parent group keys
    if (request.GroupKeys != null && request.GroupKeys.Count > 0)
    {
        var matchConditions = new BsonDocument();
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var parentCol = request.RowGroupCols[i];
            matchConditions[parentCol.Field] = request.GroupKeys[i];
        }
        pipeline.Add(new BsonDocument("$match", matchConditions));
    }
    
    // Group stage
    var groupStage = new BsonDocument("$group", new BsonDocument
    {
        { "_id", $"${groupCol.Field}" },
        { "count", new BsonDocument("$sum", 1) }
    });
    
    // Add aggregations for value columns
    if (request.ValueCols != null)
    {
        foreach (var valueCol in request.ValueCols)
        {
            var aggFieldName = GetAggregatedFieldName(valueCol);
            var aggExpression = valueCol.AggFunc switch
            {
                "sum" => new BsonDocument("$sum", $"${valueCol.Field}"),
                "avg" => new BsonDocument("$avg", $"${valueCol.Field}"),
                "min" => new BsonDocument("$min", $"${valueCol.Field}"),
                "max" => new BsonDocument("$max", $"${valueCol.Field}"),
                "count" => new BsonDocument("$sum", 1),
                _ => new BsonDocument("$first", $"${valueCol.Field}")
            };
            
            groupStage["$group"][aggFieldName] = aggExpression;
        }
    }
    
    pipeline.Add(groupStage);
    
    // Sort stage
    pipeline.Add(new BsonDocument("$sort", new BsonDocument("_id", 1)));
    
    // Pagination
    if (request.StartRow > 0)
    {
        pipeline.Add(new BsonDocument("$skip", request.StartRow));
    }
    
    if (request.EndRow > request.StartRow)
    {
        pipeline.Add(new BsonDocument("$limit", request.EndRow - request.StartRow));
    }
    
    return pipeline;
}

private List<BsonDocument> BuildLeafPipeline(GroupedDataRequest request)
{
    var pipeline = new List<BsonDocument>();
    
    // Match exact group path for leaf nodes
    if (request.GroupKeys != null && request.GroupKeys.Count > 0)
    {
        var matchConditions = new BsonDocument();
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchConditions[groupCol.Field] = request.GroupKeys[i];
        }
        pipeline.Add(new BsonDocument("$match", matchConditions));
    }
    
    // Pagination for leaf data
    if (request.StartRow > 0)
    {
        pipeline.Add(new BsonDocument("$skip", request.StartRow));
    }
    
    if (request.EndRow > request.StartRow)
    {
        pipeline.Add(new BsonDocument("$limit", request.EndRow - request.StartRow));
    }
    
    return pipeline;
}
This implementation handles:

Multi-level grouping with proper parent-child relationships
Aggregated values (sum, avg, min, max, count) at group levels
Pagination at both group and leaf levels
Type conversion from BSON to .NET types
AG-Grid specific metadata for proper rendering
The key is understanding that AG-Grid expects different data structures for group rows vs. leaf rows, and the grouping level is determined by the GroupKeys array length.

show code for only SSRM ag-grid C# mongodb Angular ag-grid Row Grouping Multiple Group Columns

Here's a complete implementation for AG-Grid SSRM with multiple group columns using C#, MongoDB, and Angular:

Angular Component
typescript
import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { 
  GridOptions, 
  IServerSideDatasource, 
  IServerSideGetRowsParams,
  ColDef
} from 'ag-grid-community';

@Component({
  selector: 'app-multi-group-grid',
  template: `
    <ag-grid-angular
      style="width: 100%; height: 600px;"
      class="ag-theme-alpine"
      [gridOptions]="gridOptions"
      [columnDefs]="columnDefs"
      (gridReady)="onGridReady($event)">
    </ag-grid-angular>
  `
})
export class MultiGroupGridComponent implements OnInit {
  
  public gridOptions: GridOptions = {};
  public columnDefs: ColDef[] = [];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.columnDefs = [
      // Group columns - these will create the hierarchy
      { 
        field: 'country', 
        rowGroup: true, 
        hide: true,
        headerName: 'Country'
      },
      { 
        field: 'year', 
        rowGroup: true, 
        hide: true,
        headerName: 'Year'
      },
      { 
        field: 'sport', 
        rowGroup: true, 
        hide: true,
        headerName: 'Sport'
      },
      
      // Display columns
      { field: 'athlete', headerName: 'Athlete' },
      { field: 'age', headerName: 'Age' },
      { 
        field: 'gold', 
        headerName: 'Gold',
        aggFunc: 'sum',
        enableValue: true
      },
      { 
        field: 'silver', 
        headerName: 'Silver',
        aggFunc: 'sum',
        enableValue: true
      },
      { 
        field: 'bronze', 
        headerName: 'Bronze',
        aggFunc: 'sum',
        enableValue: true
      }
    ];

    this.gridOptions = {
      rowModelType: 'serverSide',
      serverSideStoreType: 'partial',
      
      // Row grouping configuration
      rowGroupPanelShow: 'always',
      groupDefaultExpanded: 0, // Start collapsed
      groupSelectsChildren: true,
      groupSelectsFiltered: true,
      
      // Enable aggregation
      suppressAggFuncInHeader: true,
      groupAggFiltering: true,
      
      // Auto group column configuration
      autoGroupColumnDef: {
        headerName: 'Group',
        field: 'ag-Grid-AutoColumn',
        cellRenderer: 'agGroupCellRenderer',
        cellRendererParams: {
          suppressCount: false,
          checkbox: false
        },
        minWidth: 250
      },

      // Performance settings
      cacheBlockSize: 100,
      maxBlocksInCache: 10,
      purgeClosedRowNodes: true,
      maxConcurrentDatasourceRequests: 2,

      // Debug
      debug: false
    };
  }

  onGridReady(params: any) {
    const datasource: IServerSideDatasource = {
      getRows: (params: IServerSideGetRowsParams) => {
        console.log('Server request:', params.request);
        
        const request = {
          startRow: params.request.startRow,
          endRow: params.request.endRow,
          rowGroupCols: params.request.rowGroupCols || [],
          valueCols: params.request.valueCols || [],
          groupKeys: params.request.groupKeys || [],
          sortModel: params.request.sortModel || [],
          filterModel: params.request.filterModel || {}
        };

        this.http.post<any>('/api/olympics/grouped-data', request)
          .subscribe({
            next: (response) => {
              console.log('Server response:', response);
              params.success({
                rowData: response.rows,
                rowCount: response.rowCount
              });
            },
            error: (error) => {
              console.error('Error loading data:', error);
              params.fail();
            }
          });
      }
    };

    params.api.setServerSideDatasource(datasource);
  }
}
C# Backend Models
csharp
// Request models
public class ServerSideRequest
{
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    public List<ColumnVO> RowGroupCols { get; set; } = new();
    public List<ColumnVO> ValueCols { get; set; } = new();
    public List<string> GroupKeys { get; set; } = new();
    public List<SortModel> SortModel { get; set; } = new();
    public Dictionary<string, object> FilterModel { get; set; } = new();
}

public class ColumnVO
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public string Field { get; set; }
    public string AggFunc { get; set; }
}

public class SortModel
{
    public string ColId { get; set; }
    public string Sort { get; set; } // 'asc' or 'desc'
}

// Response models
public class ServerSideResponse
{
    public List<object> Rows { get; set; } = new();
    public int RowCount { get; set; }
}

// MongoDB document model
public class OlympicResult
{
    public ObjectId Id { get; set; }
    public string Athlete { get; set; }
    public int Age { get; set; }
    public string Country { get; set; }
    public int Year { get; set; }
    public string Date { get; set; }
    public string Sport { get; set; }
    public int Gold { get; set; }
    public int Silver { get; set; }
    public int Bronze { get; set; }
    public int Total { get; set; }
}
C# Controller
csharp
[ApiController]
[Route("api/[controller]")]
public class OlympicsController : ControllerBase
{
    private readonly OlympicsService _olympicsService;

    public OlympicsController(OlympicsService olympicsService)
    {
        _olympicsService = olympicsService;
    }

    [HttpPost("grouped-data")]
    public async Task<ActionResult<ServerSideResponse>> GetGroupedData(
        [FromBody] ServerSideRequest request)
    {
        try
        {
            var response = await _olympicsService.GetGroupedData(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
C# Service with MongoDB Aggregation
csharp
public class OlympicsService
{
    private readonly IMongoCollection<OlympicResult> _collection;

    public OlympicsService(IMongoDatabase database)
    {
        _collection = database.GetCollection<OlympicResult>("olympics");
    }

    public async Task<ServerSideResponse> GetGroupedData(ServerSideRequest request)
    {
        var currentLevel = request.GroupKeys.Count;
        var totalLevels = request.RowGroupCols.Count;
        var isLeafLevel = currentLevel >= totalLevels;

        List<BsonDocument> results;
        int totalCount;

        if (isLeafLevel)
        {
            // Get leaf data (actual records)
            results = await GetLeafData(request);
            totalCount = await GetLeafDataCount(request);
        }
        else
        {
            // Get group data
            results = await GetGroupData(request, currentLevel);
            totalCount = await GetGroupDataCount(request, currentLevel);
        }

        return new ServerSideResponse
        {
            Rows = TransformToGridFormat(results, request, isLeafLevel),
            RowCount = totalCount
        };
    }

    private async Task<List<BsonDocument>> GetGroupData(ServerSideRequest request, int level)
    {
        var pipeline = new List<BsonDocument>();

        // Add match stage for parent groups
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildParentGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Group by current level field
        var groupCol = request.RowGroupCols[level];
        var groupDoc = new BsonDocument("$group", new BsonDocument
        {
            { "_id", $"${groupCol.Field}" },
            { "childCount", new BsonDocument("$sum", 1) }
        });

        // Add aggregations for value columns
        foreach (var valueCol in request.ValueCols)
        {
            var aggExpression = GetAggregationExpression(valueCol);
            groupDoc["$group"][$"{valueCol.Field}_{valueCol.AggFunc}"] = aggExpression;
        }

        pipeline.Add(groupDoc);

        // Add sort
        var sortDoc = new BsonDocument("$sort", new BsonDocument("_id", 1));
        pipeline.Add(sortDoc);

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private async Task<List<BsonDocument>> GetLeafData(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        // Match exact group path
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Add sort
        if (request.SortModel.Count > 0)
        {
            var sortDoc = BuildSortStage(request.SortModel);
            pipeline.Add(new BsonDocument("$sort", sortDoc));
        }

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private BsonDocument BuildParentGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[groupCol.Field] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private BsonDocument BuildExactGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[groupCol.Field] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private BsonDocument GetAggregationExpression(ColumnVO valueCol)
    {
        return valueCol.AggFunc?.ToLower() switch
        {
            "sum" => new BsonDocument("$sum", $"${valueCol.Field}"),
            "avg" => new BsonDocument("$avg", $"${valueCol.Field}"),
            "min" => new BsonDocument("$min", $"${valueCol.Field}"),
            "max" => new BsonDocument("$max", $"${valueCol.Field}"),
            "count" => new BsonDocument("$sum", 1),
            _ => new BsonDocument("$sum", $"${valueCol.Field}")
        };
    }

    private async Task<int> GetGroupDataCount(ServerSideRequest request, int level)
    {
        var pipeline = new List<BsonDocument>();

        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildParentGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        var groupCol = request.RowGroupCols[level];
        pipeline.Add(new BsonDocument("$group", new BsonDocument
        {
            { "_id", $"${groupCol.Field}" }
        }));

        pipeline.Add(new BsonDocument("$count", "total"));

        var result = await _collection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        return result?["total"].AsInt32 ?? 0;
    }

    private async Task<int> GetLeafDataCount(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        pipeline.Add(new BsonDocument("$count", "total"));

        var result = await _collection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        return result?["total"].AsInt32 ?? 0;
    }

    private List<object> TransformToGridFormat(
        List<BsonDocument> results, 
        ServerSideRequest request, 
        bool isLeafLevel)
    {
        var gridRows = new List<object>();

        foreach (var doc in results)
        {
            if (isLeafLevel)
            {
                // Transform leaf row
                gridRows.Add(TransformLeafRow(doc));
            }
            else
            {
                // Transform group row
                var currentLevel = request.GroupKeys.Count;
                gridRows.Add(TransformGroupRow(doc, request, currentLevel));
            }
        }

        return gridRows;
    }

    private object TransformLeafRow(BsonDocument doc)
    {
        var row = new Dictionary<string, object>();

        foreach (var element in doc.Elements)
        {
            if (element.Name == "_id") continue;
            
            var value = ConvertBsonValue(element.Value);
            row[element.Name] = value;
        }

        return row;
    }

    private object TransformGroupRow(BsonDocument doc, ServerSideRequest request, int level)
    {
        var groupCol = request.RowGroupCols[level];
        var groupValue = doc["_id"].ToString();
        var childCount = doc.Contains("childCount") ? doc["childCount"].AsInt32 : 0;

        var row = new Dictionary<string, object>
        {
            [groupCol.Field] = groupValue,
            ["ag-Grid-AutoColumn"] = groupValue
        };

        // Add aggregated values
        foreach (var valueCol in request.ValueCols)
        {
            var aggFieldName = $"{valueCol.Field}_{valueCol.AggFunc}";
            if (doc.Contains(aggFieldName))
            {
                row[valueCol.Field] = ConvertBsonValue(doc[aggFieldName]);
            }
        }

        // Add AG-Grid metadata for group rendering
        row["__agGridGroupData"] = new
        {
            isGroup = true,
            key = groupValue,
            field = groupCol.Field,
            childCount = childCount,
            level = level,
            expanded = false
        };

        return row;
    }

    private object ConvertBsonValue(BsonValue bsonValue)
    {
        return bsonValue.BsonType switch
        {
            BsonType.String => bsonValue.AsString,
            BsonType.Int32 => bsonValue.AsInt32,
            BsonType.Int64 => bsonValue.AsInt64,
            BsonType.Double => bsonValue.AsDouble,
            BsonType.Decimal128 => bsonValue.AsDecimal,
            BsonType.Boolean => bsonValue.AsBoolean,
            BsonType.DateTime => bsonValue.AsDateTime,
            BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
            BsonType.Null => null,
            _ => bsonValue.ToString()
        };
    }

    private BsonDocument BuildFilterStage(Dictionary<string, object> filterModel)
    {
        var matchDoc = new BsonDocument();
        
        foreach (var filter in filterModel)
        {
            // Implement filter logic based on your filter types
            // This is a simplified example
            matchDoc[filter.Key] = BsonValue.Create(filter.Value);
        }
        
        return matchDoc;
    }

    private BsonDocument BuildSortStage(List<SortModel> sortModel)
    {
        var sortDoc = new BsonDocument();
        
        foreach (var sort in sortModel)
        {
            sortDoc[sort.ColId] = sort.Sort == "asc" ? 1 : -1;
        }
        
        return sortDoc;
    }
}
Key Features Implemented
Multi-level Grouping: Country → Year → Sport hierarchy
Server-side Aggregation: Sum, avg, min, max, count operations
Lazy Loading: Only loads data when groups are expanded
Pagination: Handles large datasets efficiently
Filtering & Sorting: Server-side processing
Performance Optimized: Uses MongoDB aggregation pipeline
MongoDB Sample Data Structure
json
{
  "_id": ObjectId("..."),
  "athlete": "Michael Phelps",
  "age": 23,
  "country": "United States",
  "year": 2008,
  "date": "24/08/2008",
  "sport": "Swimming",
  "gold": 8,
  "silver": 0,
  "bronze": 0,
  "total": 8
}
This implementation creates a three-level hierarchy (Country > Year > Sport) with aggregated medal counts at each level, and shows individual athlete records at the leaf level.

show code for only SSRM ag-grid C# mongodb Angular ag-grid Row Grouping Multiple Group Columns without aggregrate

Here's a complete implementation for AG-Grid SSRM with multiple group columns without aggregation using C#, MongoDB, and Angular:

Angular Component
typescript
import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { 
  GridOptions, 
  IServerSideDatasource, 
  IServerSideGetRowsParams,
  ColDef
} from 'ag-grid-community';

@Component({
  selector: 'app-multi-group-grid',
  template: `
    <ag-grid-angular
      style="width: 100%; height: 600px;"
      class="ag-theme-alpine"
      [gridOptions]="gridOptions"
      [columnDefs]="columnDefs"
      (gridReady)="onGridReady($event)">
    </ag-grid-angular>
  `
})
export class MultiGroupGridComponent implements OnInit {
  
  public gridOptions: GridOptions = {};
  public columnDefs: ColDef[] = [];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.columnDefs = [
      // Group columns - these will create the hierarchy (no aggregation)
      { 
        field: 'country', 
        rowGroup: true, 
        hide: true,
        headerName: 'Country'
      },
      { 
        field: 'year', 
        rowGroup: true, 
        hide: true,
        headerName: 'Year'
      },
      { 
        field: 'sport', 
        rowGroup: true, 
        hide: true,
        headerName: 'Sport'
      },
      
      // Display columns (no aggregation functions)
      { field: 'athlete', headerName: 'Athlete' },
      { field: 'age', headerName: 'Age' },
      { field: 'gold', headerName: 'Gold' },
      { field: 'silver', headerName: 'Silver' },
      { field: 'bronze', headerName: 'Bronze' },
      { field: 'date', headerName: 'Date' }
    ];

    this.gridOptions = {
      rowModelType: 'serverSide',
      serverSideStoreType: 'partial',
      
      // Row grouping configuration
      rowGroupPanelShow: 'always',
      groupDefaultExpanded: 0, // Start collapsed
      groupSelectsChildren: true,
      groupSelectsFiltered: true,
      
      // Disable aggregation
      suppressAggFuncInHeader: true,
      
      // Auto group column configuration
      autoGroupColumnDef: {
        headerName: 'Group',
        field: 'ag-Grid-AutoColumn',
        cellRenderer: 'agGroupCellRenderer',
        cellRendererParams: {
          suppressCount: true, // Hide count in group headers
          checkbox: false
        },
        minWidth: 250
      },

      // Performance settings
      cacheBlockSize: 100,
      maxBlocksInCache: 10,
      purgeClosedRowNodes: true,
      maxConcurrentDatasourceRequests: 2,

      // Debug
      debug: false
    };
  }

  onGridReady(params: any) {
    const datasource: IServerSideDatasource = {
      getRows: (params: IServerSideGetRowsParams) => {
        console.log('Server request:', params.request);
        
        const request = {
          startRow: params.request.startRow,
          endRow: params.request.endRow,
          rowGroupCols: params.request.rowGroupCols || [],
          groupKeys: params.request.groupKeys || [],
          sortModel: params.request.sortModel || [],
          filterModel: params.request.filterModel || {}
        };

        this.http.post<any>('/api/olympics/grouped-data', request)
          .subscribe({
            next: (response) => {
              console.log('Server response:', response);
              params.success({
                rowData: response.rows,
                rowCount: response.rowCount
              });
            },
            error: (error) => {
              console.error('Error loading data:', error);
              params.fail();
            }
          });
      }
    };

    params.api.setServerSideDatasource(datasource);
  }
}
C# Backend Models
csharp
// Request models (no ValueCols needed since no aggregation)
public class ServerSideRequest
{
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    public List<ColumnVO> RowGroupCols { get; set; } = new();
    public List<string> GroupKeys { get; set; } = new();
    public List<SortModel> SortModel { get; set; } = new();
    public Dictionary<string, object> FilterModel { get; set; } = new();
}

public class ColumnVO
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public string Field { get; set; }
}

public class SortModel
{
    public string ColId { get; set; }
    public string Sort { get; set; } // 'asc' or 'desc'
}

// Response models
public class ServerSideResponse
{
    public List<object> Rows { get; set; } = new();
    public int RowCount { get; set; }
}

// MongoDB document model
public class OlympicResult
{
    public ObjectId Id { get; set; }
    public string Athlete { get; set; }
    public int Age { get; set; }
    public string Country { get; set; }
    public int Year { get; set; }
    public string Date { get; set; }
    public string Sport { get; set; }
    public int Gold { get; set; }
    public int Silver { get; set; }
    public int Bronze { get; set; }
    public int Total { get; set; }
}
C# Controller
csharp
[ApiController]
[Route("api/[controller]")]
public class OlympicsController : ControllerBase
{
    private readonly OlympicsService _olympicsService;

    public OlympicsController(OlympicsService olympicsService)
    {
        _olympicsService = olympicsService;
    }

    [HttpPost("grouped-data")]
    public async Task<ActionResult<ServerSideResponse>> GetGroupedData(
        [FromBody] ServerSideRequest request)
    {
        try
        {
            var response = await _olympicsService.GetGroupedData(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
C# Service with MongoDB (No Aggregation)
csharp
public class OlympicsService
{
    private readonly IMongoCollection<OlympicResult> _collection;

    public OlympicsService(IMongoDatabase database)
    {
        _collection = database.GetCollection<OlympicResult>("olympics");
    }

    public async Task<ServerSideResponse> GetGroupedData(ServerSideRequest request)
    {
        var currentLevel = request.GroupKeys.Count;
        var totalLevels = request.RowGroupCols.Count;
        var isLeafLevel = currentLevel >= totalLevels;

        List<BsonDocument> results;
        int totalCount;

        if (isLeafLevel)
        {
            // Get leaf data (actual records)
            results = await GetLeafData(request);
            totalCount = await GetLeafDataCount(request);
        }
        else
        {
            // Get group data (distinct values only)
            results = await GetGroupData(request, currentLevel);
            totalCount = results.Count; // For groups, count is the number of distinct groups
        }

        return new ServerSideResponse
        {
            Rows = TransformToGridFormat(results, request, isLeafLevel),
            RowCount = totalCount
        };
    }

    private async Task<List<BsonDocument>> GetGroupData(ServerSideRequest request, int level)
    {
        var pipeline = new List<BsonDocument>();

        // Add match stage for parent groups
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildParentGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Get distinct values for the current group level (no aggregation)
        var groupCol = request.RowGroupCols[level];
        var groupDoc = new BsonDocument("$group", new BsonDocument
        {
            { "_id", $"${groupCol.Field}" }
        });

        pipeline.Add(groupDoc);

        // Add sort
        var sortDoc = new BsonDocument("$sort", new BsonDocument("_id", 1));
        pipeline.Add(sortDoc);

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private async Task<List<BsonDocument>> GetLeafData(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        // Match exact group path
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Add sort
        if (request.SortModel.Count > 0)
        {
            var sortDoc = BuildSortStage(request.SortModel);
            pipeline.Add(new BsonDocument("$sort", sortDoc));
        }
        else
        {
            // Default sort by athlete name
            pipeline.Add(new BsonDocument("$sort", new BsonDocument("athlete", 1)));
        }

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private BsonDocument BuildParentGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[groupCol.Field] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private BsonDocument BuildExactGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[groupCol.Field] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private async Task<int> GetLeafDataCount(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        pipeline.Add(new BsonDocument("$count", "total"));

        var result = await _collection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        return result?["total"].AsInt32 ?? 0;
    }

    private List<object> TransformToGridFormat(
        List<BsonDocument> results, 
        ServerSideRequest request, 
        bool isLeafLevel)
    {
        var gridRows = new List<object>();

        foreach (var doc in results)
        {
            if (isLeafLevel)
            {
                // Transform leaf row (actual data)
                gridRows.Add(TransformLeafRow(doc));
            }
            else
            {
                // Transform group row (group headers only)
                var currentLevel = request.GroupKeys.Count;
                gridRows.Add(TransformGroupRow(doc, request, currentLevel));
            }
        }

        return gridRows;
    }

    private object TransformLeafRow(BsonDocument doc)
    {
        var row = new Dictionary<string, object>();

        foreach (var element in doc.Elements)
        {
            if (element.Name == "_id") continue;
            
            var value = ConvertBsonValue(element.Value);
            row[element.Name] = value;
        }

        return row;
    }

    private object TransformGroupRow(BsonDocument doc, ServerSideRequest request, int level)
    {
        var groupCol = request.RowGroupCols[level];
        var groupValue = doc["_id"].ToString();

        var row = new Dictionary<string, object>
        {
            [groupCol.Field] = groupValue,
            ["ag-Grid-AutoColumn"] = groupValue
        };

        // Add AG-Grid metadata for group rendering (no aggregation data)
        row["__agGridGroupData"] = new
        {
            isGroup = true,
            key = groupValue,
            field = groupCol.Field,
            level = level,
            expanded = false
        };

        return row;
    }

    private object ConvertBsonValue(BsonValue bsonValue)
    {
        return bsonValue.BsonType switch
        {
            BsonType.String => bsonValue.AsString,
            BsonType.Int32 => bsonValue.AsInt32,
            BsonType.Int64 => bsonValue.AsInt64,
            BsonType.Double => bsonValue.AsDouble,
            BsonType.Decimal128 => bsonValue.AsDecimal,
            BsonType.Boolean => bsonValue.AsBoolean,
            BsonType.DateTime => bsonValue.AsDateTime,
            BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
            BsonType.Null => null,
            _ => bsonValue.ToString()
        };
    }

    private BsonDocument BuildFilterStage(Dictionary<string, object> filterModel)
    {
        var matchDoc = new BsonDocument();
        
        foreach (var filter in filterModel)
        {
            // Implement filter logic based on your filter types
            // This is a simplified example for text filters
            if (filter.Value is string stringValue)
            {
                matchDoc[filter.Key] = new BsonRegularExpression(stringValue, "i"); // Case insensitive
            }
            else
            {
                matchDoc[filter.Key] = BsonValue.Create(filter.Value);
            }
        }
        
        return matchDoc;
    }

    private BsonDocument BuildSortStage(List<SortModel> sortModel)
    {
        var sortDoc = new BsonDocument();
        
        foreach (var sort in sortModel)
        {
            sortDoc[sort.ColId] = sort.Sort == "asc" ? 1 : -1;
        }
        
        return sortDoc;
    }
}
Key Differences from Aggregated Version
No Value Columns: Removed ValueCols from request model and processing
No Aggregation Functions: Column definitions don't include aggFunc
Simple Group Query: MongoDB aggregation only does $group by _id without sum/count/avg
No Aggregated Data: Group rows only contain the group value, no calculated totals
Simplified Transform: Group transformation doesn't include aggregated values
Cleaner Group Headers: suppressCount: true hides counts in group headers
Result Structure
Group Levels: Show distinct values for Country → Year → Sport
Leaf Level: Shows individual athlete records with all their data
No Totals: Group headers show only the group name (e.g., "United States", "2008", "Swimming")
Raw Data: Leaf nodes show the actual Olympic results without any calculations
This implementation provides a clean hierarchical view of your data organized by multiple group columns without any aggregation calculations, making it ideal for browsing and exploring data structure rather than analyzing totals.

what does client vs serverside ag-grid column grouping with no aggregate look like

Here's a clear comparison of client-side vs server-side AG-Grid column grouping with no aggregation:

Client-Side Grouping (No Aggregation)
Angular Implementation
typescript
@Component({
  selector: 'app-client-side-grouping',
  template: `
    <ag-grid-angular
      style="width: 100%; height: 600px;"
      class="ag-theme-alpine"
      [gridOptions]="gridOptions"
      [columnDefs]="columnDefs"
      [rowData]="rowData"
      (gridReady)="onGridReady($event)">
    </ag-grid-angular>
  `
})
export class ClientSideGroupingComponent implements OnInit {
  
  public gridOptions: GridOptions = {};
  public columnDefs: ColDef[] = [];
  public rowData: any[] = [];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.columnDefs = [
      // Group columns
      { 
        field: 'country', 
        rowGroup: true, 
        hide: true,
        headerName: 'Country'
      },
      { 
        field: 'year', 
        rowGroup: true, 
        hide: true,
        headerName: 'Year'
      },
      { 
        field: 'sport', 
        rowGroup: true, 
        hide: true,
        headerName: 'Sport'
      },
      
      // Display columns (no aggregation)
      { field: 'athlete', headerName: 'Athlete' },
      { field: 'age', headerName: 'Age' },
      { field: 'gold', headerName: 'Gold' },
      { field: 'silver', headerName: 'Silver' },
      { field: 'bronze', headerName: 'Bronze' }
    ];

    this.gridOptions = {
      // Client-side row model (default)
      rowModelType: 'clientSide',
      
      // Grouping configuration
      rowGroupPanelShow: 'always',
      groupDefaultExpanded: 0,
      groupSelectsChildren: true,
      
      // No aggregation
      suppressAggFuncInHeader: true,
      
      // Auto group column
      autoGroupColumnDef: {
        headerName: 'Group',
        field: 'ag-Grid-AutoColumn',
        cellRenderer: 'agGroupCellRenderer',
        cellRendererParams: {
          suppressCount: true, // Hide count
          checkbox: false
        },
        minWidth: 250
      }
    };
  }

  onGridReady(params: any) {
    // Load ALL data at once - AG-Grid handles grouping
    this.http.get<any[]>('/api/olympics/all-data')
      .subscribe(data => {
        this.rowData = data;
      });
  }
}
C# Backend for Client-Side
csharp
[ApiController]
[Route("api/[controller]")]
public class OlympicsController : ControllerBase
{
    private readonly OlympicsService _olympicsService;

    public OlympicsController(OlympicsService olympicsService)
    {
        _olympicsService = olympicsService;
    }

    [HttpGet("all-data")]
    public async Task<ActionResult<List<OlympicResult>>> GetAllData()
    {
        // Return ALL data - simple and flat
        var allData = await _olympicsService.GetAllOlympicResults();
        return Ok(allData);
    }
}

public class OlympicsService
{
    private readonly IMongoCollection<OlympicResult> _collection;

    public OlympicsService(IMongoDatabase database)
    {
        _collection = database.GetCollection<OlympicResult>("olympics");
    }

    public async Task<List<OlympicResult>> GetAllOlympicResults()
    {
        // Simple query - return everything
        return await _collection.Find(_ => true).ToListAsync();
    }
}
Server-Side Grouping (No Aggregation)
Angular Implementation
typescript
@Component({
  selector: 'app-server-side-grouping',
  template: `
    <ag-grid-angular
      style="width: 100%; height: 600px;"
      class="ag-theme-alpine"
      [gridOptions]="gridOptions"
      [columnDefs]="columnDefs"
      (gridReady)="onGridReady($event)">
    </ag-grid-angular>
  `
})
export class ServerSideGroupingComponent implements OnInit {
  
  public gridOptions: GridOptions = {};
  public columnDefs: ColDef[] = [];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.columnDefs = [
      // Same column definitions as client-side
      { field: 'country', rowGroup: true, hide: true, headerName: 'Country' },
      { field: 'year', rowGroup: true, hide: true, headerName: 'Year' },
      { field: 'sport', rowGroup: true, hide: true, headerName: 'Sport' },
      { field: 'athlete', headerName: 'Athlete' },
      { field: 'age', headerName: 'Age' },
      { field: 'gold', headerName: 'Gold' },
      { field: 'silver', headerName: 'Silver' },
      { field: 'bronze', headerName: 'Bronze' }
    ];

    this.gridOptions = {
      // Server-side row model
      rowModelType: 'serverSide',
      serverSideStoreType: 'partial',
      
      // Same grouping configuration
      rowGroupPanelShow: 'always',
      groupDefaultExpanded: 0,
      groupSelectsChildren: true,
      suppressAggFuncInHeader: true,
      
      autoGroupColumnDef: {
        headerName: 'Group',
        field: 'ag-Grid-AutoColumn',
        cellRenderer: 'agGroupCellRenderer',
        cellRendererParams: {
          suppressCount: true,
          checkbox: false
        },
        minWidth: 250
      },

      // Performance settings
      cacheBlockSize: 100,
      maxBlocksInCache: 10
    };
  }

  onGridReady(params: any) {
    const datasource: IServerSideDatasource = {
      getRows: (params: IServerSideGetRowsParams) => {
        // Server handles grouping logic
        const request = {
          startRow: params.request.startRow,
          endRow: params.request.endRow,
          rowGroupCols: params.request.rowGroupCols || [],
          groupKeys: params.request.groupKeys || []
        };

        this.http.post<any>('/api/olympics/grouped-data', request)
          .subscribe({
            next: (response) => {
              params.success({
                rowData: response.rows,
                rowCount: response.rowCount
              });
            },
            error: () => params.fail()
          });
      }
    };

    params.api.setServerSideDatasource(datasource);
  }
}
C# Backend for Server-Side
csharp
[HttpPost("grouped-data")]
public async Task<ActionResult<ServerSideResponse>> GetGroupedData(
    [FromBody] ServerSideRequest request)
{
    // Complex server-side grouping logic
    var response = await _olympicsService.GetGroupedData(request);
    return Ok(response);
}

// Full complex service implementation as shown in previous answer
Key Differences
Data Flow
Client-Side:

1. Server sends ALL data → Client
2. Client performs grouping in browser
3. User expands/collapses groups → Client handles instantly
Server-Side:

1. Client requests group level → Server
2. Server sends only current level data → Client  
3. User expands group → Client requests next level → Server
4. Server calculates and sends child data → Client
Network Traffic
Client-Side:

javascript
// Single large request
GET /api/olympics/all-data
Response: [
  {athlete: "Michael Phelps", country: "USA", year: 2008, sport: "Swimming", gold: 8},
  {athlete: "Usain Bolt", country: "Jamaica", year: 2008, sport: "Athletics", gold: 3},
  // ... ALL 50,000 records
]
Server-Side:

javascript
// Multiple small requests
POST /api/olympics/grouped-data
Request: {groupKeys: [], rowGroupCols: [{field: "country"}]}
Response: {rows: [{country: "USA"}, {country: "Jamaica"}, ...], rowCount: 195}

// When user expands "USA"
POST /api/olympics/grouped-data  
Request: {groupKeys: ["USA"], rowGroupCols: [{field: "country"}, {field: "year"}]}
Response: {rows: [{year: "2008"}, {year: "2012"}, ...], rowCount: 6}
Performance Characteristics
Aspect	Client-Side	Server-Side
Initial Load	Slow (all data)	Fast (group headers only)
Memory Usage	High (all data in browser)	Low (only visible data)
Group Expand/Collapse	Instant	Network request required
Filtering	Instant	Server request required
Sorting	Instant	Server request required
Data Size Limit	~10,000-50,000 rows	Unlimited
Server Complexity	Simple	Complex
User Experience
Client-Side Grouping:

✅ Lightning-fast group operations
✅ Immediate filtering/sorting
✅ Works offline after initial load
❌ Slow initial load for large datasets
❌ High memory consumption
Server-Side Grouping:

✅ Fast initial load
✅ Handles massive datasets
✅ Low memory footprint
❌ Network delay on group expand
❌ Requires server round-trip for operations
When to Use Each
Choose Client-Side When:

Dataset < 50,000 rows
Users need instant group operations
Simple server architecture preferred
Network connectivity is reliable
Users frequently expand/collapse groups
Choose Server-Side When:

Dataset > 50,000 rows
Memory constraints on client
Users typically view small subsets
Server has powerful database capabilities
Network bandwidth is limited
Both approaches provide the same visual grouping structure (Country → Year → Sport → Individual Records) but handle the data processing and user interactions very differently.

apply code to  mongo collection is a named CacheDocument { id, cacheKey, gridKey, parentId, fullPath, hashKey, documentType, dataList, data, createdAt, ExpireseAt, metadata}

data store expando type {id, region, pendingChangeType, approvalStatus, updatedBy, updatedOn, algo, controlCategory, productType, productSegment, desk}

Here's the complete implementation for AG-Grid SSRM with multiple group columns (no aggregation) applied to your CacheDocument collection structure:

Angular Component
typescript
import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { 
  GridOptions, 
  IServerSideDatasource, 
  IServerSideGetRowsParams,
  ColDef
} from 'ag-grid-community';

@Component({
  selector: 'app-cache-document-grid',
  template: `
    <ag-grid-angular
      style="width: 100%; height: 600px;"
      class="ag-theme-alpine"
      [gridOptions]="gridOptions"
      [columnDefs]="columnDefs"
      (gridReady)="onGridReady($event)">
    </ag-grid-angular>
  `
})
export class CacheDocumentGridComponent implements OnInit {
  
  public gridOptions: GridOptions = {};
  public columnDefs: ColDef[] = [];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.columnDefs = [
      // Group columns - create hierarchy
      { 
        field: 'region', 
        rowGroup: true, 
        hide: true,
        headerName: 'Region'
      },
      { 
        field: 'controlCategory', 
        rowGroup: true, 
        hide: true,
        headerName: 'Control Category'
      },
      { 
        field: 'productType', 
        rowGroup: true, 
        hide: true,
        headerName: 'Product Type'
      },
      { 
        field: 'desk', 
        rowGroup: true, 
        hide: true,
        headerName: 'Desk'
      },
      
      // Display columns (no aggregation)
      { field: 'id', headerName: 'ID', width: 100 },
      { field: 'pendingChangeType', headerName: 'Change Type' },
      { field: 'approvalStatus', headerName: 'Approval Status' },
      { field: 'updatedBy', headerName: 'Updated By' },
      { field: 'updatedOn', headerName: 'Updated On', cellRenderer: 'agDateStringCellRenderer' },
      { field: 'algo', headerName: 'Algorithm' },
      { field: 'productSegment', headerName: 'Product Segment' },
      { field: 'cacheKey', headerName: 'Cache Key' },
      { field: 'gridKey', headerName: 'Grid Key' }
    ];

    this.gridOptions = {
      rowModelType: 'serverSide',
      serverSideStoreType: 'partial',
      
      // Row grouping configuration
      rowGroupPanelShow: 'always',
      groupDefaultExpanded: 0,
      groupSelectsChildren: true,
      groupSelectsFiltered: true,
      
      // Disable aggregation
      suppressAggFuncInHeader: true,
      
      // Auto group column configuration
      autoGroupColumnDef: {
        headerName: 'Group',
        field: 'ag-Grid-AutoColumn',
        cellRenderer: 'agGroupCellRenderer',
        cellRendererParams: {
          suppressCount: true, // Hide count in group headers
          checkbox: false
        },
        minWidth: 300
      },

      // Performance settings
      cacheBlockSize: 100,
      maxBlocksInCache: 10,
      purgeClosedRowNodes: true,
      maxConcurrentDatasourceRequests: 2
    };
  }

  onGridReady(params: any) {
    const datasource: IServerSideDatasource = {
      getRows: (params: IServerSideGetRowsParams) => {
        console.log('Server request:', params.request);
        
        const request = {
          startRow: params.request.startRow,
          endRow: params.request.endRow,
          rowGroupCols: params.request.rowGroupCols || [],
          groupKeys: params.request.groupKeys || [],
          sortModel: params.request.sortModel || [],
          filterModel: params.request.filterModel || {}
        };

        this.http.post<any>('/api/cachedocuments/grouped-data', request)
          .subscribe({
            next: (response) => {
              console.log('Server response:', response);
              params.success({
                rowData: response.rows,
                rowCount: response.rowCount
              });
            },
            error: (error) => {
              console.error('Error loading data:', error);
              params.fail();
            }
          });
      }
    };

    params.api.setServerSideDatasource(datasource);
  }
}
C# Backend Models
csharp
// MongoDB document models
public class CacheDocument
{
    [BsonId]
    public ObjectId Id { get; set; }
    
    [BsonElement("cacheKey")]
    public string CacheKey { get; set; }
    
    [BsonElement("gridKey")]
    public string GridKey { get; set; }
    
    [BsonElement("parentId")]
    public string ParentId { get; set; }
    
    [BsonElement("fullPath")]
    public string FullPath { get; set; }
    
    [BsonElement("hashKey")]
    public string HashKey { get; set; }
    
    [BsonElement("documentType")]
    public string DocumentType { get; set; }
    
    [BsonElement("dataList")]
    public List<ExpandoData> DataList { get; set; }
    
    [BsonElement("data")]
    public ExpandoData Data { get; set; }
    
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
    
    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }
    
    [BsonElement("metadata")]
    public BsonDocument Metadata { get; set; }
}

public class ExpandoData
{
    [BsonElement("id")]
    public string Id { get; set; }
    
    [BsonElement("region")]
    public string Region { get; set; }
    
    [BsonElement("pendingChangeType")]
    public string PendingChangeType { get; set; }
    
    [BsonElement("approvalStatus")]
    public string ApprovalStatus { get; set; }
    
    [BsonElement("updatedBy")]
    public string UpdatedBy { get; set; }
    
    [BsonElement("updatedOn")]
    public DateTime UpdatedOn { get; set; }
    
    [BsonElement("algo")]
    public string Algo { get; set; }
    
    [BsonElement("controlCategory")]
    public string ControlCategory { get; set; }
    
    [BsonElement("productType")]
    public string ProductType { get; set; }
    
    [BsonElement("productSegment")]
    public string ProductSegment { get; set; }
    
    [BsonElement("desk")]
    public string Desk { get; set; }
}

// Request/Response models
public class ServerSideRequest
{
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    public List<ColumnVO> RowGroupCols { get; set; } = new();
    public List<string> GroupKeys { get; set; } = new();
    public List<SortModel> SortModel { get; set; } = new();
    public Dictionary<string, object> FilterModel { get; set; } = new();
}

public class ColumnVO
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public string Field { get; set; }
}

public class SortModel
{
    public string ColId { get; set; }
    public string Sort { get; set; }
}

public class ServerSideResponse
{
    public List<object> Rows { get; set; } = new();
    public int RowCount { get; set; }
}
C# Controller
csharp
[ApiController]
[Route("api/[controller]")]
public class CacheDocumentsController : ControllerBase
{
    private readonly CacheDocumentService _cacheDocumentService;

    public CacheDocumentsController(CacheDocumentService cacheDocumentService)
    {
        _cacheDocumentService = cacheDocumentService;
    }

    [HttpPost("grouped-data")]
    public async Task<ActionResult<ServerSideResponse>> GetGroupedData(
        [FromBody] ServerSideRequest request)
    {
        try
        {
            var response = await _cacheDocumentService.GetGroupedData(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
C# Service with MongoDB Aggregation
csharp
public class CacheDocumentService
{
    private readonly IMongoCollection<CacheDocument> _collection;

    public CacheDocumentService(IMongoDatabase database)
    {
        _collection = database.GetCollection<CacheDocument>("CacheDocument");
    }

    public async Task<ServerSideResponse> GetGroupedData(ServerSideRequest request)
    {
        var currentLevel = request.GroupKeys.Count;
        var totalLevels = request.RowGroupCols.Count;
        var isLeafLevel = currentLevel >= totalLevels;

        List<BsonDocument> results;
        int totalCount;

        if (isLeafLevel)
        {
            // Get leaf data from flattened dataList/data
            results = await GetLeafData(request);
            totalCount = await GetLeafDataCount(request);
        }
        else
        {
            // Get group data
            results = await GetGroupData(request, currentLevel);
            totalCount = results.Count;
        }

        return new ServerSideResponse
        {
            Rows = TransformToGridFormat(results, request, isLeafLevel),
            RowCount = totalCount
        };
    }

    private async Task<List<BsonDocument>> GetGroupData(ServerSideRequest request, int level)
    {
        var pipeline = new List<BsonDocument>();

        // Unwind the dataList to work with individual data records
        pipeline.Add(new BsonDocument("$unwind", new BsonDocument
        {
            { "path", "$dataList" },
            { "preserveNullAndEmptyArrays", false }
        }));

        // Also handle documents where data is stored in 'data' field instead of 'dataList'
        pipeline.Add(new BsonDocument("$addFields", new BsonDocument("flatData", 
            new BsonDocument("$cond", new BsonDocument
            {
                { "if", new BsonDocument("$ne", new BsonArray { "$dataList", null }) },
                { "then", "$dataList" },
                { "else", "$data" }
            })
        )));

        // Add match stage for parent groups
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildParentGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Group by current level field
        var groupCol = request.RowGroupCols[level];
        var groupDoc = new BsonDocument("$group", new BsonDocument
        {
            { "_id", $"$flatData.{groupCol.Field}" }
        });

        pipeline.Add(groupDoc);

        // Add sort
        pipeline.Add(new BsonDocument("$sort", new BsonDocument("_id", 1)));

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private async Task<List<BsonDocument>> GetLeafData(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        // Unwind dataList to get individual records
        pipeline.Add(new BsonDocument("$unwind", new BsonDocument
        {
            { "path", "$dataList" },
            { "preserveNullAndEmptyArrays", false }
        }));

        // Handle both dataList and data fields
        pipeline.Add(new BsonDocument("$addFields", new BsonDocument("flatData", 
            new BsonDocument("$cond", new BsonDocument
            {
                { "if", new BsonDocument("$ne", new BsonArray { "$dataList", null }) },
                { "then", "$dataList" },
                { "else", "$data" }
            })
        )));

        // Project the flattened data along with cache document fields
        pipeline.Add(new BsonDocument("$project", new BsonDocument
        {
            { "cacheKey", 1 },
            { "gridKey", 1 },
            { "parentId", 1 },
            { "fullPath", 1 },
            { "hashKey", 1 },
            { "documentType", 1 },
            { "createdAt", 1 },
            { "expiresAt", 1 },
            { "id", "$flatData.id" },
            { "region", "$flatData.region" },
            { "pendingChangeType", "$flatData.pendingChangeType" },
            { "approvalStatus", "$flatData.approvalStatus" },
            { "updatedBy", "$flatData.updatedBy" },
            { "updatedOn", "$flatData.updatedOn" },
            { "algo", "$flatData.algo" },
            { "controlCategory", "$flatData.controlCategory" },
            { "productType", "$flatData.productType" },
            { "productSegment", "$flatData.productSegment" },
            { "desk", "$flatData.desk" }
        }));

        // Match exact group path
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Add sort
        if (request.SortModel.Count > 0)
        {
            var sortDoc = BuildSortStage(request.SortModel);
            pipeline.Add(new BsonDocument("$sort", sortDoc));
        }
        else
        {
            // Default sort by updatedOn descending
            pipeline.Add(new BsonDocument("$sort", new BsonDocument("updatedOn", -1)));
        }

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private BsonDocument BuildParentGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[$"flatData.{groupCol.Field}"] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private BsonDocument BuildExactGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[groupCol.Field] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private async Task<int> GetLeafDataCount(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        // Unwind and flatten
        pipeline.Add(new BsonDocument("$unwind", new BsonDocument
        {
            { "path", "$dataList" },
            { "preserveNullAndEmptyArrays", false }
        }));

        pipeline.Add(new BsonDocument("$addFields", new BsonDocument("flatData", 
            new BsonDocument("$cond", new BsonDocument
            {
                { "if", new BsonDocument("$ne", new BsonArray { "$dataList", null }) },
                { "then", "$dataList" },
                { "else", "$data" }
            })
        )));

        // Project flattened fields
        pipeline.Add(new BsonDocument("$project", new BsonDocument
        {
            { "region", "$flatData.region" },
            { "controlCategory", "$flatData.controlCategory" },
            { "productType", "$flatData.productType" },
            { "desk", "$flatData.desk" },
            { "id", "$flatData.id" },
            { "pendingChangeType", "$flatData.pendingChangeType" },
            { "approvalStatus", "$flatData.approvalStatus" },
            { "updatedBy", "$flatData.updatedBy" },
            { "updatedOn", "$flatData.updatedOn" },
            { "algo", "$flatData.algo" },
            { "productSegment", "$flatData.productSegment" }
        }));

        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        pipeline.Add(new BsonDocument("$count", "total"));

        var result = await _collection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        return result?["total"].AsInt32 ?? 0;
    }

    private List<object> TransformToGridFormat(
        List<BsonDocument> results, 
        ServerSideRequest request, 
        bool isLeafLevel)
    {
        var gridRows = new List<object>();

        foreach (var doc in results)
        {
            if (isLeafLevel)
            {
                gridRows.Add(TransformLeafRow(doc));
            }
            else
            {
                var currentLevel = request.GroupKeys.Count;
                gridRows.Add(TransformGroupRow(doc, request, currentLevel));
            }
        }

        return gridRows;
    }

    private object TransformLeafRow(BsonDocument doc)
    {
        var row = new Dictionary<string, object>();

        foreach (var element in doc.Elements)
        {
            if (element.Name == "_id") continue;
            
            var value = ConvertBsonValue(element.Value);
            row[element.Name] = value;
        }

        return row;
    }

    private object TransformGroupRow(BsonDocument doc, ServerSideRequest request, int level)
    {
        var groupCol = request.RowGroupCols[level];
        var groupValue = doc["_id"]?.ToString() ?? "Unknown";

        var row = new Dictionary<string, object>
        {
            [groupCol.Field] = groupValue,
            ["ag-Grid-AutoColumn"] = groupValue
        };

        // Add AG-Grid metadata for group rendering
        row["__agGridGroupData"] = new
        {
            isGroup = true,
            key = groupValue,
            field = groupCol.Field,
            level = level,
            expanded = false
        };

        return row;
    }

    private object ConvertBsonValue(BsonValue bsonValue)
    {
        return bsonValue.BsonType switch
        {
            BsonType.String => bsonValue.AsString,
            BsonType.Int32 => bsonValue.AsInt32,
            BsonType.Int64 => bsonValue.AsInt64,
            BsonType.Double => bsonValue.AsDouble,
            BsonType.Decimal128 => bsonValue.AsDecimal,
            BsonType.Boolean => bsonValue.AsBoolean,
            BsonType.DateTime => bsonValue.AsDateTime,
            BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
            BsonType.Null => null,
            _ => bsonValue.ToString()
        };
    }

    private BsonDocument BuildFilterStage(Dictionary<string, object> filterModel)
    {
        var matchDoc = new BsonDocument();
        
        foreach (var filter in filterModel)
        {
            if (filter.Value is string stringValue && !string.IsNullOrEmpty(stringValue))
            {
                matchDoc[filter.Key] = new BsonRegularExpression(stringValue, "i");
            }
            else if (filter.Value != null)
            {
                matchDoc[filter.Key] = BsonValue.Create(filter.Value);
            }
        }
        
        return matchDoc;
    }

    private BsonDocument BuildSortStage(List<SortModel> sortModel)
    {
        var sortDoc = new BsonDocument();
        
        foreach (var sort in sortModel)
        {
            sortDoc[sort.ColId] = sort.Sort == "asc" ? 1 : -1;
        }
        
        return sortDoc;
    }
}
Key Features for CacheDocument Structure
Data Flattening: Uses $unwind to flatten dataList array and handles both dataList and data fields
Group Hierarchy: Region → Control Category → Product Type → Desk
Field Projection: Projects both cache document fields and flattened data fields
Performance: Efficient aggregation pipeline with proper indexing
Flexibility: Handles both single data objects and arrays of data
Suggested MongoDB Indexes
javascript
// Create indexes for better performance
db.CacheDocument.createIndex({"dataList.region": 1})
db.CacheDocument.createIndex({"dataList.controlCategory": 1})
db.CacheDocument.createIndex({"dataList.productType": 1})
db.CacheDocument.createIndex({"dataList.desk": 1})
db.CacheDocument.createIndex({"dataList.updatedOn": -1})

// Compound indexes for group hierarchy
db.CacheDocument.createIndex({
  "dataList.region": 1,
  "dataList.controlCategory": 1,
  "dataList.productType": 1,
  "dataList.desk": 1
})
This implementation efficiently handles your CacheDocument structure with nested data and provides a clean hierarchical view without aggregation calculations.

getting rows from "data" not "datalist"

Here's the updated implementation to get rows from the "data" field instead of "dataList":

Updated C# Service
csharp
public class CacheDocumentService
{
    private readonly IMongoCollection<CacheDocument> _collection;

    public CacheDocumentService(IMongoDatabase database)
    {
        _collection = database.GetCollection<CacheDocument>("CacheDocument");
    }

    public async Task<ServerSideResponse> GetGroupedData(ServerSideRequest request)
    {
        var currentLevel = request.GroupKeys.Count;
        var totalLevels = request.RowGroupCols.Count;
        var isLeafLevel = currentLevel >= totalLevels;

        List<BsonDocument> results;
        int totalCount;

        if (isLeafLevel)
        {
            // Get leaf data from "data" field
            results = await GetLeafData(request);
            totalCount = await GetLeafDataCount(request);
        }
        else
        {
            // Get group data from "data" field
            results = await GetGroupData(request, currentLevel);
            totalCount = results.Count;
        }

        return new ServerSideResponse
        {
            Rows = TransformToGridFormat(results, request, isLeafLevel),
            RowCount = totalCount
        };
    }

    private async Task<List<BsonDocument>> GetGroupData(ServerSideRequest request, int level)
    {
        var pipeline = new List<BsonDocument>();

        // Filter out documents where data is null
        pipeline.Add(new BsonDocument("$match", new BsonDocument("data", new BsonDocument("$ne", null))));

        // Add match stage for parent groups
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildParentGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Group by current level field from "data"
        var groupCol = request.RowGroupCols[level];
        var groupDoc = new BsonDocument("$group", new BsonDocument
        {
            { "_id", $"$data.{groupCol.Field}" }
        });

        pipeline.Add(groupDoc);

        // Add sort
        pipeline.Add(new BsonDocument("$sort", new BsonDocument("_id", 1)));

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private async Task<List<BsonDocument>> GetLeafData(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        // Filter out documents where data is null
        pipeline.Add(new BsonDocument("$match", new BsonDocument("data", new BsonDocument("$ne", null))));

        // Project the data fields along with cache document fields
        pipeline.Add(new BsonDocument("$project", new BsonDocument
        {
            // Cache document fields
            { "cacheKey", 1 },
            { "gridKey", 1 },
            { "parentId", 1 },
            { "fullPath", 1 },
            { "hashKey", 1 },
            { "documentType", 1 },
            { "createdAt", 1 },
            { "expiresAt", 1 },
            { "metadata", 1 },
            
            // Data fields (flattened from "data" object)
            { "id", "$data.id" },
            { "region", "$data.region" },
            { "pendingChangeType", "$data.pendingChangeType" },
            { "approvalStatus", "$data.approvalStatus" },
            { "updatedBy", "$data.updatedBy" },
            { "updatedOn", "$data.updatedOn" },
            { "algo", "$data.algo" },
            { "controlCategory", "$data.controlCategory" },
            { "productType", "$data.productType" },
            { "productSegment", "$data.productSegment" },
            { "desk", "$data.desk" }
        }));

        // Match exact group path
        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        // Add filter stage
        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        // Add sort
        if (request.SortModel.Count > 0)
        {
            var sortDoc = BuildSortStage(request.SortModel);
            pipeline.Add(new BsonDocument("$sort", sortDoc));
        }
        else
        {
            // Default sort by updatedOn descending
            pipeline.Add(new BsonDocument("$sort", new BsonDocument("updatedOn", -1)));
        }

        // Add pagination
        if (request.StartRow > 0)
        {
            pipeline.Add(new BsonDocument("$skip", request.StartRow));
        }

        var pageSize = request.EndRow - request.StartRow;
        if (pageSize > 0)
        {
            pipeline.Add(new BsonDocument("$limit", pageSize));
        }

        return await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
    }

    private BsonDocument BuildParentGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            matchDoc[$"data.{groupCol.Field}"] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private BsonDocument BuildExactGroupMatch(ServerSideRequest request)
    {
        var matchDoc = new BsonDocument();
        
        for (int i = 0; i < request.GroupKeys.Count; i++)
        {
            var groupCol = request.RowGroupCols[i];
            // After projection, the fields are flattened to root level
            matchDoc[groupCol.Field] = request.GroupKeys[i];
        }
        
        return matchDoc;
    }

    private async Task<int> GetLeafDataCount(ServerSideRequest request)
    {
        var pipeline = new List<BsonDocument>();

        // Filter out documents where data is null
        pipeline.Add(new BsonDocument("$match", new BsonDocument("data", new BsonDocument("$ne", null))));

        // Project flattened fields from "data"
        pipeline.Add(new BsonDocument("$project", new BsonDocument
        {
            { "region", "$data.region" },
            { "controlCategory", "$data.controlCategory" },
            { "productType", "$data.productType" },
            { "desk", "$data.desk" },
            { "id", "$data.id" },
            { "pendingChangeType", "$data.pendingChangeType" },
            { "approvalStatus", "$data.approvalStatus" },
            { "updatedBy", "$data.updatedBy" },
            { "updatedOn", "$data.updatedOn" },
            { "algo", "$data.algo" },
            { "productSegment", "$data.productSegment" }
        }));

        if (request.GroupKeys.Count > 0)
        {
            var matchDoc = BuildExactGroupMatch(request);
            pipeline.Add(new BsonDocument("$match", matchDoc));
        }

        if (request.FilterModel.Count > 0)
        {
            var filterDoc = BuildFilterStage(request.FilterModel);
            pipeline.Add(new BsonDocument("$match", filterDoc));
        }

        pipeline.Add(new BsonDocument("$count", "total"));

        var result = await _collection.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        return result?["total"].AsInt32 ?? 0;
    }

    private List<object> TransformToGridFormat(
        List<BsonDocument> results, 
        ServerSideRequest request, 
        bool isLeafLevel)
    {
        var gridRows = new List<object>();

        foreach (var doc in results)
        {
            if (isLeafLevel)
            {
                gridRows.Add(TransformLeafRow(doc));
            }
            else
            {
                var currentLevel = request.GroupKeys.Count;
                gridRows.Add(TransformGroupRow(doc, request, currentLevel));
            }
        }

        return gridRows;
    }

    private object TransformLeafRow(BsonDocument doc)
    {
        var row = new Dictionary<string, object>();

        foreach (var element in doc.Elements)
        {
            if (element.Name == "_id") continue;
            
            var value = ConvertBsonValue(element.Value);
            row[element.Name] = value;
        }

        return row;
    }

    private object TransformGroupRow(BsonDocument doc, ServerSideRequest request, int level)
    {
        var groupCol = request.RowGroupCols[level];
        var groupValue = doc["_id"]?.ToString() ?? "Unknown";

        var row = new Dictionary<string, object>
        {
            [groupCol.Field] = groupValue,
            ["ag-Grid-AutoColumn"] = groupValue
        };

        // Add AG-Grid metadata for group rendering
        row["__agGridGroupData"] = new
        {
            isGroup = true,
            key = groupValue,
            field = groupCol.Field,
            level = level,
            expanded = false
        };

        return row;
    }

    private object ConvertBsonValue(BsonValue bsonValue)
    {
        return bsonValue.BsonType switch
        {
            BsonType.String => bsonValue.AsString,
            BsonType.Int32 => bsonValue.AsInt32,
            BsonType.Int64 => bsonValue.AsInt64,
            BsonType.Double => bsonValue.AsDouble,
            BsonType.Decimal128 => bsonValue.AsDecimal,
            BsonType.Boolean => bsonValue.AsBoolean,
            BsonType.DateTime => bsonValue.AsDateTime,
            BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
            BsonType.Null => null,
            _ => bsonValue.ToString()
        };
    }

    private BsonDocument BuildFilterStage(Dictionary<string, object> filterModel)
    {
        var matchDoc = new BsonDocument();
        
        foreach (var filter in filterModel)
        {
            if (filter.Value is string stringValue && !string.IsNullOrEmpty(stringValue))
            {
                matchDoc[filter.Key] = new BsonRegularExpression(stringValue, "i");
            }
            else if (filter.Value != null)
            {
                matchDoc[filter.Key] = BsonValue.Create(filter.Value);
            }
        }
        
        return matchDoc;
    }

    private BsonDocument BuildSortStage(List<SortModel> sortModel)
    {
        var sortDoc = new BsonDocument();
        
        foreach (var sort in sortModel)
        {
            sortDoc[sort.ColId] = sort.Sort == "asc" ? 1 : -1;
        }
        
        return sortDoc;
    }
}
Updated MongoDB Indexes
javascript
// Create indexes for better performance on "data" field
db.CacheDocument.createIndex({"data.region": 1})
db.CacheDocument.createIndex({"data.controlCategory": 1})
db.CacheDocument.createIndex({"data.productType": 1})
db.CacheDocument.createIndex({"data.desk": 1})
db.CacheDocument.createIndex({"data.updatedOn": -1})
db.CacheDocument.createIndex({"data": 1}) // For null checks

// Compound indexes for group hierarchy
db.CacheDocument.createIndex({
  "data.region": 1,
  "data.controlCategory": 1,
  "data.productType": 1,
  "data.desk": 1
})

// Index for filtering out null data
db.CacheDocument.createIndex({"data": 1})
Key Changes Made
Removed $unwind: No longer needed since we're working with single "data" objects, not arrays
Direct field access: Changed from $dataList.field to $data.field throughout
Null filtering: Added $match stage to filter out documents where data is null
Simplified pipeline: Removed the complex conditional logic for choosing between dataList and data
Field projection: Direct projection from $data.fieldName to fieldName
Updated group matching: Uses $data.fieldName for parent group matching
Sample Document Structure Expected
json
{
  "_id": ObjectId("..."),
  "cacheKey": "cache_key_123",
  "gridKey": "grid_key_456",
  "parentId": "parent_789",
  "fullPath": "/path/to/document",
  "hashKey": "hash_abc",
  "documentType": "trading_data",
  "data": {
    "id": "trade_001",
    "region": "EMEA",
    "pendingChangeType": "UPDATE",
    "approvalStatus": "PENDING",
    "updatedBy": "john.doe",
    "updatedOn": ISODate("2025-01-15T10:30:00Z"),
    "algo": "TWAP",
    "controlCategory": "RISK",
    "productType": "EQUITY",
    "productSegment": "CASH",
    "desk": "EQUITY_TRADING"
  },
  "createdAt": ISODate("2025-01-15T09:00:00Z"),
  "expiresAt": ISODate("2025-01-16T09:00:00Z"),
  "metadata": {}
}
This implementation now correctly works with the "data" field as a single object rather than an array, providing efficient grouping and filtering for your cache document structure.






