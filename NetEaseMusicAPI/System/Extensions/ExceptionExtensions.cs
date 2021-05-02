using System.Reflection;
using System.Text;

namespace System.Extensions {
	internal static class ExceptionExtensions {
		/// <summary>
		/// 获取最内层异常
		/// </summary>
		/// <param name="exception"></param>
		/// <returns></returns>
		public static Exception GetInmostException(this Exception exception) {
			if (exception is null)
				throw new ArgumentNullException(nameof(exception));

			return exception.InnerException is null ? exception : exception.InnerException.GetInmostException();
		}

		/// <summary>
		/// 返回一个字符串，其中包含异常的所有信息。
		/// </summary>
		/// <param name="exception"></param>
		/// <returns></returns>
		public static string ToFullString(this Exception exception) {
			if (exception is null)
				throw new ArgumentNullException(nameof(exception));

			var sb = new StringBuilder();
			DumpException(exception, sb);
			return sb.ToString();
		}

		private static void DumpException(Exception exception, StringBuilder sb) {
			sb.AppendLine($"Type: {Environment.NewLine}{exception.GetType().FullName}");
			sb.AppendLine($"Message: {Environment.NewLine}{exception.Message}");
			sb.AppendLine($"Source: {Environment.NewLine}{exception.Source}");
			sb.AppendLine($"StackTrace: {Environment.NewLine}{exception.StackTrace}");
			sb.AppendLine($"TargetSite: {Environment.NewLine}{exception.TargetSite}");
			sb.AppendLine("----------------------------------------");
			if (!(exception.InnerException is null))
				DumpException(exception.InnerException, sb);
			if (exception is ReflectionTypeLoadException reflectionTypeLoadException) {
				foreach (var loaderException in reflectionTypeLoadException.LoaderExceptions)
					DumpException(loaderException, sb);
			}
		}
	}
}
