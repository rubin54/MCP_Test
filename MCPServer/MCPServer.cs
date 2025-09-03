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
                                description = "Gibt den eingegebenen Text zurück (Test/Debug)",
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
                                name = "sql_connect",
                                description = "Stellt eine sichere, schreibgeschützte Verbindung zu SQL Server her",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        provider = new { type = "string", description = "Nur 'SqlServer' wird unterstützt" },
                                        connectionString = new { type = "string", description = "Optional: komplette Verbindungszeichenfolge" },
                                        server = new { type = "string", description = "Servername oder host,port" },
                                        database = new { type = "string", description = "Datenbankname" },
                                        user = new { type = "string", description = "SQL-Benutzername" },
                                        password = new { type = "string", description = "SQL-Passwort" },
                                        trustServerCertificate = new { type = "boolean", description = "Nur falls benötigt (z. B. Test)" },
                                        integratedSecurity = new { type = "boolean", description = "Windows-Authentifizierung verwenden" }
                                    },
                                    required = new[] { "provider" }
                                }
                            },
                            new
                            {
                                name = "sql_list_tables",
                                description = "Listet Tabellen der verbundenen Datenbank auf (optional Schema-Filter)",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { schema = new { type = "string", description = "Schema-Filter, z. B. 'dbo'" } },
                                    required = Array.Empty<string>()
                                }
                            },
                            new
                            {
                                name = "sql_list_stored_procedures",
                                description = "Listet Stored Procedures der verbundenen Datenbank auf (optional Schema-Filter)",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { schema = new { type = "string", description = "Schema-Filter, z. B. 'dbo'" } },
                                    required = Array.Empty<string>()
                                }
                            },
                            new
                            {
                                name = "sql_table_preview",
                                description = "Zeigt die ersten Zeilen einer Tabelle an (nur Lesezugriff)",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { table = new { type = "string", description = "Schema.Tabelle oder Tabelle" }, top = new { type = "number", description = "Max. Zeilen (1-500), Standard 50" } },
                                    required = new[] { "table" }
                                }
                            },
                            new
                            {
                                name = "sql_procedure_definition",
                                description = "Liefert die T-SQL-Definition einer Stored Procedure",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { name = new { type = "string", description = "Schema.Name oder Name" } },
                                    required = new[] { "name" }
                                }
                            },
                            new
                            {
                                name = "sql_table_relationships",
                                description = "Zeigt eingehende/ausgehende Fremdschlüssel-Beziehungen einer Tabelle",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { table = new { type = "string", description = "Schema.Tabelle oder Tabelle" } },
                                    required = new[] { "table" }
                                }
                            },
                            new
                            {
                                name = "sql_column_stats",
                                description = "Berechnet Spalten-Statistiken (Anzahl, Nulls, Min/Max/Avg oder Distinct)",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { table = new { type = "string", description = "Schema.Tabelle oder Tabelle" } },
                                    required = new[] { "table" }
                                }
                            },
                            new
                            {
                                name = "sql_describe_table",
                                description = "Zeigt die Spalten einer Tabelle mit Typen und Nullable-Informationen",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { table = new { type = "string", description = "Schema.Tabelle oder Tabelle" } },
                                    required = new[] { "table" }
                                }
                            },
                            new
                            {
                                name = "sql_query",
                                description = "Führt eine sichere SELECT-Abfrage aus (nur Lesezugriff, begrenzt)",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { query = new { type = "string", description = "SELECT-Abfrage (Kommentare erlaubt)" }, maxRows = new { type = "number", description = "Maximale Zeilen (1-1000), Standard 1000" }, timeoutSeconds = new { type = "number", description = "Timeout in Sekunden (1-60), Standard 30" } },
                                    required = new[] { "query" }
                                }
                            },
                            new
                            {
                                name = "sql_nl_to_query",
                                description = "Wandelt eine natürliche Frage in eine sichere SELECT-Abfrage um (optional ausführen)",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { question = new { type = "string", description = "Natürliche Frage zur Datenbank" }, execute = new { type = "boolean", description = "Abfrage sofort ausführen (Standard: false)" }, maxRows = new { type = "number", description = "Maximale Zeilen (1-1000), Standard 100" }, timeoutSeconds = new { type = "number", description = "Timeout in Sekunden (1-60), Standard 30" } },
                                    required = new[] { "question" }
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
                    "sql_connect" => HandleSqlConnectTool(request),
                    "sql_list_tables" => HandleSqlListTablesTool(request),
                    "sql_list_stored_procedures" => HandleSqlListStoredProceduresTool(request),
                    "sql_table_preview" => HandleSqlTablePreviewTool(request),
                    "sql_procedure_definition" => HandleSqlProcedureDefinitionTool(request),
                    "sql_table_relationships" => HandleSqlTableRelationshipsTool(request),
                    "sql_column_stats" => HandleSqlColumnStatsTool(request),
                    "sql_describe_table" => HandleSqlDescribeTableTool(request),
                    "sql_query" => HandleSqlQueryTool(request),
                    "sql_schema_overview" => HandleSqlSchemaOverviewTool(request),
                    "sql_nl_to_query" => HandleSqlNaturalLanguageToQueryTool(request),
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

        private McpResponse HandleSqlConnectTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("provider", out var providerProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Provider is required" } };
            }

            try
            {
                string provider = providerProp.GetString() ?? "";
                string? connectionString = args.Value.TryGetProperty("connectionString", out var c) ? c.GetString() : null;
                string? server = args.Value.TryGetProperty("server", out var s) ? s.GetString() : null;
                string? database = args.Value.TryGetProperty("database", out var d) ? d.GetString() : null;
                string? user = args.Value.TryGetProperty("user", out var u) ? u.GetString() : null;
                string? password = args.Value.TryGetProperty("password", out var p) ? p.GetString() : null;
                bool? trust = args.Value.TryGetProperty("trustServerCertificate", out var t) ? t.GetBoolean() : null;
                bool? integrated = args.Value.TryGetProperty("integratedSecurity", out var i) ? i.GetBoolean() : null;
                string? sqlitePath = args.Value.TryGetProperty("sqliteFilePath", out var sp) ? sp.GetString() : null;

                var result = _tools.ConfigureSqlConnection(provider, connectionString, server, database, user, password, trust, integrated, sqlitePath);
                return new McpResponse
                {
                    Id = request.Id,
                    Result = new { content = new[] { new { type = "text", text = result } } }
                };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlListTablesTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            string? schema = null;
            if (args.HasValue && args.Value.TryGetProperty("schema", out var s)) schema = s.GetString();
            try
            {
                var result = _tools.ListTablesAsync(schema).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlListStoredProceduresTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            string? schema = null;
            if (args.HasValue && args.Value.TryGetProperty("schema", out var s)) schema = s.GetString();
            try
            {
                var result = _tools.ListStoredProceduresAsync(schema).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlTablePreviewTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("table", out var tableProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Table is required" } };
            }
            var table = tableProp.GetString() ?? "";
            int top = 50;
            if (args.Value.TryGetProperty("top", out var topProp) && topProp.ValueKind == JsonValueKind.Number)
            {
                top = Math.Clamp(topProp.GetInt32(), 1, 500);
            }
            try
            {
                var result = _tools.TablePreviewAsync(table, top).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlProcedureDefinitionTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("name", out var nameProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Name is required" } };
            }
            var name = nameProp.GetString() ?? "";
            try
            {
                var result = _tools.GetStoredProcedureDefinitionAsync(name).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlTableRelationshipsTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("table", out var tableProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Table is required" } };
            }
            var table = tableProp.GetString() ?? "";
            try
            {
                var result = _tools.GetTableRelationshipsAsync(table).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlColumnStatsTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("table", out var tableProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Table is required" } };
            }
            var table = tableProp.GetString() ?? "";
            try
            {
                var result = _tools.GetColumnStatsAsync(table).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlDescribeTableTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("table", out var tableProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Table is required" } };
            }
            var table = tableProp.GetString() ?? "";
            try
            {
                var result = _tools.DescribeTableAsync(table).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlQueryTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("query", out var queryProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "Query is required" } };
            }
            var query = queryProp.GetString() ?? "";
            int? maxRows = null;
            int? timeoutSeconds = null;
            if (args.Value.TryGetProperty("maxRows", out var mr) && mr.ValueKind == JsonValueKind.Number)
            {
                maxRows = Math.Clamp(mr.GetInt32(), 1, 1000);
            }
            if (args.Value.TryGetProperty("timeoutSeconds", out var ts) && ts.ValueKind == JsonValueKind.Number)
            {
                timeoutSeconds = Math.Clamp(ts.GetInt32(), 1, 60);
            }
            try
            {
                var result = _tools.ExecuteSqlQueryAsync(query, maxRows, timeoutSeconds).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlSchemaOverviewTool(McpRequest request)
        {
            try
            {
                var result = _tools.GetSchemaOverviewAsync().Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
            }
        }

        private McpResponse HandleSqlNaturalLanguageToQueryTool(McpRequest request)
        {
            var args = request.Params?.Arguments;
            if (!args.HasValue || !args.Value.TryGetProperty("question", out var qProp))
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = "question ist erforderlich" } };
            }
            var question = qProp.GetString() ?? "";
            bool execute = args.Value.TryGetProperty("execute", out var exProp) && exProp.ValueKind == JsonValueKind.True;
            int? maxRows = null;
            int? timeoutSeconds = null;
            if (args.Value.TryGetProperty("maxRows", out var mr) && mr.ValueKind == JsonValueKind.Number)
            {
                maxRows = Math.Clamp(mr.GetInt32(), 1, 1000);
            }
            if (args.Value.TryGetProperty("timeoutSeconds", out var ts) && ts.ValueKind == JsonValueKind.Number)
            {
                timeoutSeconds = Math.Clamp(ts.GetInt32(), 1, 60);
            }
            try
            {
                var result = _tools.NaturalLanguageToSelectAsync(question, execute, maxRows, timeoutSeconds).Result;
                return new McpResponse { Id = request.Id, Result = new { content = new[] { new { type = "text", text = result } } } };
            }
            catch (Exception ex)
            {
                return new McpResponse { Id = request.Id, Error = new McpError { Message = ex.Message } };
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
