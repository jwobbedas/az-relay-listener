using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Azure.Relay;

namespace Dcs.Or.Gov.Azure.Azure.Relay.Http.Listener
{
    class HybridConnectionReverseProxy
    {
        readonly HybridConnectionListener _listener;
        readonly HttpClient _httpClient;
        readonly string _hybridConnectionSubpath;

        public HybridConnectionReverseProxy(string connectionString, Uri targetUri)
        {
            _listener = new HybridConnectionListener(connectionString);
            _httpClient = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });
            _httpClient.BaseAddress = targetUri;
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _hybridConnectionSubpath = _listener.Address.AbsolutePath.EnsureEndsWith("/");
        }

        public async Task OpenAsync(CancellationToken cancelToken)
        {
            _listener.RequestHandler = (context) => this.RequestHandler(context);
            await _listener.OpenAsync(cancelToken);
            Console.WriteLine($"Forwarding from {_listener.Address} to {_httpClient.BaseAddress}.");
            Console.WriteLine("utcTime, request, statusCode, durationMs");
        }

        public Task CloseAsync(CancellationToken cancelToken)
        {
            return _listener.CloseAsync(cancelToken);
        }

        async void RequestHandler(RelayedHttpListenerContext context)
        {
            DateTime startTimeUtc = DateTime.UtcNow;
            try
            {
                var requestMessage = CreateHttpRequestMessage(context);
                var responseMessage = await _httpClient.SendAsync(requestMessage);
                await SendResponseAsync(context, responseMessage);
                await context.Response.CloseAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.GetType().Name}: {e.Message}");
                SendErrorResponse(e, context);
            }
            finally
            {
                LogRequest(startTimeUtc, context);
            }
        }

        async Task SendResponseAsync(RelayedHttpListenerContext context, HttpResponseMessage responseMessage)
        {
            context.Response.StatusCode = responseMessage.StatusCode;
            context.Response.StatusDescription = responseMessage.ReasonPhrase;
            foreach (KeyValuePair<string, IEnumerable<string>> header in responseMessage.Headers)
            {
                if (string.Equals(header.Key, "Transfer-Encoding"))
                {
                    continue;
                }

                context.Response.Headers.Add(header.Key, string.Join(",", header.Value));
            }
            
            foreach (KeyValuePair<string, IEnumerable<string>> header in responseMessage.Content.Headers)
            {
                context.Response.Headers.Add(header.Key, string.Join(",", header.Value));
            }

            var responseStream = await responseMessage.Content.ReadAsStreamAsync();
            await responseStream.CopyToAsync(context.Response.OutputStream);
        }

        void SendErrorResponse(Exception e, RelayedHttpListenerContext context)
        {
            context.Response.StatusCode = HttpStatusCode.InternalServerError;

#if DEBUG || INCLUDE_ERROR_DETAILS
            context.Response.StatusDescription = $"Internal Server Error: {e.GetType().FullName}: {e.Message}";
#endif
            context.Response.Close();
        }

        HttpRequestMessage CreateHttpRequestMessage(RelayedHttpListenerContext context)
        {
            var requestMessage = new HttpRequestMessage();
            if (context.Request.HasEntityBody)
            {
                requestMessage.Content = new StreamContent(context.Request.InputStream);
                var contentType = context.Request.Headers[HttpRequestHeader.ContentType];
                if (!string.IsNullOrEmpty(contentType))
                {
                    contentType = contentType.Split(';')[0].Trim();
                    requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                }
            }

            var relativePath = context.Request.Url.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);
            relativePath = relativePath.Replace(_hybridConnectionSubpath, string.Empty, StringComparison.OrdinalIgnoreCase);
            requestMessage.RequestUri = new Uri(relativePath, UriKind.RelativeOrAbsolute);
            requestMessage.Method = new HttpMethod(context.Request.HttpMethod);

            foreach (var headerName in context.Request.Headers.AllKeys)
            {
                if (string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    // Don't flow these headers here
                    continue;
                }

                requestMessage.Headers.Add(headerName, context.Request.Headers[headerName]);
            }

            return requestMessage;
        }

        void LogRequest(DateTime startTimeUtc, RelayedHttpListenerContext context)
        {
            var stopTimeUtc = DateTime.UtcNow;
            var buffer = new StringBuilder();
            buffer.Append($"{startTimeUtc.ToString("s", CultureInfo.InvariantCulture)}, ");
            buffer.Append($"\"{context.Request.HttpMethod} {context.Request.Url.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped)}\", ");
            buffer.Append($"{(int)context.Response.StatusCode}, ");
            buffer.Append($"{(int)stopTimeUtc.Subtract(startTimeUtc).TotalMilliseconds}");
            Console.WriteLine(buffer);
        }
    }
}
