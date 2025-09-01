using MCPServer;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

    public class McpServer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly McpTools _tools;

        public McpServer()
        {
            _tools = new McpTools();
        }

        public async Task RunAsync()
        {
            // Force UTF-8 without BOM
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);

            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            Console.Error.WriteLine("MCP Server starting...");

            try
            {
                string? line;
                while ((line = await stdin.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    Console.Error.WriteLine($"Received: {line}");

                    try
                    {
                        var request = JsonSerializer.Deserialize<McpRequest>(line, JsonOptions);
                        if (request == null)
                        {
                            Console.Error.WriteLine("Failed to deserialize request");
                            continue;
                        }

                        var response = HandleRequest(request);
                        var responseJson = JsonSerializer.Serialize(response, JsonOptions);

                        Console.Error.WriteLine($"Sending: {responseJson}");
                        await stdout.WriteLineAsync(responseJson);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error: {ex.Message}");
                        var errorResponse = new McpResponse
                        {
                            Id = 0,
                            Error = new McpError { Message = ex.Message }
                        };
                        var errorJson = JsonSerializer.Serialize(errorResponse, JsonOptions);
                        await stdout.WriteLineAsync(errorJson);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Server error: {ex.Message}");
                throw;
            }
        }

        private McpResponse HandleRequest(McpRequest request)
        {
            Console.Error.WriteLine($"Handling method: {request.Method}");

            return request.Method switch
            {
                "initialize" => new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            tools = new { }
                        },
                        serverInfo = new
                        {
                            name = "C# MCP Server",
                            version = "1.0.0"
                        }
                    }
                },

                "tools/list" => new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        tools = new object[]
                        {
                            new
                            {
                                name = "echo",
                                description = "Echo back the input text",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        text = new
                                        {
                                            type = "string",
                                            description = "Text to echo back"
                                        }
                                    },
                                    required = new[] { "text" }
                                }
                            },
                            new
                            {
                                name = "sqlite_query",
                                description = "Execute SQLite query on the employee database",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        query = new
                                        {
                                            type = "string",
                                            description = "SQL SELECT query to execute"
                                        }
                                    },
                                    required = new[] { "query" }
                                }
                            },
                            new
                            {
                                name = "read_excel",
                                description = "Read Excel file and return its contents",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        filePath = new
                                        {
                                            type = "string",
                                            description = "Path to the Excel file"
                                        },
                                        sheetName = new
                                        {
                                            type = "string",
                                            description = "Name of the sheet to read (optional)"
                                        }
                                    },
                                    required = new[] { "filePath" }
                                }
                            },
                            new
                            {
                                name = "generate_report",
                                description = "Generate a formatted report from data",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        title = new
                                        {
                                            type = "string",
                                            description = "Title of the report"
                                        },
                                        data = new
                                        {
                                            type = "string",
                                            description = "Data to include in the report"
                                        },
                                        format = new
                                        {
                                            type = "string",
                                            description = "Report format (summary, detailed, executive)",
                                            @enum = new[] { "summary", "detailed", "executive" }
                                        }
                                    },
                                    required = new[] { "title", "data" }
                                }
                            }
                        }
                    }
                },

                "tools/call" => HandleToolCall(request),

                _ => new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = $"Unknown method: {request.Method}" }
                }
            };
        }

        private McpResponse HandleToolCall(McpRequest request)
        {
            try
            {
                var toolName = request.Params?.Name;
                Console.Error.WriteLine($"Tool call: {toolName}");

                return toolName switch
                {
                    "echo" => HandleEchoTool(request),
                    "sqlite_query" => HandleSqliteQueryTool(request),
                    "read_excel" => HandleReadExcelTool(request),
                    "generate_report" => HandleGenerateReportTool(request),
                    _ => new McpResponse
                    {
                        Id = request.Id,
                        Error = new McpError { Message = $"Unknown tool: {toolName}" }
                    }
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Tool error: {ex.Message}");
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = $"Tool execution error: {ex.Message}" }
                };
            }
        }

        private McpResponse HandleEchoTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            var text = "No text provided";

            if (args.HasValue && args.Value.TryGetProperty("text", out var textProp))
            {
                text = textProp.GetString() ?? "No text provided";
            }

            var result = _tools.Echo(text);

            return new McpResponse
            {
                Id = request.Id,
                Result = new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = result
                        }
                    }
                }
            };
        }

        private McpResponse HandleSqliteQueryTool(McpRequest request)
        {
            var args = request.Params?.Arguments;

            if (!args.HasValue || !args.Value.TryGetProperty("query", out var queryProp))
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = "Query parameter is required" }
                };
            }

            var query = queryProp.GetString();
            if (string.IsNullOrEmpty(query))
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = "Query cannot be empty" }
                };
            }

            try
            {
                // Since this is an async method, we need to handle it properly
                var resultTask = _tools.ExecuteSqliteQueryAsync(query);
                var result = resultTask.Result; // For simplicity in this context

                return new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = result
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = ex.Message }
                };
            }
        }

        private McpResponse HandleReadExcelTool(McpRequest request)
        {
            var args = request.Params?.Arguments;

            if (!args.HasValue || !args.Value.TryGetProperty("filePath", out var filePathProp))
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = "FilePath parameter is required" }
                };
            }

            var filePath = filePathProp.GetString();
            if (string.IsNullOrEmpty(filePath))
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = "FilePath cannot be empty" }
                };
            }

            string? sheetName = null;
            if (args.Value.TryGetProperty("sheetName", out var sheetNameProp))
            {
                sheetName = sheetNameProp.GetString();
            }

            try
            {
                var result = _tools.ReadExcelFile(filePath, sheetName);

                return new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = result
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = ex.Message }
                };
            }
        }

        private McpResponse HandleGenerateReportTool(McpRequest request)
        {
            var args = request.Params?.Arguments;

            if (!args.HasValue)
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = "Arguments are required" }
                };
            }

            if (!args.Value.TryGetProperty("title", out var titleProp) ||
                !args.Value.TryGetProperty("data", out var dataProp))
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = "Title and data parameters are required" }
                };
            }

            var title = titleProp.GetString() ?? "Untitled Report";
            var data = dataProp.GetString() ?? "";
            var format = "summary";

            if (args.Value.TryGetProperty("format", out var formatProp))
            {
                format = formatProp.GetString() ?? "summary";
            }

            try
            {
                var result = _tools.GenerateReport(title, data, format);

                return new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = result
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Message = ex.Message }
                };
            }
        }
    }

    public class McpRequest
    {
        public string Jsonrpc { get; set; } = "2.0";
        public int Id { get; set; }
        public string Method { get; set; } = "";
        public McpParams? Params { get; set; }
    }

    public class McpParams
    {
        public string? Name { get; set; }
        public JsonElement? Arguments { get; set; }
    }

    public class McpResponse
    {
        public string Jsonrpc { get; set; } = "2.0";
        public int Id { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Result { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public McpError? Error { get; set; }
    }

    public class McpError
    {
        public string Message { get; set; } = "";
        public int Code { get; set; } = -1;
    }
