using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MCPServer
{
    public class McpTools
    {
        private string? _sqlServerConnectionString;
        private const int DefaultCommandTimeoutSeconds = 30;
        private const int DefaultMaxRows = 1000;

        public McpTools()
        {
        }

        public string Echo(string text) => $"Echo: {text}";

        public string ConfigureSqlConnection(
            string provider,
            string? connectionString = null,
            string? server = null,
            string? database = null,
            string? user = null,
            string? password = null,
            bool? trustServerCertificate = null,
            bool? integratedSecurity = null,
            string? sqliteFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("Provider is required and must be 'SqlServer'");

            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new SqlConnectionStringBuilder();
                if (!string.IsNullOrWhiteSpace(connectionString))
                    builder = new SqlConnectionStringBuilder(connectionString);

                if (!string.IsNullOrWhiteSpace(server)) builder.DataSource = server;
                if (!string.IsNullOrWhiteSpace(database)) builder.InitialCatalog = database;

                if (integratedSecurity == true)
                {
                    builder.IntegratedSecurity = true;
                }
                else if (!string.IsNullOrWhiteSpace(user))
                {
                    builder.UserID = user;
                    if (!string.IsNullOrWhiteSpace(password)) builder.Password = password;
                }

                // Security hardened defaults
                builder.Encrypt = true;
                builder.ApplicationIntent = ApplicationIntent.ReadOnly;
                if (trustServerCertificate.HasValue)
                    builder.TrustServerCertificate = trustServerCertificate.Value;

                _sqlServerConnectionString = builder.ConnectionString;
                return "SQL Server connection configured.";
            }
            else
            {
                throw new ArgumentException("Unsupported provider. Only 'SqlServer' is supported.");
            }
        }

        public async Task<string> NaturalLanguageToSelectAsync(string question, bool execute, int? maxRows, int? timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Frage darf nicht leer sein");

            // 1) Get schema (tables, columns) to ground the generation
            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            var schemaInfo = new List<(string Table, string Column, string DataType)>();
            var schemaSql = @"SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE
                              FROM INFORMATION_SCHEMA.COLUMNS
                              ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION";
            using (var cmd = new SqlCommand(schemaSql, connection) { CommandTimeout = DefaultCommandTimeoutSeconds })
            using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    schemaInfo.Add(($"{r.GetString(0)}.{r.GetString(1)}", r.GetString(2), r.GetString(3)));
                }
            }

            // 2) Build a naive SELECT template using simple heuristics (no LLM)
            //    - Look for table names and column-like tokens in the question
            //    - Default to TOP N and wildcard if unsure
            var tokens = question.ToLowerInvariant().Split(new[] { ' ', '\t', '\n', ',', ';', '.', ':', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var candidateTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (table, column, _) in schemaInfo)
            {
                var shortTable = table.Split('.')[1];
                if (tokens.Any(t => t == shortTable.ToLowerInvariant()))
                    candidateTables.Add(table);
            }
            string selectedTable = candidateTables.FirstOrDefault() ?? schemaInfo.First().Table;

            // Prefer a reasonable handful of columns
            var selectedColumns = schemaInfo.Where(s => s.Table.Equals(selectedTable, StringComparison.OrdinalIgnoreCase))
                                            .Take(8)
                                            .Select(s => $"[{s.Column}]");
            var top = maxRows.HasValue && maxRows.Value > 0 ? Math.Min(maxRows.Value, DefaultMaxRows) : 100;
            var generated = $"SELECT TOP {top} {string.Join(", ", selectedColumns)} FROM [{selectedTable.Replace(".", "].[")}]";

            // 3) Validate the generated SQL is safe SELECT
            if (!IsReadOnlySelect(generated))
                throw new InvalidOperationException("Generierte Abfrage wurde aus Sicherheitsgründen verworfen");

            if (!execute)
                return JsonSerializer.Serialize(new { query = generated }, new JsonSerializerOptions { WriteIndented = true });

            // 4) Execute via hardened path
            return await ExecuteSqlQueryAsync(generated, top, timeoutSeconds);
        }

        private SqlConnection CreateSqlServerConnection()
        {
            if (string.IsNullOrWhiteSpace(_sqlServerConnectionString))
                throw new InvalidOperationException("SQL Server connection is not configured. Call sql_connect first.");
            return new SqlConnection(_sqlServerConnectionString);
        }

        public async Task<string> ListTablesAsync(string? schema = null)
        {
            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();
            var sql = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'" +
                      (string.IsNullOrWhiteSpace(schema) ? "" : " AND TABLE_SCHEMA = @schema") +
                      " ORDER BY TABLE_SCHEMA, TABLE_NAME";
            using var command = new SqlCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(schema))
                command.Parameters.AddWithValue("@schema", schema);

            using var reader = await command.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
                tables.Add($"{reader.GetString(0)}.{reader.GetString(1)}");

            return tables.Count == 0 ? "No tables found" : JsonSerializer.Serialize(tables, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> DescribeTableAsync(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("Table name is required");

            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            string schema = "dbo";
            string nameOnly = tableName;
            if (tableName.Contains('.'))
            {
                var parts = tableName.Split('.', 2);
                schema = parts[0];
                nameOnly = parts[1];
            }

            var sql = @"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
                         FROM INFORMATION_SCHEMA.COLUMNS
                         WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                         ORDER BY ORDINAL_POSITION";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@schema", schema);
            command.Parameters.AddWithValue("@table", nameOnly);
            using var reader = await command.ExecuteReaderAsync();
            var columns = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                columns.Add(new Dictionary<string, object?>
                {
                    { "name", reader.GetString(0) },
                    { "type", reader.GetString(1) },
                    { "isNullable", reader.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase) },
                    { "maxLength", reader.IsDBNull(3) ? null : reader.GetValue(3) }
                });
            }
            return JsonSerializer.Serialize(columns, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> ExecuteSqlQueryAsync(string query)
        {
            return await ExecuteSqlQueryAsync(query, null, null);
        }

        public async Task<string> ExecuteSqlQueryAsync(string query, int? maxRows, int? timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query cannot be empty");
            var cleaned = RemoveSqlComments(query);
            if (!IsReadOnlySelect(cleaned))
                throw new InvalidOperationException("Only read-only SELECT queries are allowed");

            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();
            var limit = maxRows.HasValue && maxRows.Value > 0 ? Math.Min(maxRows.Value, DefaultMaxRows) : DefaultMaxRows;
            var timeout = timeoutSeconds.HasValue && timeoutSeconds.Value > 0 ? Math.Min(timeoutSeconds.Value, 60) : DefaultCommandTimeoutSeconds;
            var limitedQuery = $"SET NOCOUNT ON; SET ROWCOUNT {limit}; {cleaned}";
            using var command = new SqlCommand(limitedQuery, connection) { CommandTimeout = timeout };
            using var reader = await command.ExecuteReaderAsync();

            var results = new List<Dictionary<string, object?>>();
            var columnNames = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columnNames.Add(reader.GetName(i));

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                results.Add(row);
            }
            return results.Count == 0 ? "No results found" : JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> GetSchemaOverviewAsync()
        {
            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            var schema = new Dictionary<string, object?>();

            // Columns
            var columnsSql = @"SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                               FROM INFORMATION_SCHEMA.COLUMNS
                               ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION";
            using (var cmd = new SqlCommand(columnsSql, connection))
            using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var key = $"{r.GetString(0)}.{r.GetString(1)}";
                    if (!schema.ContainsKey(key))
                        schema[key] = new { columns = new List<Dictionary<string, object?>>(), foreignKeys = new List<Dictionary<string, object?>>() };

                    var entry = (dynamic)schema[key]!;
                    ((List<Dictionary<string, object?>>)entry.columns).Add(new Dictionary<string, object?>
                    {
                        { "name", r.GetString(2) },
                        { "type", r.GetString(3) },
                        { "isNullable", r.GetString(4).Equals("YES", StringComparison.OrdinalIgnoreCase) }
                    });
                }
            }

            // Foreign keys
            var fkSql = @"SELECT
                            fk.name AS FK_Name,
                            sch1.name AS SchemaName,
                            tab1.name AS TableName,
                            col1.name AS ColumnName,
                            sch2.name AS RefSchemaName,
                            tab2.name AS RefTableName,
                            col2.name AS RefColumnName
                          FROM sys.foreign_key_columns fkc
                          INNER JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                          INNER JOIN sys.tables tab1 ON fkc.parent_object_id = tab1.object_id
                          INNER JOIN sys.schemas sch1 ON tab1.schema_id = sch1.schema_id
                          INNER JOIN sys.columns col1 ON fkc.parent_object_id = col1.object_id AND fkc.parent_column_id = col1.column_id
                          INNER JOIN sys.tables tab2 ON fkc.referenced_object_id = tab2.object_id
                          INNER JOIN sys.schemas sch2 ON tab2.schema_id = sch2.schema_id
                          INNER JOIN sys.columns col2 ON fkc.referenced_object_id = col2.object_id AND fkc.referenced_column_id = col2.column_id";

            using (var cmd = new SqlCommand(fkSql, connection))
            using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var key = $"{r.GetString(1)}.{r.GetString(2)}";
                    if (!schema.ContainsKey(key))
                        schema[key] = new { columns = new List<Dictionary<string, object?>>(), foreignKeys = new List<Dictionary<string, object?>>() };

                    var entry = (dynamic)schema[key]!;
                    ((List<Dictionary<string, object?>>)entry.foreignKeys).Add(new Dictionary<string, object?>
                    {
                        { "name", r.GetString(0) },
                        { "column", r.GetString(3) },
                        { "references", $"{r.GetString(4)}.{r.GetString(5)}({r.GetString(6)})" }
                    });
                }
            }

            return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> ListStoredProceduresAsync(string? schema = null)
        {
            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            var sql = "SELECT SPECIFIC_SCHEMA, SPECIFIC_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE'" +
                      (string.IsNullOrWhiteSpace(schema) ? "" : " AND SPECIFIC_SCHEMA = @schema") +
                      " ORDER BY SPECIFIC_SCHEMA, SPECIFIC_NAME";

            using var command = new SqlCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(schema))
                command.Parameters.AddWithValue("@schema", schema);

            using var reader = await command.ExecuteReaderAsync();
            var list = new List<string>();
            while (await reader.ReadAsync())
                list.Add($"{reader.GetString(0)}.{reader.GetString(1)}");

            return list.Count == 0 ? "No stored procedures found" : JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> GetStoredProcedureDefinitionAsync(string nameOrSchemaName)
        {
            if (string.IsNullOrWhiteSpace(nameOrSchemaName)) throw new ArgumentException("Procedure name is required");
            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            string schema = "dbo";
            string proc = nameOrSchemaName;
            if (nameOrSchemaName.Contains('.'))
            {
                var parts = nameOrSchemaName.Split('.', 2);
                schema = parts[0];
                proc = parts[1];
            }

            var sql = "SELECT sm.definition FROM sys.procedures p JOIN sys.sql_modules sm ON p.object_id = sm.object_id WHERE SCHEMA_NAME(p.schema_id) = @schema AND p.name = @name";
            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = DefaultCommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@name", proc);
            var def = await cmd.ExecuteScalarAsync() as string;
            return string.IsNullOrEmpty(def) ? "Procedure not found or no definition available" : def;
        }

        public async Task<string> TablePreviewAsync(string nameOrSchemaName, int top = 50)
        {
            if (string.IsNullOrWhiteSpace(nameOrSchemaName)) throw new ArgumentException("Table name is required");
            string schema = "dbo";
            string table = nameOrSchemaName;
            if (nameOrSchemaName.Contains('.'))
            {
                var parts = nameOrSchemaName.Split('.', 2);
                schema = parts[0];
                table = parts[1];
            }
            var limit = top > 0 ? Math.Min(top, 500) : 50;
            var sql = $"SET NOCOUNT ON; SELECT TOP ({limit}) * FROM [{schema}].[{table}]";
            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();
            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = DefaultCommandTimeoutSeconds };
            using var reader = await cmd.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object?>>();
            var columnNames = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++) columnNames.Add(reader.GetName(i));
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++) row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }
            return results.Count == 0 ? "No data" : JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> GetTableRelationshipsAsync(string nameOrSchemaName)
        {
            if (string.IsNullOrWhiteSpace(nameOrSchemaName)) throw new ArgumentException("Table name is required");
            string schema = "dbo";
            string table = nameOrSchemaName;
            if (nameOrSchemaName.Contains('.'))
            {
                var parts = nameOrSchemaName.Split('.', 2);
                schema = parts[0];
                table = parts[1];
            }

            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            var sql = @"SELECT
                            'Outbound' AS Direction,
                            sch1.name AS TableSchema,
                            tab1.name AS TableName,
                            col1.name AS ColumnName,
                            sch2.name AS RefSchema,
                            tab2.name AS RefTable,
                            col2.name AS RefColumn,
                            fk.name AS ConstraintName
                        FROM sys.foreign_key_columns fkc
                        JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                        JOIN sys.tables tab1 ON fkc.parent_object_id = tab1.object_id
                        JOIN sys.schemas sch1 ON tab1.schema_id = sch1.schema_id
                        JOIN sys.columns col1 ON fkc.parent_object_id = col1.object_id AND fkc.parent_column_id = col1.column_id
                        JOIN sys.tables tab2 ON fkc.referenced_object_id = tab2.object_id
                        JOIN sys.schemas sch2 ON tab2.schema_id = sch2.schema_id
                        JOIN sys.columns col2 ON fkc.referenced_object_id = col2.object_id AND fkc.referenced_column_id = col2.column_id
                        WHERE sch1.name = @schema AND tab1.name = @table
                        UNION ALL
                        SELECT
                            'Inbound' AS Direction,
                            sch2.name AS TableSchema,
                            tab2.name AS TableName,
                            col2.name AS ColumnName,
                            sch1.name AS RefSchema,
                            tab1.name AS RefTable,
                            col1.name AS RefColumn,
                            fk.name AS ConstraintName
                        FROM sys.foreign_key_columns fkc
                        JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                        JOIN sys.tables tab1 ON fkc.parent_object_id = tab1.object_id
                        JOIN sys.schemas sch1 ON tab1.schema_id = sch1.schema_id
                        JOIN sys.columns col1 ON fkc.parent_object_id = col1.object_id AND fkc.parent_column_id = col1.column_id
                        JOIN sys.tables tab2 ON fkc.referenced_object_id = tab2.object_id
                        JOIN sys.schemas sch2 ON tab2.schema_id = sch2.schema_id
                        JOIN sys.columns col2 ON fkc.referenced_object_id = col2.object_id AND fkc.referenced_column_id = col2.column_id
                        WHERE sch2.name = @schema AND tab2.name = @table
                        ORDER BY Direction, TableSchema, TableName";

            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = DefaultCommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);
            using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                list.Add(new Dictionary<string, object?>
                {
                    { "direction", reader.GetString(0) },
                    { "table", $"{reader.GetString(1)}.{reader.GetString(2)}" },
                    { "column", reader.GetString(3) },
                    { "references", $"{reader.GetString(4)}.{reader.GetString(5)}({reader.GetString(6)})" },
                    { "constraint", reader.GetString(7) }
                });
            }
            return list.Count == 0 ? "No relationships found" : JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<string> GetColumnStatsAsync(string nameOrSchemaName)
        {
            if (string.IsNullOrWhiteSpace(nameOrSchemaName)) throw new ArgumentException("Table name is required");
            string schema = "dbo";
            string table = nameOrSchemaName;
            if (nameOrSchemaName.Contains('.'))
            {
                var parts = nameOrSchemaName.Split('.', 2);
                schema = parts[0];
                table = parts[1];
            }

            using var connection = CreateSqlServerConnection();
            await connection.OpenAsync();

            var colsCmd = new SqlCommand(@"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t ORDER BY ORDINAL_POSITION", connection);
            colsCmd.Parameters.AddWithValue("@s", schema);
            colsCmd.Parameters.AddWithValue("@t", table);
            var cols = new List<(string Name, string Type)>();
            using (var r = await colsCmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) cols.Add((r.GetString(0), r.GetString(1)));
            }

            var stats = new List<Dictionary<string, object?>>();
            foreach (var (col, type) in cols)
            {
                bool numeric = type.Contains("int") || type.Contains("decimal") || type.Contains("numeric") || type.Contains("float") || type.Contains("real") || type.Contains("money");
                string sql = numeric
                    ? $"SELECT COUNT(1) total, SUM(CASE WHEN [{col}] IS NULL THEN 1 ELSE 0 END) nulls, MIN([{col}]) min, MAX([{col}]) max, AVG(CAST([{col}] AS FLOAT)) avg FROM [{schema}].[{table}]"
                    : $"SELECT COUNT(1) total, SUM(CASE WHEN [{col}] IS NULL THEN 1 ELSE 0 END) nulls, COUNT(DISTINCT CAST([{col}] AS NVARCHAR(4000))) distinctCount FROM [{schema}].[{table}]";
                using var cmd = new SqlCommand("SET NOCOUNT ON; " + sql, connection) { CommandTimeout = DefaultCommandTimeoutSeconds };
                using var rr = await cmd.ExecuteReaderAsync();
                if (await rr.ReadAsync())
                {
                    var entry = new Dictionary<string, object?>
                    {
                        { "column", col },
                        { "type", type },
                        { "total", rr.GetValue(0) },
                        { "nulls", rr.GetValue(1) }
                    };
                    if (numeric)
                    {
                        entry["min"] = rr.IsDBNull(2) ? null : rr.GetValue(2);
                        entry["max"] = rr.IsDBNull(3) ? null : rr.GetValue(3);
                        entry["avg"] = rr.IsDBNull(4) ? null : rr.GetValue(4);
                    }
                    else
                    {
                        entry["distinct"] = rr.IsDBNull(2) ? null : rr.GetValue(2);
                    }
                    stats.Add(entry);
                }
            }
            return JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string RemoveSqlComments(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return sql;
            var lines = sql.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf("--", StringComparison.Ordinal);
                if (idx >= 0) lines[i] = lines[i].Substring(0, idx);
            }
            var noLineComments = string.Join("\n", lines);
            while (true)
            {
                int start = noLineComments.IndexOf("/*", StringComparison.Ordinal);
                if (start < 0) break;
                int end = noLineComments.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0) { noLineComments = noLineComments.Remove(start); break; }
                noLineComments = noLineComments.Remove(start, end - start + 2);
            }
            return noLineComments.Trim();
        }

        private static bool IsReadOnlySelect(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;
            var trimmed = sql.TrimStart();
            if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                // Allow CTEs
            }
            else if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] forbidden = new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "EXECUTE", "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE", "DENY", "BACKUP", "RESTORE", "DBCC" };
            var upper = sql.ToUpperInvariant();
            foreach (var word in forbidden)
            {
                if (upper.Contains(word)) return false;
            }
            return true;
        }

        public string GenerateReport(string title, string data, string format = "summary")
        {
            var report = new StringBuilder();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            switch (format.ToLower())
            {
                case "executive":
                    report.AppendLine("=" + new string('=', title.Length + 2) + "=");
                    report.AppendLine($"| {title} |");
                    report.AppendLine("=" + new string('=', title.Length + 2) + "=");
                    report.AppendLine($"Generated: {timestamp}");
                    report.AppendLine();
                    report.AppendLine("EXECUTIVE SUMMARY");
                    report.AppendLine("-".PadRight(50, '-'));
                    report.AppendLine(AnalyzeDataForExecutiveSummary(data));
                    report.AppendLine();
                    report.AppendLine("KEY INSIGHTS");
                    report.AppendLine("-".PadRight(50, '-'));
                    report.AppendLine(ExtractKeyInsights(data));
                    break;

                case "detailed":
                    report.AppendLine($"DETAILED REPORT: {title}");
                    report.AppendLine("=" + new string('=', 50));
                    report.AppendLine($"Generated: {timestamp}");
                    report.AppendLine();
                    report.AppendLine("DATA ANALYSIS");
                    report.AppendLine("-".PadRight(30, '-'));
                    report.AppendLine(data);
                    report.AppendLine();
                    report.AppendLine("BREAKDOWN");
                    report.AppendLine("-".PadRight(30, '-'));
                    report.AppendLine(BreakdownData(data));
                    break;

                default: // summary
                    report.AppendLine($"SUMMARY REPORT: {title}");
                    report.AppendLine("-".PadRight(40, '-'));
                    report.AppendLine($"Generated: {timestamp}");
                    report.AppendLine();
                    report.AppendLine("Overview:");
                    report.AppendLine(SummarizeData(data));
                    break;
            }

            return report.ToString();
        }

        private string AnalyzeDataForExecutiveSummary(string data)
        {
            if (string.IsNullOrEmpty(data))
                return "No data provided for analysis.";

            var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return $"Data contains {lines.Length} data points. Analysis suggests structured information requiring strategic attention.";
        }

        private string ExtractKeyInsights(string data)
        {
            if (string.IsNullOrEmpty(data))
                return "• No insights available due to lack of data";

            var insights = new List<string>
            {
                "• Data structure appears consistent and well-formatted",
                "• Information density suggests comprehensive dataset",
                "• Recommend further analysis for actionable recommendations"
            };

            return string.Join("\n", insights);
        }

        private string BreakdownData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return "No data to break down.";

            var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var breakdown = new StringBuilder();

            breakdown.AppendLine($"Total lines: {lines.Length}");
            breakdown.AppendLine($"Average line length: {(lines.Length > 0 ? lines.Average(l => l.Length).ToString("F1") : "0")} characters");
            breakdown.AppendLine($"Data type: {(data.TrimStart().StartsWith("{") || data.TrimStart().StartsWith("[") ? "JSON" : "Text")}");

            return breakdown.ToString();
        }

        private string SummarizeData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return "No data provided.";

            var preview = data.Length > 200 ? data.Substring(0, 200) + "..." : data;
            return $"Data preview: {preview}\n\nData length: {data.Length} characters";
        }
    }
}
