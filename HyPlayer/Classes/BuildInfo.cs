namespace HyPlayer.Classes
{
    /// <summary>
    /// 编译信息类，包含编译时的 GitHub Commit SHA、编译时间和编译的 JobId
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>
        /// GitHub Commit SHA (占位符: __COMMIT_SHA__)
        /// </summary>
        public static readonly string CommitSha = "__COMMIT_SHA__";

        /// <summary>
        /// 编译时间 (占位符: __BUILD_TIME__)
        /// </summary>
        public static readonly string BuildTime = "__BUILD_TIME__";

        /// <summary>
        /// 编译的 JobId (占位符: __BUILD_JOB_ID__)
        /// </summary>
        public static readonly string BuildJobId = "__BUILD_JOB_ID__";

        /// <summary>
        /// 编译的 BranchId (占位符: __BUILD_BRANCH_ID__)
        /// </summary>
        public static readonly string BuildBranchId = "__BUILD_BRANCH_ID__";

        /// <summary>
        /// 获取完整的编译信息字符串
        /// </summary>
        /// <returns>格式化的编译信息</returns>
        public static string GetBuildInfo()
        {
            return $"Commit: {CommitSha}, BuildTime: {BuildTime}, JobId: {BuildJobId}";
        }

        /// <summary>
        /// 检查是否为占位符（未被替换）
        /// </summary>
        /// <returns>如果所有字段都是占位符则返回true</returns>
        public static bool IsPlaceholder()
        {
            return CommitSha.StartsWith("__") && BuildTime.StartsWith("__") && BuildJobId.StartsWith("__");
        }
    }
}