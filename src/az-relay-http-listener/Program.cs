namespace Dcs.Or.Gov.Azure.Azure.Relay.Http.Listener
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Azure Relay HTTP Reverse Proxy starting\n");

            var connectionString = Environment.GetEnvironmentVariable("RELAY_CONNECTION_STRING");
            var targetUriString = Environment.GetEnvironmentVariable("TARGET_URI")?.EnsureEndsWith("/");
            if (connectionString == null || targetUriString == null)
            {
                Console.WriteLine("Requires two environment variables: RELAY_CONNECTION_STRING and TARGET_URI.");
                return;
            }

            var targetUri = new Uri(targetUriString);
            RunAsync(connectionString, targetUri).GetAwaiter().GetResult();
        }

        static async Task RunAsync(string connectionString, Uri targetUri)
        {
            var hybridProxy = new HybridConnectionReverseProxy(connectionString, targetUri);
            await hybridProxy.OpenAsync(CancellationToken.None);

            Console.ReadLine();

            await hybridProxy.CloseAsync(CancellationToken.None);
        }
    }
}
