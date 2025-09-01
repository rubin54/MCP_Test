
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var server = new McpServer();
                await server.RunAsync();
            }
            catch (Exception ex)
            {
                // Write errors to stderr, not stdout to avoid JSON protocol issues
                Console.Error.WriteLine($"Server error: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
