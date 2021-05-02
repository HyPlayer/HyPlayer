using System.Collections.Generic;

namespace NeteaseCloudMusicApi {
	internal static class Extensions {
		public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue) {
			return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
		}
	}
}
