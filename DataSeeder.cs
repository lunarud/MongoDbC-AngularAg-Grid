// DataSeeder.cs - Sample Data for Testing
using MongoDB.Bson;
using YourApp.Models;
using YourApp.Services;

namespace YourApp.Data
{
    public class DataSeeder
    {
        private readonly ICacheDocumentService _service;

        public DataSeeder(ICacheDocumentService service)
        {
            _service = service;
        }

        public async Task SeedSampleDataAsync()
        {
            // Check if data already exists
            var existing = await _service.GetAllAsync();
            if (existing.Any())
            {
                Console.WriteLine("Sample data already exists. Skipping seeding.");
                return;
            }

            var sampleDocuments = new List<CacheDocument>
            {
                new CacheDocument
                {
                    Data = new BsonDocument
                    {
                        { "name", "John Doe" },
                        { "age", 30 },
                        { "email", "john.doe@example.com" },
                        { "isActive", true },
                        { "department", "Engineering" },
                        { "salary", 75000.50 },
                        { "startDate", DateTime.Parse("2020-01-15") }
                    }
                },
                new CacheDocument
                {
                    Data = new BsonDocument
                    {
                        { "name", "Jane Smith" },
                        { "age", 28 },
                        { "email", "jane.smith@example.com" },
                        { "isActive", true },
                        { "department", "Marketing" },
                        { "salary", 65000.75 },
                        { "startDate", DateTime.Parse("2021-03-10") },
                        { "skills", new BsonArray { "Social Media", "Analytics", "Content Creation" } }
                    }
                },
                new CacheDocument
                {
                    Data = new BsonDocument
                    {
                        { "name", "Bob Johnson" },
                        { "age", 45 },
                        { "email", "bob.johnson@example.com" },
                        { "isActive", false },
                        { "department", "Sales" },
                        { "salary", 85000.00 },
                        { "startDate", DateTime.Parse("2018-07-20") },
                        { "region", "West Coast" },
                        { "quotaAchieved", true }
                    }
                },
                new CacheDocument
                {
                    Data = new BsonDocument
                    {
                        { "productName", "Widget Pro" },
                        { "price", 299.99 },
                        { "inStock", true },
                        { "category", "Electronics" },
                        { "rating", 4.5 },
                        { "reviews", 1250 },
                        { "features", new BsonArray { "Wireless", "Waterproof", "Long Battery" } }
                    }
                },
                new CacheDocument
                {
                    Data = new BsonDocument
                    {
                        { "orderNumber", "ORD-2024-001" },
                        { "customerName", "Alice Cooper" },
                        { "orderTotal", 1299.99 },
                        { "orderDate", DateTime.Parse("2024-01-15") },
                        { "status", "Shipped" },
                        { "items", new BsonDocument
                            {
                                { "laptop", 1 },
                                { "mouse", 2 },
                                { "keyboard", 1 }
                            }
                        },
                        { "shippingAddress", new BsonDocument
                            {
                                { "street", "123 Main St" },
                                { "city", "Anytown" },
                                { "state", "CA" },
                                { "zipCode", "12345" }
                            }
                        }
                    }
                }
            };

            foreach (var document in sampleDocuments)
            {
                await _service.CreateAsync(document);
            }

            Console.WriteLine($"Seeded {sampleDocuments.Count} sample documents successfully!");
        }
    }
}

// Program.cs modifications for seeding
/*
// Add to your Program.cs after services configuration and before app.Run()

// Seed sample data in development
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var seeder = new DataSeeder(scope.ServiceProvider.GetRequiredService<ICacheDocumentService>());
        await seeder.SeedSampleDataAsync();
    }
}
*/

// Package.json dependencies for Angular project
/*
{
  "dependencies": {
    "@angular/animations": "^16.0.0",
    "@angular/common": "^16.0.0",
    "@angular/compiler": "^16.0.0",
    "@angular/core": "^16.0.0",
    "@angular/forms": "^16.0.0",
    "@angular/platform-browser": "^16.0.0",
    "@angular/platform-browser-dynamic": "^16.0.0",
    "@angular/router": "^16.0.0",
    "ag-grid-angular": "^30.0.0",
    "ag-grid-community": "^30.0.0",
    "rxjs": "~7.8.0",
    "tslib": "^2.3.0",
    "zone.js": "~0.13.0"
  },
  "devDependencies": {
    "@angular-devkit/build-angular": "^16.0.0",
    "@angular/cli": "~16.0.0",
    "@angular/compiler-cli": "^16.0.0",
    "@types/jasmine": "~4.3.0",
    "@types/node": "^18.7.0",
    "jasmine-core": "~4.6.0",
    "karma": "~6.4.0",
    "karma-chrome-launcher": "~3.2.0",
    "karma-coverage": "~2.2.0",
    "karma-jasmine": "~5.1.0",
    "karma-jasmine-html-reporter": "~2.1.0",
    "typescript": "~5.0.2"
  }
}
*/

// C# Project file additions (.csproj)
/*
<PackageReference Include="MongoDB.Driver" Version="2.21.0" />
<PackageReference Include="Microsoft.AspNetCore.Cors" Version="2.2.0" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
*/

// Additional MongoDB indexes for better performance
/*
// In your MongoDB shell or through MongoDB Compass:

// Create indexes for better query performance
db.CacheDocument.createIndex({ "createdAt": -1 })
db.CacheDocument.createIndex({ "updatedAt": -1 })
db.CacheDocument.createIndex({ "data.name": 1 })  // If you commonly query by name
db.CacheDocument.createIndex({ "data.department": 1 })  // If you commonly query by department

// Create a compound index for common queries
db.CacheDocument.createIndex({ 
  "data.department": 1, 
  "data.isActive": 1, 
  "createdAt": -1 
})
*/
