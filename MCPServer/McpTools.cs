using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OfficeOpenXml;

namespace MCPServer
{


    public class McpTools
    {
        private readonly string _dbPath;

        public McpTools(string dbPath = "testdata.db")
        {
            _dbPath = dbPath;
            InitializeDatabaseAsync();
        }

        public async Task InitializeDatabaseAsync()
        {
            // Delete existing database
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            // Create tables
            var createTables = @"
                CREATE TABLE Employees (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Department TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    Salary REAL NOT NULL,
                    HireDate TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE Projects (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProjectName TEXT NOT NULL,
                    Budget REAL NOT NULL,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT,
                    Status TEXT NOT NULL,
                    ManagerId INTEGER,
                    FOREIGN KEY (ManagerId) REFERENCES Employees(Id)
                );";

            var command = new SqliteCommand(createTables, connection);
            await command.ExecuteNonQueryAsync();

            // Insert test data
            await InsertTestDataAsync(connection);
            Console.Error.WriteLine("Database initialized with test data");
        }

        private async Task InsertTestDataAsync(SqliteConnection connection)
        {
            var departments = new[] { "IT", "HR", "Finance", "Marketing", "Sales", "Operations" };
            var names = new[] { "Anna Müller", "Max Weber", "Lisa Schmidt", "Tom Fischer", "Sarah Wagner", "Michael Klein", "Elena Bauer", "David Wolf", "Nina Kraus", "Alex Meyer" };

            Random random = new Random(42); // Fixed seed for consistent data

            // Insert 100 employees
            for (int i = 1; i <= 100; i++)
            {
                var name = names[random.Next(names.Length)] + $" {i}";
                var dept = departments[random.Next(departments.Length)];
                var email = $"{name.Split(' ')[0].ToLower()}.{name.Split(' ')[1].ToLower()}@company.com".Replace(" ", "");
                var salary = 30000 + random.Next(70000);
                var hireDate = DateTime.Now.AddDays(-random.Next(365 * 5)).ToString("yyyy-MM-dd");

                var insertEmployee = $@"
                    INSERT INTO Employees (Name, Department, Email, Salary, HireDate, IsActive)
                    VALUES ('{name}', '{dept}', '{email}', {salary}, '{hireDate}', {(random.Next(10) > 0 ? 1 : 0)});";

                var empCommand = new SqliteCommand(insertEmployee, connection);
                await empCommand.ExecuteNonQueryAsync();
            }

            // Insert 20 projects
            var projectNames = new[] { "Website Redesign", "Mobile App", "Data Migration", "Security Audit", "CRM Implementation", "Cloud Migration", "API Development", "Database Optimization", "User Portal", "Analytics Dashboard" };
            var statuses = new[] { "Active", "Completed", "On Hold", "Cancelled", "Planning" };

            for (int i = 1; i <= 20; i++)
            {
                var projectName = projectNames[random.Next(projectNames.Length)] + $" {i}";
                var budget = 50000 + random.Next(500000);
                var startDate = DateTime.Now.AddDays(-random.Next(200)).ToString("yyyy-MM-dd");
                var endDate = random.Next(2) == 0 ? DateTime.Now.AddDays(random.Next(200)).ToString("yyyy-MM-dd") : null;
                var status = statuses[random.Next(statuses.Length)];
                var managerId = random.Next(1, 21); // Random manager from first 20 employees

                var insertProject = $@"
                    INSERT INTO Projects (ProjectName, Budget, StartDate, EndDate, Status, ManagerId)
                    VALUES ('{projectName}', {budget}, '{startDate}', {(endDate != null ? $"'{endDate}'" : "NULL")}, '{status}', {managerId});";

                var projCommand = new SqliteCommand(insertProject, connection);
                await projCommand.ExecuteNonQueryAsync();
            }
        }

        public string Echo(string text)
        {
            return $"Echo: {text}";
        }

        public async Task<string> ExecuteSqliteQueryAsync(string query)
        {
            // Security: Only allow SELECT statements
            if (!query.Trim().ToUpper().StartsWith("SELECT"))
            {
                throw new InvalidOperationException("Only SELECT queries are allowed for security reasons");
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var command = new SqliteCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            var results = new List<Dictionary<string, object>>();
            var columnNames = new List<string>();

            // Get column names
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columnNames.Add(reader.GetName(i));
            }

            // Read data
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            return results.Count == 0 ?
                "No results found" :
                $"Found {results.Count} results:\n\n" +
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }

        public string ReadExcelFile(string filePath, string? sheetName = null)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = string.IsNullOrEmpty(sheetName) ?
                package.Workbook.Worksheets.FirstOrDefault() :
                package.Workbook.Worksheets[sheetName];

            if (worksheet == null)
            {
                throw new InvalidOperationException($"Worksheet not found: {sheetName ?? "first sheet"}");
            }

            var result = new StringBuilder();
            result.AppendLine($"Excel File: {Path.GetFileName(filePath)}");
            result.AppendLine($"Sheet: {worksheet.Name}");
            result.AppendLine($"Dimensions: {worksheet.Dimension?.Rows ?? 0} rows x {worksheet.Dimension?.Columns ?? 0} columns");
            result.AppendLine();

            if (worksheet.Dimension != null)
            {
                // Read first 10 rows or all rows if less than 10
                var maxRows = Math.Min(worksheet.Dimension.Rows, 10);
                var maxCols = worksheet.Dimension.Columns;

                result.AppendLine("Data preview (first 10 rows):");
                for (int row = 1; row <= maxRows; row++)
                {
                    var rowData = new List<string>();
                    for (int col = 1; col <= maxCols; col++)
                    {
                        var cellValue = worksheet.Cells[row, col].Value?.ToString() ?? "";
                        rowData.Add(cellValue);
                    }
                    result.AppendLine($"Row {row}: {string.Join(" | ", rowData)}");
                }
            }

            return result.ToString();
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
