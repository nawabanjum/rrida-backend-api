using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using Pgvector;
using SharpToken;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
namespace CloverBackoffice.Services.Implementation
{
    public class DocumentEmbeddingService : IDocumentEmbeddingService
    {
        private readonly IDocumentEmbeddingRepository _repository;
        private readonly IDocumentEmbeddingAuditLogRepository _auditLogRepository;
        private readonly IDocumentEmbeddingGeneratorService _embeddingGenerator;
        private readonly IDocumentParserService _parserService;

        private readonly SearchClient _searchClient;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly int _rpm;
        private readonly int _tpm;
        private readonly TimeSpan _window;
        private readonly Queue<DateTime> _requestLog = new();
        private readonly Queue<(DateTime, int)> _tokenLog = new();
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        private const int ChunkSize = 1000;
        private const int ChunkOverlap = 200;

        private ModelConfiguration? _cachedEmbeddingModel;
        private ModelConfiguration? _cachedSearchModel;

        public DocumentEmbeddingService(
            IDocumentEmbeddingRepository repository,
            IDocumentEmbeddingAuditLogRepository auditLogRepository,
            IDocumentEmbeddingGeneratorService embeddingGenerator,
            IDocumentParserService parserService,
            SearchClient searchClient,
            IHttpClientFactory httpFactory,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            IWebHostEnvironment env)
        {
            _repository = repository;
            _auditLogRepository = auditLogRepository;
            _embeddingGenerator = embeddingGenerator;
            _parserService = parserService;
            _searchClient = searchClient;
            _httpFactory = httpFactory;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _env = env;
        }

        private async Task<ModelConfiguration> GetEmbeddingModelAsync()
        {
            _cachedEmbeddingModel ??= await _repository.GetDefaultModelAsync(modelConfigTypeId: 3)
                ?? throw new InvalidOperationException("No default Embedding-model found in DB.");
            return _cachedEmbeddingModel;
        }

        private async Task<ModelConfiguration> GetSearchModelAsync()
        {
            _cachedSearchModel ??= await _repository.GetDefaultModelAsync(modelConfigTypeId: 1)
                ?? throw new InvalidOperationException("No default Search-model found in DB.");
            return _cachedSearchModel;
        }

        public async Task<DocumentEmbeddingResponse> StoreAsync(StoreDocumentEmbeddingRequest request)
        {
            var embeddings = await _embeddingGenerator.GenerateEmbeddingsAsync(new List<string> { request.Content });
            var embeddingVector = embeddings.First();

            var processId = await _repository.GetNextProcessIdAsync();

            var entity = new DocumentEmbedding
            {
                Title = request.Title,
                Content = request.Content,
                Embedding = new Vector(embeddingVector),
                SourceType = request.SourceType,
                ProcessId = processId,
                CreatedAt = DateTime.UtcNow
            };

            var saved = await _repository.AddAsync(entity);

            await _auditLogRepository.AddAsync(new DocumentEmbeddingAuditLog
            {
                Action = "Store",
                ProcessId = saved.ProcessId,
                SourceType = saved.SourceType,
                EmbeddingType = saved.EmbeddingType,
                TotalChunks = 1,
                Details = $"Stored text embedding: {saved.Title}",
                CreatedAt = DateTime.UtcNow
            });

            return new DocumentEmbeddingResponse
            {
                Id = saved.Id,
                Title = saved.Title,
                Content = saved.Content,
                SourceType = saved.SourceType,
                EmbeddingType = saved.EmbeddingType,
                ProcessId = saved.ProcessId,
                CreatedAt = saved.CreatedAt
            };
        }

        public async Task<List<DocumentEmbeddingResponse>> SearchAsync(SearchDocumentEmbeddingRequest request)
        {
            var embeddings = await _embeddingGenerator.GenerateEmbeddingsAsync(new List<string> { request.Query });
            var queryVector = new Vector(embeddings.First());

            var results = await _repository.SearchSimilarAsync(queryVector, request.TopK);

            return results.Select(r => new DocumentEmbeddingResponse
            {
                Id = r.Document.Id,
                Title = r.Document.Title,
                Content = r.Document.Content,
                SourceType = r.Document.SourceType,
                EmbeddingType = r.Document.EmbeddingType,
                ProcessId = r.Document.ProcessId,
                Score = 1 - r.Distance,
                CreatedAt = r.Document.CreatedAt
            }).ToList();
        }

        public async Task<DocumentEmbeddingResponse?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
                return null;

            return new DocumentEmbeddingResponse
            {
                Id = entity.Id,
                Title = entity.Title,
                Content = entity.Content,
                SourceType = entity.SourceType,
                EmbeddingType = entity.EmbeddingType,
                ProcessId = entity.ProcessId,
                CreatedAt = entity.CreatedAt
            };
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            var result = await _repository.DeleteAsync(id);

            if (result && entity != null)
            {
                await _auditLogRepository.AddAsync(new DocumentEmbeddingAuditLog
                {
                    Action = "Delete",
                    ProcessId = entity.ProcessId,
                    FileName = entity.FileName,
                    SourceType = entity.SourceType,
                    EmbeddingType = entity.EmbeddingType,
                    TotalChunks = 1,
                    Details = $"Deleted single embedding Id: {id}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            return result;
        }

        public async Task<UploadDocumentResponse> UploadFileAsync(IFormFile file, string? embeddingType)
        {
            var processId = await _repository.GetNextProcessIdAsync();
            using var stream = file.OpenReadStream();
            var result = await ProcessStreamAsync(stream, file.FileName, processId, embeddingType);

            await _auditLogRepository.AddAsync(new DocumentEmbeddingAuditLog
            {
                Action = "Upload",
                ProcessId = result.ProcessId,
                FileName = result.FileName,
                SourceType = result.SourceType,
                EmbeddingType = result.EmbeddingType,
                TotalChunks = result.TotalChunks,
                Details = $"Uploaded file: {result.FileName}, created {result.TotalChunks} chunks",
                CreatedAt = DateTime.UtcNow
            });

            return result;
        }

        public async Task<UploadDocumentResponse> UpdateFileByProcessIdAsync(IFormFile file, int processId, string? embeddingType)
        {
            // Capture old info before deleting
            var oldEntities = await _repository.GetByProcessIdAsync(processId);
            var oldFileName = oldEntities.FirstOrDefault()?.FileName;
            var oldChunkCount = oldEntities.Count;

            // 1. Remove all existing chunks for this ProcessId
            await _repository.DeleteByProcessIdAsync(processId);

            // 2. Process and store the new file with the same ProcessId
            using var stream = file.OpenReadStream();
            var result = await ProcessStreamAsync(stream, file.FileName, processId, embeddingType);

            await _auditLogRepository.AddAsync(new DocumentEmbeddingAuditLog
            {
                Action = "Update",
                ProcessId = processId,
                FileName = result.FileName,
                SourceType = result.SourceType,
                EmbeddingType = result.EmbeddingType,
                TotalChunks = result.TotalChunks,
                Details = $"Updated ProcessId: {processId}. Old file: {oldFileName} ({oldChunkCount} chunks) replaced with: {result.FileName} ({result.TotalChunks} chunks)",
                CreatedAt = DateTime.UtcNow
            });

            return result;
        }

        public async Task<DocumentEmbeddingByProcessResponse?> GetByProcessIdAsync(int processId)
        {
            var entities = await _repository.GetByProcessIdAsync(processId);
            if (!entities.Any())
                return null;

            var first = entities.First();
            return new DocumentEmbeddingByProcessResponse
            {
                ProcessId = processId,
                FileName = first.FileName,
                SourceType = first.SourceType,
                EmbeddingType = first.EmbeddingType,
                TotalChunks = entities.Count,
                CreatedAt = first.CreatedAt,
                Chunks = entities.Select(e => new DocumentEmbeddingChunkResponse
                {
                    Id = e.Id,
                    Title = e.Title,
                    Content = e.Content,
                    ChunkIndex = e.ChunkIndex,
                    CreatedAt = e.CreatedAt
                }).ToList()
            };
        }

        public async Task<List<DocumentEmbeddingListResponse>> GetAllAsync()
        {
            var entities = await _repository.GetAllAsync();
            return entities
                .GroupBy(e => e.ProcessId)
                .Select(g =>
                {
                    var first = g.First();
                    return new DocumentEmbeddingListResponse
                    {
                        ProcessId = g.Key,
                        FileName = first.FileName,
                        SourceType = first.SourceType,
                        EmbeddingType = first.EmbeddingType,
                        TotalChunks = g.Count(),
                        CreatedAt = first.CreatedAt
                    };
                })
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }

        public async Task<bool> DeleteByProcessIdAsync(int processId)
        {
            var entities = await _repository.GetByProcessIdAsync(processId);
            var result = await _repository.DeleteByProcessIdAsync(processId);

            if (result && entities.Any())
            {
                var first = entities.First();
                await _auditLogRepository.AddAsync(new DocumentEmbeddingAuditLog
                {
                    Action = "Delete",
                    ProcessId = processId,
                    FileName = first.FileName,
                    SourceType = first.SourceType,
                    EmbeddingType = first.EmbeddingType,
                    TotalChunks = entities.Count,
                    Details = $"Deleted all {entities.Count} chunks for ProcessId: {processId}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            return result;
        }

        private async Task<UploadDocumentResponse> ProcessStreamAsync(Stream stream, string fileName, int processId, string? embeddingType)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var sourceType = extension switch
            {
                ".pdf" => "pdf",
                ".docx" => "word",
                ".xlsx" => "excel",
                _ => "unknown"
            };

            // 1. Extract text from file stream
            var rawText = await _parserService.ExtractTextAsync(stream, fileName);

            if (string.IsNullOrWhiteSpace(rawText))
                throw new InvalidOperationException("No text could be extracted from the uploaded file.");

            // 2. Chunk the text
            var chunks = ChunkText(rawText, ChunkSize, ChunkOverlap);

            // 3. Generate embeddings for all chunks
            var embeddings = await _embeddingGenerator.GenerateEmbeddingsAsync(chunks);

            // 4. Create entities
            var entities = new List<DocumentEmbedding>();
            for (int i = 0; i < chunks.Count; i++)
            {
                entities.Add(new DocumentEmbedding
                {
                    Title = $"{Path.GetFileNameWithoutExtension(fileName)} - Chunk {i + 1}",
                    Content = chunks[i],
                    Embedding = new Vector(embeddings[i]),
                    SourceType = sourceType,
                    EmbeddingType = embeddingType,
                    ProcessId = processId,
                    FileName = fileName,
                    ChunkIndex = i,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 5. Bulk save
            var saved = await _repository.AddBulkAsync(entities);

            return new UploadDocumentResponse
            {
                FileName = fileName,
                SourceType = sourceType,
                EmbeddingType = embeddingType,
                ProcessId = processId,
                TotalChunks = saved.Count,
                ChunkIds = saved.Select(e => e.Id).ToList()
            };
        }

        public async Task<BulkUploadDocumentResponse> UploadZipFileAsync(IFormFile zipFile)
        {
            var response = new BulkUploadDocumentResponse
            {
                FileResults = new List<UploadDocumentResponse>(),
                SkippedFiles = new List<string>()
            };

            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".pdf", ".docx", ".xlsx"
            };

            using var stream = zipFile.OpenReadStream();
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                // Skip directories and empty entries
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var ext = Path.GetExtension(entry.FullName);
                if (!allowedExtensions.Contains(ext))
                {
                    response.SkippedFiles.Add(entry.FullName);
                    continue;
                }

                // Skip hidden files like __MACOSX
                if (entry.FullName.Contains("__MACOSX") || entry.Name.StartsWith("."))
                {
                    continue;
                }

                try
                {
                    using var entryStream = entry.Open();
                    // Copy to memory stream because some parsers/readers need seek capability or full stream access
                    // and ZipArchiveEntry stream is non-seekable.
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    var processId = await _repository.GetNextProcessIdAsync();
                    var result = await ProcessStreamAsync(memoryStream, entry.FullName, processId, null);
                    response.FileResults.Add(result);
                }
                catch (Exception ex)
                {
                    response.SkippedFiles.Add($"{entry.FullName} — {ex.Message}");
                }
            }

            response.TotalFilesProcessed = response.FileResults.Count;
            response.TotalChunksGenerated = response.FileResults.Sum(r => r.TotalChunks);
            return response;
        }

        private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
        private static List<string> ChunkText(string text, int chunkSize, int overlap)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text))
                return chunks;

            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(chunkSize, text.Length - start);
                var chunk = text.Substring(start, length).Trim();

                if (!string.IsNullOrWhiteSpace(chunk))
                    chunks.Add(chunk);

                start += chunkSize - overlap;
            }

            return chunks;
        }

        // below code realated to embedding
        public async Task<string> AnalyzeFeedbackPromptDataEmbedding(string startPoint, IFormFile file, string endpoint)
        {
            // 1. Determine Model Configuration
            // We treat 'endpoint' parameter as an optional modelName override.
            // If provided, we try to fetch that specific model config.
            // If null/empty, we fetch the default model config.

            ModelConfiguration? modelConfig = null;

            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                modelConfig = await _repository.GetModelByNameAsync(endpoint);
            }

            if (modelConfig == null)
            {
                modelConfig = await _repository.GetDefaultModelAsync(modelConfigTypeId: 1);
            }

            if (modelConfig == null)
            {
                throw new InvalidOperationException("No valid ModelConfiguration found (neither by name nor default).");
            }

            // DEBUG: Log which model was selected so we can diagnose deployment issues
            System.Diagnostics.Debug.WriteLine($"[DocumentEmbedding] Selected model Id={modelConfig.Id}, Name={modelConfig.ModelName}, Endpoint={modelConfig.Endpoint}");
            Console.WriteLine($"[DocumentEmbedding] Selected model Id={modelConfig.Id}, Name={modelConfig.ModelName}, Endpoint={modelConfig.Endpoint}");

            // 1. Read feedback from Excel
            var importList = await ImportPromptDataAsyncEmbedding(file);
            if (!importList.Any())
            {
                return null;
            }

            // 2. Map ImportPromptDataDto -> DailyFeedbackReportRowDto
            var feedbackList = importList.Select(obj =>
            {
                var dto = new DailyFeedbackReportRowDto
                {
                    QuestionText = obj.Question,
                    Answer = obj.Answer,
                    ReasonText = obj.Reason
                };

                // map CloverCategory enum if valid
                if (Enum.TryParse<CloverCategory>(obj.CloverCategory, out var category))
                {
                    dto.SpecificationCloverCategoryName = category.ToString();
                }
                else
                {
                    dto.SpecificationCloverCategoryName = obj.CloverCategory;
                }

                //Sentiment Type 
                if (Enum.TryParse<DailyFeedbackSentimentType>(obj.Sentiment, out var sentiment))
                {
                    dto.SentimentTypeName = sentiment.ToString();
                }


                return dto;
            }).ToList();

            // 3. Build GPT/RAG prompt
            var prompt = await BuildPromptExelDataEmbedding(startPoint, feedbackList, modelConfig.Endpoint);

            // 4. Get response from Azure RAG
            var rawResponse = await AnswerAsyncEmbedding(prompt, modelConfig);
            return rawResponse;
        }

        public async Task<List<ImportPromptDataDto>> ImportPromptDataAsyncEmbedding(IFormFile file)
        {
            // Required for EPPlus
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            var feedbacks = new List<ImportPromptDataDto>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets[0];
            int rowCount = worksheet.Dimension.Rows;

            for (int row = 2; row <= rowCount; row++)
            {
                var dto = new ImportPromptDataDto
                {
                    CloverCategory = worksheet.Cells[row, 1].Text?.Trim(),
                    Question = worksheet.Cells[row, 2].Text?.Trim(),
                    Sentiment = worksheet.Cells[row, 3].Text?.Trim(),
                    Answer = worksheet.Cells[row, 4].Text?.Trim(),
                    Reason = worksheet.Cells[row, 5].Text?.Trim(),

                };

                // Only add if at least one column is non-empty
                if (!string.IsNullOrWhiteSpace(dto.CloverCategory) ||
                    !string.IsNullOrWhiteSpace(dto.Question) ||
                    !string.IsNullOrWhiteSpace(dto.Sentiment) ||
                    !string.IsNullOrWhiteSpace(dto.Answer) ||
                    !string.IsNullOrWhiteSpace(dto.Reason))
                {
                    feedbacks.Add(dto);
                }
            }

            return feedbacks;
        }

        private async Task<string> BuildPromptExelDataEmbedding(string startPoint, List<DailyFeedbackReportRowDto> feedbackRows, string endpoint)
        {
            var sb = new StringBuilder();

            // Add static prompt
            sb.AppendLine(startPoint);

            // Add extra context from API parameters
            sb.AppendLine($"StartPoint Info: {startPoint}");
            sb.AppendLine($"Endpoint Info: {endpoint}");
            sb.AppendLine();

            // Add dynamic feedback rows from Excel
            foreach (var f in feedbackRows)
            {
                sb.AppendLine(
                    $"CloverCategory: {f.SpecificationCloverCategoryName}, " +
                    $"Question: {f.QuestionText}, " +
                    $"Sentiment: {f.SentimentType}, " +
                    $"Answer: {f.Answer}," +
                    $"Reason: {f.ReasonText}"
                );
            }

            sb.AppendLine();
            sb.AppendLine(endpoint);

            return sb.ToString();
        }

        private async Task<string> AnswerAsyncEmbedding(string query, ModelConfiguration modelConfig, int topK = 5, CancellationToken ct = default)
        {
            var qVec = await GetEmbeddingAsync(query, ct);
            var docs = await VectorSearchAsync(qVec, topK, ct);

            // Split docs into batches (3 docs per request)
            int batchSize = 3;
            var batches = docs.Select((d, i) => new { d, i })
                              .GroupBy(x => x.i / batchSize)
                              .Select(g => g.Select(x => x.d).ToList())
                              .ToList();

            var partialAnswers = new List<string>();

            foreach (var batch in batches)
            {
                var sb = new StringBuilder();

                sb.AppendLine(@"
                 You are an assistant that MUST return ONLY valid JSON.
                 You are analyzing employee engagement data.
                 Use the documents below, the Azure input and output templates, and the schema definition.

                 Your output must be a valid JSON object following this structure:

                 {
                   ""TopThemes"": [""string""],
                   ""TopRecommendations"": [""string""],
                   ""TopActions"": [""string""],
                   ""AzureOutput"": { ... }  // Follows Azure_optimized_constrained_OUTPUT_v4 format
                 }

                 No explanations, no markdown, ONLY JSON.

                 ### AZURE_optimized_constrained_INPUT_v4 ###
                 {
                   ""VERSION"": ""AZURE_3.0_ULTRA_CONSTRAINED"",
                   ""TASK"": ""Analyze employee engagement data. Output 1 priority element, maximum 2 themes, and EXACTLY 2 actions."",
                   ""DATA_ANALYSIS"": {
                     ""input_file"": ""xlsx with columns: Clover Category, QUESTION, Answer, Reason"",
                     ""sentiment_scoring"": { ""positive"": ""60-100"", ""neutral"": ""40-59"", ""needs_work"": ""0-39"" },
                     ""priority_element"": ""The CLOVER element with the lowest average sentiment score"",
                     ""themes"": ""Maximum 2 themes with sentiment scores under 40%"",
                     ""quotes"": ""2 anonymized quotes per theme, 6-18 words each""
                   },
                   ""OUTPUT_EXAMPLE"": {
                     ""priority_element"": {""name"": ""Reflection"", ""sentiment_score"": 41.0},
                     ""themes_requiring_attention"": [
                       {""theme_label"": ""Days lack meaning"", ""sentiment_score"": 25.0},
                       {""theme_label"": ""Distraction overwhelm"", ""sentiment_score"": 28.0}
                     ],
                     ""action_plan"": {
                       ""action_1_worksheet"": {
                         ""title"": ""Complete Reflection 1: The Reflection Hour"",
                         ""worksheet_identifier"": ""Reflection 1"",
                         ""worksheet_verification"": {
                           ""alternatives_considered"": [
                             {""identifier"": ""Reflection 9"", ""actual_title"": ""..."", ""score"": 8.5, ""why_not_selected"": ""...""},
                             {""identifier"": ""Reflection 6"", ""actual_title"": ""..."", ""score"": 7.0, ""why_not_selected"": ""...""}
                           ]
                         }
                       },
                       ""action_2_micro"": {
                         ""title"": ""End-of-Day Done List"",
                         ""duration_minutes"": 5
                       }
                     }
                   }
                 }

         ### AZURE_optimized_constrained_OUTPUT_v4 ###
         {
           ""priority_element"": { ""name"": ""string"", ""sentiment_score"": ""number"", ""confidence"": ""number"" },
           ""why_this_matters"": {
             ""primary_reason"": ""string"",
             ""supporting_evidence"": [""string"", ""string"", ""string""],
             ""impact_if_ignored"": ""string""
           },
           ""themes_requiring_attention"": [
             {
               ""theme_label"": ""string"",
               ""supporting_responses"": ""integer"",
               ""sentiment_score"": ""number"",
               ""related_elements"": [""string""],
               ""example_quotes"": [""string"", ""string""]
             }
           ],
           ""action_plan"": {
             ""action_1_worksheet"": { ""title"": ""Complete [Element] [Number]: [Title]"", ""duration_minutes"": 30 },
             ""action_2_micro"": { ""title"": ""string"", ""duration_minutes"": ""integer"" }
           }
         }

         ### DOCUMENTS ###
         ");

                foreach (var d in batch)
                {
                    var content = d.Content.Length > 3000 ? d.Content.Substring(0, 3000) + "..." : d.Content;
                    sb.AppendLine($"Doc ID: {d.Id}");
                    sb.AppendLine(content);
                }

                sb.AppendLine($"\nQuestion: {query}");
                sb.AppendLine("\nReturn ONLY JSON following the schema shown above.");

                var partial = await CallChatCompletionAsync(sb.ToString(), modelConfig, ct);
                partialAnswers.Add(partial);
            }

            // Combine partial answers into one JSON
            var finalPrompt = $@"
         You are an assistant that MUST return ONLY valid JSON.
         Here are several partial JSON answers for the same question:
         {string.Join("\n---\n", partialAnswers)}

         Merge them into ONE valid JSON object that includes:
         - TopThemes
         - TopRecommendations
         - TopActions
         - AzureOutput (matching Azure_optimized_constrained_OUTPUT_v4)

         Return ONLY JSON. No explanations, no markdown.
         ";

            return await CallChatCompletionAsync(finalPrompt, modelConfig, ct);
        }

        private async Task<string> CallChatCompletionAsync(string userContent, ModelConfiguration modelConfig, CancellationToken ct)
        {
            int tokenCount = CountTokens(userContent);
            await ThrottleAsync(tokenCount); //  request+token aware limiter

            // Build the full chat completions URL from ModelConfiguration fields
            // AI Foundry serverless endpoints already contain the full URL (e.g. /models/chat/completions)
            string url;
            var endpoint = modelConfig.Endpoint?.Trim() ?? "";
            if (endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                url = endpoint;
            }
            else
            {
                url = $"{endpoint.TrimEnd('/')}/openai/deployments/{modelConfig.ModelName?.Trim()}/chat/completions?api-version={modelConfig.ApiVersion}";
            }

            // DEBUG: Log the final URL being called
            Console.WriteLine($"[DocumentEmbedding] Calling URL: {url}");

            var messages = new[]
            {
              new { role = "system", content = "You are a helpful assistant." },
              new { role = "user", content = userContent }
             };

            var isAIFoundryServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase);

            var body = isAIFoundryServerless
                ? JsonSerializer.Serialize(new
                {
                    model = modelConfig.ModelName?.Trim(),
                    messages,
                    max_tokens = modelConfig.MaxTokens,
                    temperature = modelConfig.Temperature
                })
                : JsonSerializer.Serialize(new
                {
                    messages,
                    max_tokens = modelConfig.MaxTokens,
                    temperature = modelConfig.Temperature
                });

            // Build the final prompt string
            var finalPrompt = string.Join("\n", messages.Select(m => $"{m.role}: {m.content}"));

            // Save into HttpContext so middleware can pick it later
            _httpContextAccessor.HttpContext.Items["FinalPrompt"] = finalPrompt;

            // Create a fresh client to avoid conflicts with _client's base address/headers
            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("api-key", modelConfig.ApiKey);

            using var res = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            res.EnsureSuccessStatusCode();

            string responseText = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseText);

            // log usage if available
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                int promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                int completionTokens = usage.GetProperty("completion_tokens").GetInt32();
                int totalTokens = usage.GetProperty("total_tokens").GetInt32();
            }
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        private int CountTokens(string text)
        {
            var encoding = GptEncoding.GetEncoding("cl100k_base");
            return encoding.Encode(text).Count;
        }
        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        {
            var embeddingModel = await GetEmbeddingModelAsync();
            var url = $"{embeddingModel.Endpoint.TrimEnd('/')}/openai/deployments/{embeddingModel.ModelName}/embeddings?api-version={embeddingModel.ApiVersion}";

            // Split text into chunks if its too long
            int chunkSize = 2000;
            var chunks = Enumerable.Range(0, (int)Math.Ceiling((double)text.Length / chunkSize))
                .Select(i => text.Substring(i * chunkSize, Math.Min(chunkSize, text.Length - i * chunkSize)))
                .ToList();

            var allEmbeddings = new List<float[]>();

            foreach (var chunk in chunks)
            {
                var body = JsonSerializer.Serialize(new { input = chunk });

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("api-key", embeddingModel.ApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var client = _httpFactory.CreateClient();
                using var res = await client.SendAsync(request, ct);
                res.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var embedding = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

                var list = new List<float>();
                foreach (var v in embedding.EnumerateArray())
                    list.Add(v.GetSingle());

                allEmbeddings.Add(list.ToArray());
            }

            if (allEmbeddings.Count == 1) return allEmbeddings[0];

            int dim = allEmbeddings[0].Length;
            var avg = new float[dim];
            foreach (var emb in allEmbeddings)
            {
                for (int i = 0; i < dim; i++)
                    avg[i] += emb[i];
            }
            for (int i = 0; i < dim; i++)
                avg[i] /= allEmbeddings.Count;

            return avg;
        }
        private async Task<List<SearchResultDoc>> VectorSearchAsync(float[] vector, int topK = 5, CancellationToken ct = default)
        {
            // calls repository(PostgreSQL) instead of Azure
            var dbResults = await _repository.SearchSimilarAsync(new Pgvector.Vector(vector), topK);

            return dbResults.Select(r => new SearchResultDoc
            {
                Id = r.Document.Id.ToString(),
                Content = r.Document.Content,
                Score = 1 - r.Distance,
                Metadata = new Dictionary<string, object>()
            }).ToList();
        }
        private async Task ThrottleAsync(int tokensUsed)
        {
            await _semaphore.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;

                // clean old requests
                while (_requestLog.Count > 0 && now - _requestLog.Peek() > _window)
                    _requestLog.Dequeue();

                // clean old token usage
                while (_tokenLog.Count > 0 && now - _tokenLog.Peek().Item1 > _window)
                    _tokenLog.Dequeue();

                // calculate totals
                var tokenSum = _tokenLog.Sum(t => t.Item2);

                // check if we exceed limits
                if ((_requestLog.Count >= _rpm) || (tokenSum + tokensUsed > _tpm))
                {
                    if (_requestLog.Count > 0)
                    {
                        var waitTime = _window - (now - _requestLog.Peek());
                        if (waitTime > TimeSpan.Zero)
                        {
                            await Task.Delay(waitTime);
                        }
                    }
                    else
                    {
                        // fallback: if no requests logged but we exceeded token limit
                        await Task.Delay(1000); // backoff 1 second
                    }
                }

                // log request + tokens
                _requestLog.Enqueue(DateTime.UtcNow);
                _tokenLog.Enqueue((DateTime.UtcNow, tokensUsed));
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<string> RunPromptfooEvaluationAsync()
        {
            try
            {
                // Note: Ensure promptfoo is installed globally (npm install -g promptfoo)
                // and that the current user has permissions to run it.
                var projectRoot = _env.ContentRootPath;
                var configPath = Path.Combine(projectRoot, "promptfooconfig.yaml");
                var outputPath = Path.Combine(projectRoot, "promptfoo_results.json");

                if (!System.IO.File.Exists(configPath))
                {
                    return "{\"error\": \"promptfooconfig.yaml not found at " + configPath.Replace("\\", "\\\\") + "\"}";
                }

                var promptfooAuth = _configuration["Promptfoo:AuthorizationKey"];
                var sharingEnabled = _configuration.GetValue<bool>("Promptfoo:SharingEnabled");
                var shareFlag = sharingEnabled ? "--share" : "";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c npx --yes promptfoo@latest eval --config \"{configPath}\" --output \"{outputPath}\" --no-cache {shareFlag}",
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Authorization for Cloud
                if (!string.IsNullOrEmpty(promptfooAuth))
                {
                    startInfo.EnvironmentVariables["PROMPTFOO_API_KEY"] = promptfooAuth;
                }

                // Ensure it targets the correct cloud API
                // Ensure it targets the correct cloud app
                startInfo.EnvironmentVariables["PROMPTFOO_HOST"] = "https://www.promptfoo.app";

                using var process = Process.Start(startInfo);
                if (process == null) return "{\"error\": \"Failed to start promptfoo process.\"}";

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (System.IO.File.Exists(outputPath))
                {
                    return await System.IO.File.ReadAllTextAsync(outputPath);
                }

                return $"{{\"error\": \"results.json not generated.\", \"stdout\": \"{stdout.Replace("\"", "\\\"").Replace("\r\n", " ")}\", \"stderr\": \"{stderr.Replace("\"", "\\\"").Replace("\r\n", " ")}\"}}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"Exception during Promptfoo eval: {ex.Message.Replace("\"", "\\\"")}\"}}";
            }
        }

        // PromptConfiguration CRUD Implementation
        public async Task<PromptConfigurationResponse> UpsertPromptAsync(UpsertPromptConfigurationRequest request)
        {
            // If Id is provided and > 0, it's an Update
            if (request.Id.HasValue && request.Id.Value > 0)
            {
                var existing = await _repository.GetPromptByIdAsync(request.Id.Value);
                if (existing == null)
                    throw new KeyNotFoundException($"Prompt with Id {request.Id.Value} not found.");

                existing.Text = request.Text;
                // If IsDefault is null on update, keep existing value
                existing.IsDefault = request.IsDefault ?? existing.IsDefault;
                existing.ModifiedBy = request.ModifiedBy;

                await _repository.UpdatePromptAsync(existing);

                var updated = await _repository.GetPromptByIdAsync(request.Id.Value);
                return new PromptConfigurationResponse
                {
                    Id = updated.Id,
                    Text = updated.Text,
                    IsDefault = updated.IsDefault,
                    CreatedAt = updated.CreatedAt,
                    CreatedBy = updated.CreatedBy,
                    ModifiedAt = updated.ModifiedAt,
                    ModifiedBy = updated.ModifiedBy
                };
            }

            // Otherwise, it's a Create
            var entity = new PromptConfiguration
            {
                Text = request.Text,
                // On create: if client didn't send IsDefault, treat it as false
                IsDefault = request.IsDefault ?? false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = request.CreatedBy,
                // Ensure ModifiedBy is also populated for new records
                ModifiedAt = DateTime.UtcNow,
                ModifiedBy = request.ModifiedBy ?? request.CreatedBy,
                IsDel = false
            };

            var saved = await _repository.AddPromptAsync(entity);

            return new PromptConfigurationResponse
            {
                Id = saved.Id,
                Text = saved.Text,
                IsDefault = saved.IsDefault,
                CreatedAt = saved.CreatedAt,
                CreatedBy = saved.CreatedBy,
                ModifiedAt = saved.ModifiedAt,
                ModifiedBy = saved.ModifiedBy
            };
        }

        public async Task<object> GetPromptsAsync(int? id)
        {
            // If Id is provided, return single prompt
            if (id.HasValue && id.Value > 0)
            {
                var entity = await _repository.GetPromptByIdAsync(id.Value);
                if (entity == null)
                    return null;

                return new PromptConfigurationResponse
                {
                    Id = entity.Id,
                    Text = entity.Text,
                    IsDefault = entity.IsDefault,
                    CreatedAt = entity.CreatedAt,
                    CreatedBy = entity.CreatedBy,
                    ModifiedAt = entity.ModifiedAt,
                    ModifiedBy = entity.ModifiedBy
                };
            }

            // Otherwise, return all prompts
            var entities = await _repository.GetAllPromptsAsync();
            return entities.Select(e => new PromptConfigurationResponse
            {
                Id = e.Id,
                Text = e.Text,
                IsDefault = e.IsDefault,
                CreatedAt = e.CreatedAt,
                CreatedBy = e.CreatedBy,
                ModifiedAt = e.ModifiedAt,
                ModifiedBy = e.ModifiedBy
            }).ToList();
        }

        public async Task<bool> DeletePromptAsync(int id)
        {
            return await _repository.SoftDeletePromptAsync(id);
        }

        // ModelConfiguration CRUD Implementation
        public async Task<ModelConfigurationResponse> UpsertModelAsync(UpsertModelConfigurationRequest request)
        {
            // If Id is provided and > 0, it's an Update
            if (request.Id.HasValue && request.Id.Value > 0)
            {
                var existing = await _repository.GetModelByIdAsync(request.Id.Value);
                if (existing == null)
                    throw new KeyNotFoundException($"Model with Id {request.Id.Value} not found.");

                existing.ModelName = request.ModelName;
                existing.FriendlyName = request.FriendlyName;
                existing.ApiKey = request.ApiKey;
                existing.Endpoint = request.Endpoint;
                existing.ApiVersion = request.ApiVersion;
                existing.MaxTokens = request.MaxTokens;
                existing.Temperature = request.Temperature;
                // If IsDefault is null on update, keep existing value
                existing.IsDefault = request.IsDefault ?? existing.IsDefault;
                existing.ModelConfigTypeId = request.ModelConfigTypeId;
                existing.Provider = request.Provider;
                existing.Source = request.Source;
                existing.InputCostPer1k = request.InputCostPer1k;
                existing.OutputCostPer1k = request.OutputCostPer1k;
                existing.TopP = request.TopP;
                existing.IsSelected = request.IsSelected ?? existing.IsSelected;
                existing.ModifiedBy = request.ModifiedBy;

                // If this record is being set as default, clear all others with the same type
                if (existing.IsDefault)
                    await _repository.ClearDefaultByTypeAsync(existing.ModelConfigTypeId, excludeId: existing.Id);

                await _repository.UpdateModelAsync(existing);

                var updated = await _repository.GetModelByIdAsync(request.Id.Value);
                return MapToModelResponse(updated);
            }

            // Otherwise, it's a Create
            var isDefault = request.IsDefault ?? false;

            // If new record is default, clear all others with the same type first
            if (isDefault)
                await _repository.ClearDefaultByTypeAsync(request.ModelConfigTypeId);

            var entity = new ModelConfiguration
            {
                ModelName = request.ModelName,
                FriendlyName = request.FriendlyName,
                ApiKey = request.ApiKey,
                Endpoint = request.Endpoint,
                ApiVersion = request.ApiVersion,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                IsDefault = isDefault,
                ModelConfigTypeId = request.ModelConfigTypeId,
                Provider = request.Provider,
                Source = request.Source,
                InputCostPer1k = request.InputCostPer1k,
                OutputCostPer1k = request.OutputCostPer1k,
                TopP = request.TopP,
                IsSelected = request.IsSelected ?? false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = request.CreatedBy,
                // Ensure Modified fields are initialized for new records
                ModifiedAt = DateTime.UtcNow,
                ModifiedBy = request.ModifiedBy ?? request.CreatedBy,
                IsDel = false
            };

            var saved = await _repository.AddModelAsync(entity);
            return MapToModelResponse(saved);
        }

        public async Task<object> GetModelsAsync(int? id, int? modelConfigTypeId = null)
        {
            if (id.HasValue && id.Value > 0)
            {
                var entity = await _repository.GetModelByIdAsync(id.Value);
                if (entity == null)
                    return null;

                return MapToModelResponse(entity);
            }

            var entities = await _repository.GetAllModelsAsync(modelConfigTypeId);
            return entities.Select(e => MapToModelResponse(e)).ToList();
        }

        public async Task<bool> DeleteModelAsync(int id)
        {
            return await _repository.SoftDeleteModelAsync(id);
        }

        private ModelConfigurationResponse MapToModelResponse(ModelConfiguration entity)
        {
            return new ModelConfigurationResponse
            {
                Id = entity.Id,
                ModelName = entity.ModelName,
                FriendlyName = entity.FriendlyName,
                ApiKey = entity.ApiKey,
                Endpoint = entity.Endpoint,
                ApiVersion = entity.ApiVersion,
                MaxTokens = entity.MaxTokens,
                Temperature = entity.Temperature,
                IsDefault = entity.IsDefault,
                ModelConfigTypeId = entity.ModelConfigTypeId,
                Provider = entity.Provider,
                Source = entity.Source,
                InputCostPer1k = entity.InputCostPer1k,
                OutputCostPer1k = entity.OutputCostPer1k,
                TopP = entity.TopP,
                IsSelected = entity.IsSelected,
                CreatedAt = entity.CreatedAt,
                CreatedBy = entity.CreatedBy,
                ModifiedAt = entity.ModifiedAt,
                ModifiedBy = entity.ModifiedBy
            };
        }

    }
}
