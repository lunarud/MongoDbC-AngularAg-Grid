// CacheDocument.cs - MongoDB Document Model
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json;

namespace YourApp.Models
{
    public class CacheDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("data")]
        public BsonDocument Data { get; set; } = new BsonDocument();

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

// ICacheDocumentService.cs - Service Interface
using YourApp.Models;

namespace YourApp.Services
{
    public interface ICacheDocumentService
    {
        Task<List<CacheDocument>> GetAllAsync();
        Task<CacheDocument?> GetByIdAsync(string id);
        Task<CacheDocument> CreateAsync(CacheDocument document);
        Task UpdateAsync(string id, CacheDocument document);
        Task DeleteAsync(string id);
        Task<List<Dictionary<string, object>>> GetDataAsRowsAsync();
    }
}

// CacheDocumentService.cs - MongoDB Service Implementation
using MongoDB.Driver;
using MongoDB.Bson;
using YourApp.Models;
using Microsoft.Extensions.Options;

namespace YourApp.Services
{
    public class CacheDocumentService : ICacheDocumentService
    {
        private readonly IMongoCollection<CacheDocument> _collection;

        public CacheDocumentService(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var database = client.GetDatabase(settings.Value.DatabaseName);
            _collection = database.GetCollection<CacheDocument>("CacheDocument");
        }

        public async Task<List<CacheDocument>> GetAllAsync()
        {
            return await _collection.Find(_ => true).ToListAsync();
        }

        public async Task<CacheDocument?> GetByIdAsync(string id)
        {
            return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<CacheDocument> CreateAsync(CacheDocument document)
        {
            document.CreatedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            await _collection.InsertOneAsync(document);
            return document;
        }

        public async Task UpdateAsync(string id, CacheDocument document)
        {
            document.UpdatedAt = DateTime.UtcNow;
            await _collection.ReplaceOneAsync(x => x.Id == id, document);
        }

        public async Task DeleteAsync(string id)
        {
            await _collection.DeleteOneAsync(x => x.Id == id);
        }

        public async Task<List<Dictionary<string, object>>> GetDataAsRowsAsync()
        {
            var documents = await GetAllAsync();
            var rows = new List<Dictionary<string, object>>();

            foreach (var doc in documents)
            {
                var row = new Dictionary<string, object>();
                
                // Add document ID
                row["_id"] = doc.Id ?? "";
                row["createdAt"] = doc.CreatedAt;
                row["updatedAt"] = doc.UpdatedAt;

                // Convert BsonDocument to Dictionary for easier serialization
                foreach (var element in doc.Data.Elements)
                {
                    row[element.Name] = ConvertBsonValue(element.Value);
                }

                rows.Add(row);
            }

            return rows;
        }

        private static object ConvertBsonValue(BsonValue bsonValue)
        {
            return bsonValue.BsonType switch
            {
                BsonType.String => bsonValue.AsString,
                BsonType.Int32 => bsonValue.AsInt32,
                BsonType.Int64 => bsonValue.AsInt64,
                BsonType.Double => bsonValue.AsDouble,
                BsonType.Boolean => bsonValue.AsBoolean,
                BsonType.DateTime => bsonValue.AsDateTime,
                BsonType.Null => null!,
                BsonType.Array => bsonValue.AsBsonArray.Select(ConvertBsonValue).ToArray(),
                BsonType.Document => bsonValue.AsBsonDocument.ToDictionary(
                    e => e.Name, 
                    e => ConvertBsonValue(e.Value)
                ),
                _ => bsonValue.ToString()
            };
        }
    }

    // MongoDbSettings.cs - Configuration
    public class MongoDbSettings
    {
        public string ConnectionString { get; set; } = "";
        public string DatabaseName { get; set; } = "";
    }
}

// CacheDocumentController.cs - Web API Controller
using Microsoft.AspNetCore.Mvc;
using YourApp.Services;
using YourApp.Models;

namespace YourApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CacheDocumentController : ControllerBase
    {
        private readonly ICacheDocumentService _service;

        public CacheDocumentController(ICacheDocumentService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<List<CacheDocument>>> Get()
        {
            var documents = await _service.GetAllAsync();
            return Ok(documents);
        }

        [HttpGet("rows")]
        public async Task<ActionResult<List<Dictionary<string, object>>>> GetRows()
        {
            var rows = await _service.GetDataAsRowsAsync();
            return Ok(rows);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<CacheDocument>> Get(string id)
        {
            var document = await _service.GetByIdAsync(id);
            if (document == null)
                return NotFound();
            
            return Ok(document);
        }

        [HttpPost]
        public async Task<ActionResult<CacheDocument>> Post([FromBody] CacheDocument document)
        {
            var created = await _service.CreateAsync(document);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(string id, [FromBody] CacheDocument document)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing == null)
                return NotFound();

            document.Id = id;
            await _service.UpdateAsync(id, document);
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing == null)
                return NotFound();

            await _service.DeleteAsync(id);
            return NoContent();
        }
    }
}
