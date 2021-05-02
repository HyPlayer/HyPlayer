using System;
using System.Collections.Generic;
using System.Extensions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeteaseCloudMusicApi.Utils {
	internal static class Request {
		private static readonly string[] userAgentList = new string[] {
			// iOS 13.5.1 14.0 beta with safari
			"Mozilla/5.0 (iPhone; CPU iPhone OS 13_5_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.1.1 Mobile/15E148 Safari/604.1",
			"Mozilla/5.0 (iPhone; CPU iPhone OS 14_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.0 Mobile/15E148 Safari/604.",
			// iOS with qq micromsg
			"Mozilla/5.0 (iPhone; CPU iPhone OS 13_5_1 like Mac OS X) AppleWebKit/602.1.50 (KHTML like Gecko) Mobile/14A456 QQ/6.5.7.408 V1_IPH_SQ_6.5.7_1_APP_A Pixel/750 Core/UIWebView NetType/4G Mem/103",
			"Mozilla/5.0 (iPhone; CPU iPhone OS 13_5_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 MicroMessenger/7.0.15(0x17000f27) NetType/WIFI Language/zh",
			// Android -> Huawei Xiaomi
			"Mozilla/5.0 (Linux; Android 9; PCT-AL10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/70.0.3538.64 HuaweiBrowser/10.0.3.311 Mobile Safari/537.36",
			"Mozilla/5.0 (Linux; U; Android 9; zh-cn; Redmi Note 8 Build/PKQ1.190616.001) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/71.0.3578.141 Mobile Safari/537.36 XiaoMi/MiuiBrowser/12.5.22",
			// Android + qq micromsg
			"Mozilla/5.0 (Linux; Android 10; YAL-AL00 Build/HUAWEIYAL-AL00; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/78.0.3904.62 XWEB/2581 MMWEBSDK/200801 Mobile Safari/537.36 MMWEBID/3027 MicroMessenger/7.0.18.1740(0x27001235) Process/toolsmp WeChat/arm64 NetType/WIFI Language/zh_CN ABI/arm64",
			"Mozilla/5.0 (Linux; U; Android 8.1.0; zh-cn; BKK-AL10 Build/HONORBKK-AL10) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/66.0.3359.126 MQQBrowser/10.6 Mobile Safari/537.36",		
			// macOS 10.15.6  Firefox / Chrome / Safari
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:80.0) Gecko/20100101 Firefox/80.0",
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.30 Safari/537.36",
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.1.2 Safari/605.1.15",
			// Windows 10 Firefox / Chrome / Edge
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:80.0) Gecko/20100101 Firefox/80.0",
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.30 Safari/537.36",
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/42.0.2311.135 Safari/537.36 Edge/13.10586"
		};

		public static string ChooseUserAgent(string ua) {
			var random = new Random();
			switch (ua) {
			case "mobile":
				return userAgentList[random.Next(8)];
			case "pc":
				return userAgentList[random.Next(8, 14)];
			default:
				return string.IsNullOrEmpty(ua) ? userAgentList[random.Next(userAgentList.Length)] : ua;
			}
		}

		public static async Task<(bool, JObject)> CreateRequest(string method, string url, Dictionary<string, object> data, Options options, CookieCollection setCookie) {
			if (method is null)
				throw new ArgumentNullException(nameof(method));
			if (url is null)
				throw new ArgumentNullException(nameof(url));
			if (data is null)
				throw new ArgumentNullException(nameof(data));
			if (options is null)
				throw new ArgumentNullException(nameof(options));

			var headers = new Dictionary<string, string> {
				["User-Agent"] = ChooseUserAgent(options.UA),
				["Cookie"] = string.Join("; ", options.Cookie.Cast<Cookie>().Select(t => t.Name + "=" + t.Value))
			};
			if (method.ToUpperInvariant() == "POST")
				headers["Content-Type"] = "application/x-www-form-urlencoded";
			if (url.Contains("music.163.com"))
				headers["Referer"] = "https://music.163.com";
			if (!(options.RealIP is null))
				headers["X-Real-IP"] = options.RealIP;
			var data2 = default(Dictionary<string, string>);
			switch (options.Crypto) {
			case "weapi": {
				data["csrf_token"] = options.Cookie.Get("__csrf", string.Empty);
				data2 = Crypto.WEApi(data);
				url = Regex.Replace(url, @"\w*api", "weapi");
				break;
			}
			case "linuxapi": {
				data2 = Crypto.LinuxApi(new Dictionary<string, object> {
					["method"] = method,
					["url"] = Regex.Replace(url, @"\w*api", "api"),
					["params"] = data
				});
				headers["User-Agent"] = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.90 Safari/537.36";
				url = "https://music.163.com/api/linux/forward";
				break;
			}
			case "eapi": {
				var cookie = options.Cookie;
				string csrfToken = cookie.Get("__csrf", string.Empty);
				var header = new Dictionary<string, string>() {
					["osver"] = cookie.Get("osver", string.Empty), // 系统版本
					["deviceId"] = cookie.Get("deviceId", string.Empty), // encrypt.base64.encode(imei + '\t02:00:00:00:00:00\t5106025eb79a5247\t70ffbaac7')
					["appver"] = cookie.Get("appver", "8.0.0"), // app版本
					["versioncode"] = cookie.Get("versioncode", "140"), // 版本号
					["mobilename"] = cookie.Get("mobilename", string.Empty), // 设备model
					["buildver"] = cookie.Get("buildver", GetCurrentTotalSeconds().ToString()),
					["resolution"] = cookie.Get("resolution", "1920x1080"), // 设备分辨率
					["__csrf"] = csrfToken,
					["os"] = cookie.Get("os", "android"),
					["channel"] = cookie.Get("channel", string.Empty),
					["requestId"] = $"{GetCurrentTotalMilliseconds()}_{Math.Floor(new Random().NextDouble() * 1000).ToString().PadLeft(4, '0')}"
				};
				if (!(cookie["MUSIC_U"] is null))
					header["MUSIC_U"] = cookie["MUSIC_U"].Value;
				if (!(cookie["MUSIC_A"] is null))
					header["MUSIC_A"] = cookie["MUSIC_A"].Value;
				headers["Cookie"] = string.Join("; ", header.Select(t => t.Key + "=" + t.Value));
				data["header"] = JsonConvert.SerializeObject(header);
				data2 = Crypto.EApi(options.Url, data);
				url = Regex.Replace(url, @"\w*api", "eapi");
				break;
			}
			}
			try {
				 var handler = new HttpClientHandler {
					AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
					UseCookies = false,
					UseProxy = options.UseProxy
				};
				if (options.UseProxy)
					handler.Proxy = options.Proxy;
				 var client = new HttpClient(handler);
				 var response = await client.SendAsync(url, method, headers, data2);
				response.EnsureSuccessStatusCode();
				if (response.Headers.TryGetValues("Set-Cookie", out var rawSetCookie))
					setCookie.Add(QuickHttp.ParseCookies(rawSetCookie));
				string json;
				if (options.Crypto == "eapi") {
					try {
						json = Encoding.UTF8.GetString(Crypto.Decrypt(await response.Content.ReadAsByteArrayAsync()));
					}
					catch {
						json = await response.Content.ReadAsStringAsync();
					}
				}
				else {
					json = await response.Content.ReadAsStringAsync();
				}
				var body = JObject.Parse(json);
				int status = (int?)body["code"] ?? (int)response.StatusCode;
				var answer = new JObject {
					["status"] = status,
					["body"] = body
				};
				return (status == 200, answer);
			}
			catch (Exception ex) {
				return (false, new JObject {
					["status"] = 502,
					["body"] = new JObject {
						["code"] = 502,
						["msg"] = ex.ToFullString()
					}
				});
			}
		}

        private static ulong GetCurrentTotalMilliseconds()
        {
            var timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return (ulong)timeSpan.TotalMilliseconds;
        }

        private static ulong GetCurrentTotalSeconds()
        {
            var timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return (ulong)timeSpan.TotalSeconds;
        }
    }
}
