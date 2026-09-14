namespace EventTicketing.ServiceDefaults.Authentication;

internal sealed class BackchannelAddressRewriteHandler(Uri publicAuthority, Uri internalAuthority)
    : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } requestUri &&
            string.Equals(requestUri.Host, publicAuthority.Host, StringComparison.OrdinalIgnoreCase) &&
            requestUri.Port == publicAuthority.Port)
        {
            var relativePath = requestUri.PathAndQuery;
            var publicPath = publicAuthority.AbsolutePath.TrimEnd('/');
            var internalPath = internalAuthority.AbsolutePath.TrimEnd('/');

            if (relativePath.StartsWith(publicPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = internalPath + relativePath[publicPath.Length..];
            }

            request.RequestUri = new UriBuilder(requestUri)
            {
                Scheme = internalAuthority.Scheme,
                Host = internalAuthority.Host,
                Port = internalAuthority.Port,
                Path = relativePath.Split('?', 2)[0],
                Query = requestUri.Query.TrimStart('?')
            }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
