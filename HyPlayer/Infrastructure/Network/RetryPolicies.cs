using Polly;
using Polly.Retry;
using System;
using System.Threading.Tasks;

namespace HyPlayer.Infrastructure.Network
{
    /// <summary>
    /// Polly 重试策略配置类
    /// 提供统一的重试策略和错误处理机制
    /// </summary>
    public static class RetryPolicies
    {
        /// <summary>
        /// 网络请求重试策略
        /// 适用于 API 调用、网络下载等场景
        /// </summary>
        public static AsyncRetryPolicy NetworkRequestPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.Net.Http.HttpRequestException ||
                ex is TaskCanceledException ||
                ex is TimeoutException)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 指数退避
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    // 记录重试日志
                    System.Diagnostics.Debug.WriteLine(
                        $"网络请求重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalSeconds} 秒");
                });

        /// <summary>
        /// 媒体加载重试策略
        /// 适用于音频/视频文件加载
        /// </summary>
        public static AsyncRetryPolicy MediaLoadPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidOperationException)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500 * attempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"媒体加载重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                });

        /// <summary>
        /// 媒体源加载重试策略
        /// 适用于完整的媒体源加载流程
        /// </summary>
        public static AsyncRetryPolicy MediaSourceLoadPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidOperationException ||
                ex is ArgumentException ||
                ex.Message.Contains("文件大小不匹配") ||
                ex.Message.Contains("下载链接获取失败"))
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(600 * attempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"媒体源加载重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                });

        /// <summary>
        /// 文件操作重试策略
        /// 适用于文件读写、缓存操作
        /// </summary>
        public static AsyncRetryPolicy FileOperationPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(200 * attempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"文件操作重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                });

        /// <summary>
        /// 快速失败策略
        /// 适用于需要立即响应的场景
        /// </summary>
        public static AsyncRetryPolicy FastFailPolicy { get; } = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 1,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(100),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"快速重试 {retryCount}: {exception.Message}");
                });

        /// <summary>
        /// URL 获取重试策略
        /// 适用于歌曲 URL 获取和 API 调用
        /// </summary>
        public static AsyncRetryPolicy UrlFetchPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.Net.Http.HttpRequestException ||
                ex is TaskCanceledException ||
                ex is TimeoutException ||
                ex.Message.Contains("下载链接获取失败") ||
                ex.Message.Contains("API 请求失败"))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 指数退避
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"URL 获取重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalSeconds} 秒");
                });

        /// <summary>
        /// NCM 文件加载重试策略
        /// 适用于 NCM 文件解析
        /// </summary>
        public static AsyncRetryPolicy NcmFileLoadPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex.Message.Contains("NCM 文件格式不正确"))
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(300 * attempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"NCM 文件加载重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                });

        /// <summary>
        /// 本地文件加载重试策略
        /// 适用于本地文件访问
        /// </summary>
        public static AsyncRetryPolicy LocalFileLoadPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is InvalidOperationException)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(200 * attempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"本地文件加载重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                });

        /// <summary>
        /// 文件访问重试策略
        /// 适用于文件选择和权限操作
        /// </summary>
        public static AsyncRetryPolicy FileAccessPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException ||
                ex is InvalidOperationException)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(150 * attempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"文件访问重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                });

        /// <summary>
        /// API 调用重试策略
        /// 适用于网络 API 调用
        /// </summary>
        public static AsyncRetryPolicy ApiCallPolicy { get; } = Policy
            .Handle<Exception>(ex =>
                ex is System.Net.Http.HttpRequestException ||
                ex is TaskCanceledException ||
                ex is TimeoutException ||
                ex.Message.Contains("失败"))
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(1.5, attempt)), // 较温和的指数退避
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"API 调用重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalSeconds} 秒");
                });

        /// <summary>
        /// 创建自定义重试策略
        /// </summary>
        /// <param name="retryCount">重试次数</param>
        /// <param name="baseDelay">基础延迟时间</param>
        /// <param name="exponential">是否使用指数退避</param>
        /// <param name="onRetryAction">重试时的回调动作</param>
        public static AsyncRetryPolicy CreateCustomPolicy(
            int retryCount,
            TimeSpan baseDelay,
            bool exponential = true,
            Action<Exception, TimeSpan, int, Context> onRetryAction = null)
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: retryCount,
                    sleepDurationProvider: attempt => exponential
                        ? TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1))
                        : baseDelay,
                    onRetry: onRetryAction ?? ((exception, timespan, retryCount, context) =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"自定义重试 {retryCount}: {exception.Message}, 等待 {timespan.TotalMilliseconds} 毫秒");
                    }));
        }

        /// <summary>
        /// 执行带有重试保护的操作
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <param name="policy">使用的策略</param>
        /// <param name="fallbackValue">失败时的回退值</param>
        /// <returns>操作结果或回退值</returns>
        public static async Task<T> ExecuteWithFallbackAsync<T>(
            Func<Task<T>> action,
            AsyncRetryPolicy policy,
            T fallbackValue = default!)
        {
            try
            {
                return await policy.ExecuteAsync(action);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"操作失败，使用回退值: {ex.Message}");
                return fallbackValue;
            }
        }

        /// <summary>
        /// 执行不返回值的操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="policy">使用的策略</param>
        public static async Task ExecuteAsync(
            Func<Task> action,
            AsyncRetryPolicy policy)
        {
            try
            {
                await policy.ExecuteAsync(action);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"操作失败: {ex.Message}");
                // 记录错误但不抛出
            }
        }
    }
}