using System;
using System.Collections.Generic;
using System.Net.Http;
using NeteaseCloudMusicApi.Utils;

namespace NeteaseCloudMusicApi {
	/// <summary>
	/// 网易云音乐API提供者
	/// </summary>
	public sealed class CloudMusicApiProvider {
		/// <summary />
		public string Route { get; }

		internal HttpMethod Method { get; }

		internal Func<Dictionary<string, object>, string> Url { get; }

		internal ParameterInfo[] ParameterInfos { get; }

		internal Options Options { get; }

		internal Func<Dictionary<string, object>, Dictionary<string, object>> DataProvider { get; set; }

		internal Func<Dictionary<string, object>, Dictionary<string, object>> Data => DataProvider ?? GetData;

		internal CloudMusicApiProvider(string router) {
			if (string.IsNullOrEmpty(router))
				throw new ArgumentNullException(nameof(router));

			Route = router;
		}

		internal CloudMusicApiProvider(string router, HttpMethod method, string url, ParameterInfo[] parameterInfos, Options options) : this(router, method, _ => url, parameterInfos, options) {
		}

		internal CloudMusicApiProvider(string router, HttpMethod method, Func<Dictionary<string, object>, string> url, ParameterInfo[] parameterInfos, Options options) {
			Route = router;
			Method = method;
			Url = url;
			ParameterInfos = parameterInfos;
			Options = options;
		}

		private Dictionary<string, object> GetData(Dictionary<string, object> queries) {
			if (ParameterInfos.Length == 0)
				return new Dictionary<string, object>();
			var data = new Dictionary<string, object>();
			foreach (var parameterInfo in ParameterInfos) {
				switch (parameterInfo.Type) {
				case ParameterType.Required:
					data.Add(parameterInfo.Key, parameterInfo.GetRealValue(queries[parameterInfo.GetForwardedKey()]));
					break;
				case ParameterType.Optional: {
					object value = queries.TryGetValue(parameterInfo.GetForwardedKey(), out value) ? parameterInfo.GetRealValue(value) : parameterInfo.DefaultValue;
					if (!(value is null))
						data.Add(parameterInfo.Key, value);
					break;
				}
				case ParameterType.Constant:
					data.Add(parameterInfo.Key, parameterInfo.DefaultValue);
					break;
				case ParameterType.Custom: {
					object value = parameterInfo.CustomHandler(queries);
					if (!(value is null))
						data.Add(parameterInfo.Key, value);
					break;
				}
				default:
					throw new ArgumentOutOfRangeException(nameof(parameterInfo));
				}
			}
			return data;
		}

		/// <summary />
		public override string ToString() {
			return Route;
		}

		internal enum ParameterType {
			Required,
			Optional,
			Constant,
			Custom
		}

		internal sealed class ParameterInfo {
			public string Key;
			public ParameterType Type;
			public object DefaultValue;
			public string KeyForwarding;
			public Func<object, object> Transformer;
			public Func<Dictionary<string, object>, object> CustomHandler;

			public ParameterInfo(string key) : this(key, ParameterType.Required) {
			}

			public ParameterInfo(string key, ParameterType type) : this(key, type, null) {
			}

			public ParameterInfo(string key, ParameterType type, object defaultValue) {
				Key = key;
				Type = type;
				DefaultValue = defaultValue;
			}

			public string GetForwardedKey() {
				return KeyForwarding ?? Key;
			}

			public object GetRealValue(object value) {
				return Transformer is null ? value : Transformer(value);
			}
		}
	}
}
