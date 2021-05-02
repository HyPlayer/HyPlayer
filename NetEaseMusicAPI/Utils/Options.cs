using System.Net;

namespace NeteaseCloudMusicApi.Utils {
	internal sealed class Options {
		public string Crypto;
		public CookieCollection Cookie;
		public string UA;
		public string Url;
		public string RealIP;
		public bool UseProxy;
		public IWebProxy Proxy;
	}
}
