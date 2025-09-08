/* cache-document-grid.component.scss */
.cache-document-grid-container {
  padding: 20px;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.grid-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  padding-bottom: 15px;
  border-bottom: 1px solid #e0e0e0;

  h2 {
    margin: 0;
    color: #333;
    font-weight: 600;
  }

  .grid-actions {
    display: flex;
    gap: 10px;

    .btn {
      padding: 8px 16px;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
      display: flex;
      align-items: center;
      gap: 6px;
      transition: all 0.2s ease;

      &:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }

      &.btn-primary {
        background-color: #007bff;
        color: white;

        &:hover:not(:disabled) {
          background-color: #0056b3;
        }
      }

      &.btn-secondary {
        background-color: #6c757d;
        color: white;

        &:hover:not(:disabled) {
          background-color: #545b62;
        }
      }

      i {
        font-size: 12px;
      }
    }
  }
}

.grid-status {
  margin-bottom: 15px;

  .loading-message,
  .error-message {
    padding: 12px 16px;
    border-radius: 4px;
    display: flex;
    align-items: center;
    gap: 8px;
    font-weight: 500;
  }

  .loading-message {
    background-color: #e3f2fd;
    color: #1976d2;
    border: 1px solid #bbdefb;
  }

  .error-message {
    background-color: #ffebee;
    color: #d32f2f;
    border: 1px solid #ffcdd2;
  }
}

.grid-wrapper {
  flex: 1;
  position: relative;

  &.loading {
    opacity: 0.7;
    pointer-events: none;
  }

  /* Ag-Grid Theme Customizations */
  .ag-theme-alpine {
    --ag-header-background-color: #f8f9fa;
    --ag-header-foreground-color: #495057;
    --ag-odd-row-background-color: #f8f9fa;
    --ag-row-hover-color: #e9ecef;
    --ag-selected-row-background-color: #007bff20;
    --ag-range-selection-background-color: #007bff10;
    
    .ag-header-cell-label {
      font-weight: 600;
    }

    .ag-cell {
      display: flex;
      align-items: center;
    }

    .ag-cell-wrapper.ag-row-group {
      align-items: center;
    }
  }
}

.grid-footer {
  margin-top: 15px;
  padding-top: 15px;
  border-top: 1px solid #e0e0e0;
  color: #666;
  font-size: 14px;

  .record-count {
    font-weight: 500;
  }
}

/* Responsive Design */
@media (max-width: 768px) {
  .cache-document-grid-container {
    padding: 10px;
  }

  .grid-header {
    flex-direction: column;
    gap: 15px;
    align-items: stretch;

    .grid-actions {
      justify-content: center;
      flex-wrap: wrap;
    }
  }

  .grid-wrapper .ag-theme-alpine {
    font-size: 12px;
  }
}

// app.module.ts - Module Configuration
import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientModule } from '@angular/common/http';
import { AgGridModule } from 'ag-grid-angular';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { CacheDocumentGridComponent } from './cache-document-grid/cache-document-grid.component';

@NgModule({
  declarations: [
    AppComponent,
    CacheDocumentGridComponent
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    HttpClientModule,
    AgGridModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }

// environment.ts - Environment Configuration
export const environment = {
  production: false,
  apiUrl: 'https://localhost:7000' // Update with your C# API URL
};

// Startup.cs or Program.cs additions for C# API
/*
// Add to ConfigureServices (Startup.cs) or builder.Services (Program.cs)
services.Configure<MongoDbSettings>(
    Configuration.GetSection("MongoDbSettings"));

services.AddSingleton<ICacheDocumentService, CacheDocumentService>();

services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        builder =>
        {
            builder.WithOrigins("http://localhost:4200")
                   .AllowAnyHeader()
                   .AllowAnyMethod();
        });
});

// Add to Configure (Startup.cs) or app pipeline (Program.cs)
app.UseCors("AllowAngular");
*/

// appsettings.json configuration
/*
{
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "YourAppDatabase"
  }
}
*/
