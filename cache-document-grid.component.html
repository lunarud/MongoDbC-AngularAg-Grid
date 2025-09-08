<!-- cache-document-grid.component.html -->
<div class="cache-document-grid-container">
  <div class="grid-header">
    <h2>Cache Documents</h2>
    <div class="grid-actions">
      <button 
        class="btn btn-primary" 
        (click)="refreshData()" 
        [disabled]="loading">
        <i class="fas fa-sync" [class.fa-spin]="loading"></i>
        Refresh
      </button>
      <button 
        class="btn btn-secondary" 
        (click)="exportToCsv()"
        [disabled]="loading || rowData.length === 0">
        <i class="fas fa-download"></i>
        Export CSV
      </button>
      <button 
        class="btn btn-secondary" 
        (click)="autoSizeColumns()"
        [disabled]="loading">
        <i class="fas fa-arrows-alt-h"></i>
        Auto Size
      </button>
      <button 
        class="btn btn-secondary" 
        (click)="resetColumnSizes()"
        [disabled]="loading">
        <i class="fas fa-compress-arrows-alt"></i>
        Fit Columns
      </button>
    </div>
  </div>

  <div class="grid-status" *ngIf="loading || error">
    <div *ngIf="loading" class="loading-message">
      <i class="fas fa-spinner fa-spin"></i>
      Loading cache documents...
    </div>
    <div *ngIf="error" class="error-message">
      <i class="fas fa-exclamation-triangle"></i>
      {{ error }}
    </div>
  </div>

  <div class="grid-wrapper" [class.loading]="loading">
    <ag-grid-angular
      #agGrid
      class="ag-theme-alpine"
      [columnDefs]="columnDefs"
      [rowData]="rowData"
      [defaultColDef]="defaultColDef"
      [gridOptions]="gridOptions"
      [rowSelection]="'multiple'"
      (selectionChanged)="onSelectionChanged()"
      style="width: 100%; height: 600px;">
    </ag-grid-angular>
  </div>

  <div class="grid-footer" *ngIf="!loading && rowData.length > 0">
    <span class="record-count">
      Showing {{ rowData.length }} record{{ rowData.length !== 1 ? 's' : '' }}
    </span>
  </div>
</div>
