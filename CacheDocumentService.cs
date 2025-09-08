// cache-document.service.ts - Angular Service
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface CacheDocument {
  _id?: string;
  data: any;
  createdAt?: Date;
  updatedAt?: Date;
}

@Injectable({
  providedIn: 'root'
})
export class CacheDocumentService {
  private readonly apiUrl = `${environment.apiUrl}/api/cachedocument`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<CacheDocument[]> {
    return this.http.get<CacheDocument[]>(this.apiUrl);
  }

  getRows(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/rows`);
  }

  getById(id: string): Observable<CacheDocument> {
    return this.http.get<CacheDocument>(`${this.apiUrl}/${id}`);
  }

  create(document: CacheDocument): Observable<CacheDocument> {
    return this.http.post<CacheDocument>(this.apiUrl, document);
  }

  update(id: string, document: CacheDocument): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, document);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}

// cache-document-grid.component.ts - Angular Component
import { Component, OnInit, ViewChild } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridOptions, GridReadyEvent, ICellRendererParams } from 'ag-grid-community';
import { CacheDocumentService } from './cache-document.service';

@Component({
  selector: 'app-cache-document-grid',
  templateUrl: './cache-document-grid.component.html',
  styleUrls: ['./cache-document-grid.component.scss']
})
export class CacheDocumentGridComponent implements OnInit {
  @ViewChild('agGrid') agGrid!: AgGridAngular;

  public columnDefs: ColDef[] = [];
  public rowData: any[] = [];
  public defaultColDef: ColDef = {
    sortable: true,
    filter: true,
    resizable: true,
    floatingFilter: true
  };

  public gridOptions: GridOptions = {
    pagination: true,
    paginationPageSize: 50,
    animateRows: true,
    enableRangeSelection: true,
    suppressMenuHide: true,
    onGridReady: this.onGridReady.bind(this)
  };

  public loading = false;
  public error: string | null = null;

  constructor(private cacheDocumentService: CacheDocumentService) {}

  ngOnInit(): void {
    this.loadData();
  }

  onGridReady(params: GridReadyEvent): void {
    params.api.sizeColumnsToFit();
  }

  loadData(): void {
    this.loading = true;
    this.error = null;

    this.cacheDocumentService.getRows().subscribe({
      next: (data) => {
        this.rowData = data;
        this.generateColumnDefs(data);
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading cache documents:', error);
        this.error = 'Failed to load data. Please try again.';
        this.loading = false;
      }
    });
  }

  private generateColumnDefs(data: any[]): void {
    if (!data || data.length === 0) {
      this.columnDefs = [];
      return;
    }

    // Get all unique keys from all rows to handle varying schemas
    const allKeys = new Set<string>();
    data.forEach(row => {
      Object.keys(row).forEach(key => allKeys.add(key));
    });

    // Sort keys to have system fields first, then alphabetical
    const systemFields = ['_id', 'createdAt', 'updatedAt'];
    const sortedKeys = Array.from(allKeys).sort((a, b) => {
      const aIsSystem = systemFields.includes(a);
      const bIsSystem = systemFields.includes(b);
      
      if (aIsSystem && !bIsSystem) return -1;
      if (!aIsSystem && bIsSystem) return 1;
      if (aIsSystem && bIsSystem) return systemFields.indexOf(a) - systemFields.indexOf(b);
      
      return a.localeCompare(b);
    });

    this.columnDefs = sortedKeys.map(key => {
      const colDef: ColDef = {
        headerName: this.formatHeaderName(key),
        field: key,
        sortable: true,
        filter: true,
        resizable: true
      };

      // Special handling for system fields
      switch (key) {
        case '_id':
          colDef.headerName = 'ID';
          colDef.width = 120;
          colDef.pinned = 'left';
          break;
        case 'createdAt':
        case 'updatedAt':
          colDef.headerName = key === 'createdAt' ? 'Created' : 'Updated';
          colDef.width = 160;
          colDef.valueFormatter = this.dateFormatter;
          colDef.filter = 'agDateColumnFilter';
          break;
        default:
          // Dynamic width based on content type
          const sampleValue = data.find(row => row[key] !== null && row[key] !== undefined)?.[key];
          if (typeof sampleValue === 'boolean') {
            colDef.width = 100;
            colDef.cellRenderer = this.booleanRenderer;
          } else if (typeof sampleValue === 'number') {
            colDef.width = 120;
            colDef.filter = 'agNumberColumnFilter';
            colDef.type = 'numericColumn';
          } else if (typeof sampleValue === 'object') {
            colDef.width = 200;
            colDef.cellRenderer = this.objectRenderer;
            colDef.filter = false; // Disable filtering for complex objects
          } else {
            colDef.width = 150;
          }
      }

      return colDef;
    });
  }

  private formatHeaderName(key: string): string {
    return key
      .replace(/([A-Z])/g, ' $1') // Add space before capital letters
      .replace(/^./, str => str.toUpperCase()) // Capitalize first letter
      .trim();
  }

  private dateFormatter(params: any): string {
    if (!params.value) return '';
    const date = new Date(params.value);
    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
  }

  private booleanRenderer(params: ICellRendererParams): string {
    if (params.value === null || params.value === undefined) return '';
    return params.value ? '✓' : '✗';
  }

  private objectRenderer(params: ICellRendererParams): string {
    if (params.value === null || params.value === undefined) return '';
    if (typeof params.value === 'object') {
      return JSON.stringify(params.value);
    }
    return String(params.value);
  }

  refreshData(): void {
    this.loadData();
  }

  exportToCsv(): void {
    this.agGrid.api.exportDataAsCsv({
      fileName: `cache-documents-${new Date().toISOString().split('T')[0]}.csv`
    });
  }

  onSelectionChanged(): void {
    const selectedRows = this.agGrid.api.getSelectedRows();
    console.log('Selected rows:', selectedRows);
  }

  autoSizeColumns(): void {
    this.agGrid.columnApi.autoSizeAllColumns();
  }

  resetColumnSizes(): void {
    this.agGrid.api.sizeColumnsToFit();
  }
}
