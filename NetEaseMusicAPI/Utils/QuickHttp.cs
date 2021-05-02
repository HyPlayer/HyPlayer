using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NeteaseCloudMusicApi.Utils {
	/// <summary>
	/// a VERY VERY EASY http request helper
	/// </summary>
	/// <remarks>
	/// SOMETHING IMPORTANT:
	/// 
	/// 1.
	/// Default value of <see cref="HttpClientHandler.UseCookies"/> is true. Set <see cref="HttpClientHandler.UseCookies"/> to false so that <see cref="HttpClient"/> will use cookies in headers not in <see cref="HttpClientHandler"/>.
	/// In .NET Framework, when <see cref="HttpClientHandler.UseCookies"/> is true, <see cref="HttpClient"/> won't use cookies in <see cref="HttpClient"/>.
	/// In .NET Core, when <see cref="HttpClientHandler.UseCookies"/> is true, cookies in headers will be merged into final request not be replaced by cookies in <see cref="HttpClientHandler"/>.
	///
	/// 2.
	/// If HttpClientHandler can't automatically parse Set-Cookie correctly in .NET 5.0 and later versions, please try enabling 'System.Globalization.UseNls' switch.
	/// By default 'System.Globalization.UseNls' switch is false in .NET 5.0 and later versions and <see cref="System.Globalization.GregorianCalendar.TwoDigitYearMax"/> is 2029. It will cause a few cookies like 'Thu, 31-Dec-37 23:55:55 GMT' be regard as expired.
	/// How to enable NLS: https://docs.microsoft.com/en-us/dotnet/core/run-time-config/globalization#nls
	///
	/// 3.
	/// <see cref="HttpClient"/> is fully rewrited in .NET Core, so many behaviors are different with <see cref="HttpClient"/> in .NET Framework.
	/// </remarks>
	internal static class QuickHttp {
		public static async Task<byte[]> SendAsync(object url, object method, object headers = null, object content = null, bool ensureSuccessStatusCode = true, Out<HttpStatusCode> statusCode = null, Out<CookieCollection> setCookie = null, CancellationToken cancellationToken = default) {
			 var handler = new HttpClientHandler {
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
				UseCookies = false
			};
			 var client = new HttpClient(handler);
			 var response = await client.SendAsync(url, method, headers, content, cancellationToken);
			if (ensureSuccessStatusCode)
				response.EnsureSuccessStatusCode();
			if (!(statusCode is null))
				statusCode.Value = response.StatusCode;
			if (!(setCookie is null) && response.Headers.TryGetValues("Set-Cookie", out var cookies)) {
				var cookieCollection = setCookie.Value = new CookieCollection();
				foreach (string cookie in cookies)
					cookieCollection.Add(ParseCookies(cookie));
			}
#if DotNet50Plus
			return response.IsSuccessStatusCode ? await response.Content?.ReadAsByteArrayAsync(cancellationToken) : null;
#else
			return response.IsSuccessStatusCode ? await response.Content?.ReadAsByteArrayAsync() : null;
#endif
		}

		public static CookieCollection ParseCookies(IEnumerable<string> cookieHeaders) {
			if (cookieHeaders is null)
				return new CookieCollection();

			var cookies = new CookieCollection();
			foreach (string cookieHeader in cookieHeaders)
				ParseCookies(cookies, cookieHeader);
			return cookies;
		}

		public static CookieCollection ParseCookies(string cookieHeader) {
			if (string.IsNullOrEmpty(cookieHeader))
				return new CookieCollection();

			var cookies = new CookieCollection();
			ParseCookies(cookies, cookieHeader);
			return cookies;
		}

		private static void ParseCookies(CookieCollection cookies, string cookieHeader) {
			try {
				var cookie = new Cookie();
				var CookieDic = new Dictionary<string, string>();
				var arr1 = cookieHeader.Split(';').ToList();
				var arr2 = arr1[0].Trim().Split('=');
				cookie.Name = arr2[0];
				cookie.Value = arr2[1];
				arr1.RemoveAt(0);
				foreach (string cookiediac in arr1) {
					try {
						string[] cookiesetarr = cookiediac.Trim().Split('=');
						switch (cookiesetarr[0].Trim().ToLower()) {
						case "expires":
							cookie.Expires = DateTime.Parse(cookiesetarr[1].Trim());
							break;
						case "max-age":
							cookie.Expires = DateTime.Now.AddSeconds(int.Parse(cookiesetarr[1]));
							break;
						case "domain":
							cookie.Domain = cookiesetarr[1].Trim();
							break;
						case "path":
							cookie.Path = cookiesetarr[1].Trim().Replace("%x2F", "/");
							break;
						case "secure":
							cookie.Secure = cookiesetarr[1].Trim().ToLower() == "true";
							break;
						}
					}
					catch {
						continue;
					}
				}
				cookies.Add(cookie);
			}
			catch (Exception) {

			}
		}

		public static string ToCookieHeader(CookieCollection cookies) {
			if (cookies is null || cookies.Count == 0)
				return string.Empty;

#if DotNetCore30Plus
			return string.Join("; ", cookies.Select(t => t.Name + "=" + t.Value));
#else
			return string.Join("; ", cookies.Cast<Cookie>().Select(t => t.Name + "=" + t.Value));
#endif
		}

		public static async Task<HttpResponseMessage> SendAsync(this HttpClient client, object url, object method, object headers = null, object content = null, CancellationToken cancellationToken = default) {
			if (client is null)
				throw new ArgumentNullException(nameof(client));
			if (url is null)
				throw new ArgumentNullException(nameof(url));
			if (!(url is Uri) && !(url is string))
				throw new ArgumentOutOfRangeException(nameof(url), $"For '{url}', only the following types are supported: Uri, string");
			if (method is null)
				throw new ArgumentNullException(nameof(method));
			if (!(method is HttpMethod) && !(method is string))
				throw new ArgumentOutOfRangeException(nameof(method), $"For '{method}', only the following types are supported: HttpMethod, string");

			 var request = new HttpRequestMessage(method is string methodString ? new HttpMethod(methodString) : (HttpMethod)method, url is string urlString ? new Uri(urlString) : (Uri)url);
			if (!(content is null) && !ContentConverters.TryConvert(request, content))
				throw new ArgumentOutOfRangeException(nameof(content), $"For '{content}', only the following types are supported: {ContentConverters.SupportedTypesString}");
			if (!(headers is null) && !HeaderConverters.TryConvert(request, headers))
				throw new ArgumentOutOfRangeException(nameof(headers), $"For '{headers}', only the following types are supported: {HeaderConverters.SupportedTypesString}");
			return await client.SendAsync(request, cancellationToken);
		}

		/// <summary>
		/// Used for out parameter in async method
		/// </summary>
		/// <typeparam name="T"></typeparam>
		public sealed class Out<T> {
			public T Value;

			public static implicit operator T(Out<T> o) {
				return o.Value;
			}
		}

		// see https://github.com/dotnet/runtime/blob/master/src/libraries/System.Net.Http/src/System/Net/Http/Headers/KnownHeaders.cs
		private static class HeaderConverters {
			private sealed class HeaderConverter {
				public Type Type;
				public string TypeString;
				public Action<HttpRequestMessage, object> Convert;
			}

			private static readonly List<HeaderConverter> Converters = new List<HeaderConverter> {
				new HeaderConverter {
					Type = typeof(string),
					TypeString = "string",
					Convert = (request, headers) => Convert(request, (string)headers)
				},
				new HeaderConverter {
					Type = typeof(IEnumerable<KeyValuePair<string, string>>),
					TypeString = "IEnumerable<KeyValuePair<string, string>>",
					Convert = (request, headers) => Convert(request, (IEnumerable<KeyValuePair<string, string>>)headers)
				},
				new HeaderConverter {
					Type = typeof( IEnumerable<KeyValuePair<string, IEnumerable<string>>>),
					TypeString = " IEnumerable<KeyValuePair<string, IEnumerable<string>>>",
					Convert = (request, headers) => Convert(request, ( IEnumerable<KeyValuePair<string, IEnumerable<string>>>)headers)
				}
			};
			public static readonly string SupportedTypesString = string.Join(", ", Converters.Select(t => t.TypeString));
			//private static readonly HashSet<string> GeneralHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			//	"Cache-Control", "Connection", "Date", "Pragma", "Trailer", "Transfer-Encoding", "Upgrade", "Via", "Warning"
			//};
			//private static readonly HashSet<string> RequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			//	"Accept", "Accept-Charset", "Accept-Encoding", "Accept-Language", "Alt-Used", "Authorization", "Expect", "From",
			//	"Host", "If-Match", "If-Modified-Since", "If-None-Match", "If-Range", "If-Unmodified-Since", "Max-Forwards",
			//	"Proxy-Authorization", "Range", "Referer", "TE", "User-Agent"
			//};
			//private static readonly HashSet<string> ResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			//	":status", "Accept-Ranges", "Access-Control-Allow-Credentials", "Access-Control-Allow-Headers",
			//	"Access-Control-Allow-Methods", "Access-Control-Allow-Origin", "Access-Control-Expose-Headers", "Age",
			//	"Alt-Svc", "ETag", "Location", "Proxy-Authenticate", "Retry-After", "Server", "Vary", "WWW-Authenticate"
			//};
			private static readonly HashSet<string> ContentHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
				"Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Length", "Content-Location",
				"Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified"
			};

			public static bool TryConvert(HttpRequestMessage request, object headers) {
				foreach (var converter in Converters) {
					if (converter.Type.IsAssignableFrom(headers.GetType())) {
						converter.Convert(request, headers);
						return true;
					}
				}
				return false;
			}

			private static void Convert(HttpRequestMessage request, string headers) {
				 var reader = new StringReader(headers);
				string line;
				while (!((line = reader.ReadLine()) is null)) {
					if (line.Length == 0)
						continue;

					int index = line.IndexOf(':');
					if (index == -1)
						throw new ArgumentException($"Can't parse line: '{line}'", nameof(headers));

					string name = line.Substring(0, index).Trim();
					string value = line.Substring(index + 1).Trim();
					AddHeader(request, name, value);
				}
			}

			private static void Convert(HttpRequestMessage request, IEnumerable<KeyValuePair<string, string>> headers) {
				foreach (var header in headers)
					AddHeader(request, header.Key, header.Value);
			}

			private static void Convert(HttpRequestMessage request, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) {
				foreach (var header in headers)
					AddHeader(request, header.Key, header.Value);
			}

			private static void AddHeader(HttpRequestMessage request, string name, object value) {
				if (ContentHeaders.Contains(name)) {
					var content = request.Content;
					if (content is null)
						return;

					if (name.ToUpperInvariant() == "CONTENT-TYPE")
						content.Headers.Remove("Content-Type");
					// When we create a HttpContent, it will automatically set a Content-Type. We should remove it first otherwise header parser will throw an exception.
					if (value is string s)
						content.Headers.TryAddWithoutValidation(name, s);
					else
						content.Headers.TryAddWithoutValidation(name, (IEnumerable<string>)value);
				}
				else {
					if (value is string s)
						request.Headers.TryAddWithoutValidation(name, s);
					else
						request.Headers.TryAddWithoutValidation(name, (IEnumerable<string>)value);
				}
			}
		}

		private static class ContentConverters {
			private sealed class ContentConverter {
				public Type Type;
				public string TypeString;
				public Action<HttpRequestMessage, object> Convert;
			}

			private static readonly List<ContentConverter> Converters = new List<ContentConverter> {
				new ContentConverter {
					Type = typeof(HttpContent),
					TypeString = "HttpContent",
					Convert = (request, content) => Convert(request, (HttpContent)content)
				},
				new ContentConverter {
					Type = typeof(string),
					TypeString = "string",
					Convert = (request, content) => Convert(request, (string)content)
				},
				new ContentConverter {
					Type = typeof(IEnumerable<KeyValuePair<string, string>>),
					TypeString = "IEnumerable<KeyValuePair<string, string>>",
					Convert = (request, content) => Convert(request, (IEnumerable<KeyValuePair<string, string>>)content)
				},
				new ContentConverter {
					Type = typeof(byte[]),
					TypeString = "byte[]",
					Convert = (request, content) => Convert(request, (byte[])content)
				},
				new ContentConverter {
					Type = typeof(Stream),
					TypeString = "Stream",
					Convert = (request, content) => Convert(request, (Stream)content)
				}
			};
			public static readonly string SupportedTypesString = string.Join(", ", Converters.Select(t => t.TypeString));

			public static bool TryConvert(HttpRequestMessage request, object content) {
				foreach (var converter in Converters) {
					if (converter.Type.IsAssignableFrom(content.GetType())) {
						converter.Convert(request, content);
						return true;
					}
				}
				return false;
			}

			private static void Convert(HttpRequestMessage request, HttpContent content) {
				request.Content = content;
			}

			private static void Convert(HttpRequestMessage request, string content) {
				request.Content = new StringContent(content);
			}

			private static void Convert(HttpRequestMessage request, IEnumerable<KeyValuePair<string, string>> content) {
				request.Content = new FormUrlEncodedContent(content);
			}

			private static void Convert(HttpRequestMessage request, byte[] content) {
				request.Content = new ByteArrayContent(content);
			}

			private static void Convert(HttpRequestMessage request, Stream content) {
				request.Content = new StreamContent(content);
			}
		}
	}
}
