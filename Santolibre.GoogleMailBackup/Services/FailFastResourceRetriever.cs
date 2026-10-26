using iText.StyledXmlParser.Resolver.Resource;
using System;
using System.Collections.Generic;
using System.IO;

namespace Santolibre.GoogleMailBackup.Services
{
    // Remote images in old emails often point at hosts that are gone or that never answer. Without timeouts iText
    // waits for every single image, so a mail with dozens of them looks hung. A host that failed is skipped for a while.
#pragma warning disable CS0618 // the replacement type from iText.IO can't be passed to ConverterProperties in this iText version
    public class FailFastResourceRetriever : DefaultResourceRetriever
#pragma warning restore CS0618
    {
        private static readonly TimeSpan FailedHostBackoff = TimeSpan.FromMinutes(5);

        private readonly Action<string>? _onHostFailed;
        private readonly Dictionary<string, DateTime> _failedHosts = new Dictionary<string, DateTime>();

        public FailFastResourceRetriever(Action<string>? onHostFailed = null)
        {
            _onHostFailed = onHostFailed;
            SetConnectTimeout(5000);
            SetReadTimeout(10000);
        }

        public override Stream? GetInputStreamByUrl(Uri url)
        {
            if (_failedHosts.TryGetValue(url.Host, out var failedAt))
            {
                if (DateTime.UtcNow - failedAt < FailedHostBackoff)
                {
                    return null;
                }

                _failedHosts.Remove(url.Host);
            }

            try
            {
                return base.GetInputStreamByUrl(url);
            }
            catch (Exception)
            {
                _failedHosts[url.Host] = DateTime.UtcNow;
                _onHostFailed?.Invoke(url.Host);
                return null;
            }
        }
    }
}
