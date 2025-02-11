/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * This source code is licensed under the license found in the
 * LICENSE file in the root directory of this source tree.
 */

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using UnityEngine;

namespace Meta.WitAi
{
    /// <summary>
    /// The various logging options for VLog
    /// </summary>
    public enum VLogLevel
    {
        Error = 0,
        Warning = 1,
        Log = 2,
        Info = 3
    }

    /// <summary>
    /// A class for internal Meta.Voice logs
    /// </summary>
    public static class VLog
    {
        #if UNITY_EDITOR
        /// <summary>
        /// Ignores logs in editor if less than log level (Error = 0, Warning = 2, Log = 3)
        /// </summary>
        public static VLogLevel EditorLogLevel
        {
            get => _editorLogLevel;
            set
            {
                _editorLogLevel = value;
                UnityEditor.EditorPrefs.SetString(EDITOR_LOG_LEVEL_KEY, _editorLogLevel.ToString());
            }
        }
        private static VLogLevel _editorLogLevel = (VLogLevel)(-1);
        private const string EDITOR_LOG_LEVEL_KEY = "VSDK_EDITOR_LOG_LEVEL";
        private const VLogLevel EDITOR_LOG_LEVEL_DEFAULT = VLogLevel.Warning;

        // Init on load
        [UnityEngine.RuntimeInitializeOnLoadMethod]
        public static void Init()
        {
            // Already init
            if (_editorLogLevel != (VLogLevel) (-1))
            {
                return;
            }

            // Load log
            string editorLogLevel = UnityEditor.EditorPrefs.GetString(EDITOR_LOG_LEVEL_KEY, EDITOR_LOG_LEVEL_DEFAULT.ToString());

            // Try parsing
            if (!Enum.TryParse(editorLogLevel, out _editorLogLevel))
            {
                // If parsing fails, use default log level
                EditorLogLevel = EDITOR_LOG_LEVEL_DEFAULT;
            }
        }
        #endif

        /// <summary>
        /// Hides all errors from the console
        /// </summary>
        public static bool SuppressLogs { get; set; } = !Application.isEditor && !UnityEngine.Debug.isDebugBuild;

        /// <summary>
        /// Event for appending custom data to a log before logging to console
        /// </summary>
        public static event Action<StringBuilder, string, VLogLevel> OnPreLog;

        /// <summary>
        /// Performs a Debug.Log with custom categorization and using the global log level of Info
        /// </summary>
        /// <param name="log">The text to be debugged</param>
        /// <param name="logCategory">The category of the log</param>
        public static void I(object log) => Log(VLogLevel.Info, null, log);
        public static void I(string logCategory, object log) => Log(VLogLevel.Info, logCategory, log);

        /// <summary>
        /// Performs a Debug.Log with custom categorization and using the global log level
        /// </summary>
        /// <param name="log">The text to be debugged</param>
        /// <param name="logCategory">The category of the log</param>
        public static void D(object log) => Log(VLogLevel.Log, null, log);
        public static void D(string logCategory, object log) => Log(VLogLevel.Log, logCategory, log);

        /// <summary>
        /// Performs a Debug.LogWarning with custom categorization and using the global log level
        /// </summary>
        /// <param name="log">The text to be debugged</param>
        /// <param name="logCategory">The category of the log</param>
        public static void W(object log, Exception e = null) => Log(VLogLevel.Warning, null, log, e);
        public static void W(string logCategory, object log, Exception e = null) => Log(VLogLevel.Warning, logCategory, log, e);

        /// <summary>
        /// Performs a Debug.LogError with custom categorization and using the global log level
        /// </summary>
        /// <param name="log">The text to be debugged</param>
        /// <param name="logCategory">The category of the log</param>
        public static void E(object log, Exception e = null) => Log(VLogLevel.Error, null, log, e);
        public static void E(string logCategory, object log, Exception e = null) => Log(VLogLevel.Error, logCategory, log, e);

        /// <summary>
        /// Filters out unwanted logs, appends category information
        /// and performs UnityEngine.Debug.Log as desired
        /// </summary>
        /// <param name="logType"></param>
        /// <param name="log"></param>
        /// <param name="category"></param>
        public static void Log(VLogLevel logType, string logCategory, object log, Exception exception = null)
        {
            #if UNITY_EDITOR
            // Skip logs with higher log type then global log level
            if ((int) logType > (int)EditorLogLevel)
            {
                return;
            }
            #endif
            // Suppress all except errors
            if (SuppressLogs && (int)logType > (int)VLogLevel.Error)
            {
                return;
            }

            // Use calling category if null
            string category = logCategory;
            if (string.IsNullOrEmpty(category))
            {
                category = GetCallingCategory();
            }

            // String builder
            StringBuilder result = new StringBuilder();

            #if !UNITY_EDITOR && !UNITY_ANDROID
            {
                // Start with datetime if not done so automatically
                DateTime now = DateTime.Now;
                result.Append($"[{now.ToShortDateString()} {now.ToShortTimeString()}] ");
            }
            #endif

            // Insert log type
            int start = result.Length;
            result.Append($"[{logType.ToString().ToUpper()}] ");
            WrapWithLogColor(result, start, logType);

            // Append VDSK & Category
            start = result.Length;
            result.Append("[VSDK");
            if (!string.IsNullOrEmpty(category))
            {
                result.Append($" {category}");
            }
            result.Append("] ");
            WrapWithCallingLink(result, start);

            // Append the actual log
            result.Append(log == null ? string.Empty : log.ToString());

            // Final log append
            OnPreLog?.Invoke(result, logCategory, logType);

            object message = result;
            if (null != exception)
            {
                #if UNITY_EDITOR
                message = string.Format("{0}\n<color=\"#ff6666\"><b>{1}:</b> {2}</color>\n=== STACK TRACE ===\n{3}\n=====", result, exception.GetType().Name, exception.Message, FormatStackTrace(exception.StackTrace));
                #endif
            }

            // Log
            switch (logType)
            {
                case VLogLevel.Error:
                    UnityEngine.Debug.LogError(message);
                    break;
                case VLogLevel.Warning:
                    UnityEngine.Debug.LogWarning(message);
                    break;
                default:
                    UnityEngine.Debug.Log(message);
                    break;
            }
        }

        public static string FormatStackTrace(string stackTrace)
        {
            // Get the project's working directory
            string workingDirectory = Directory.GetCurrentDirectory();
            // Use a regular expression to match lines with a file path and line number
            var regex = new Regex(@"at (.+) in (.*):(\d+)");
            // Use the MatchEvaluator delegate to format the matched lines
            MatchEvaluator evaluator = match =>
            {
                string method = match.Groups[1].Value;
                string filePath = match.Groups[2].Value.Replace(workingDirectory, "");
                string lineNumber = match.Groups[3].Value;
                // Only format the line as a clickable link if the file exists
                if (File.Exists(filePath))
                {
                    string fileName = Path.GetFileName(filePath);
                    return $"at {method} in <a href=\"{filePath}\" line=\"{lineNumber}\">{fileName}:<b>{lineNumber}</b></a>";
                }
                else
                {
                    return match.Value;
                }
            };
            // Replace the matched lines in the stack trace
            string formattedStackTrace = regex.Replace(stackTrace, evaluator);
            return formattedStackTrace;
        }

        /// <summary>
        /// Determines a category from the script name that called the previous method
        /// </summary>
        /// <returns>Assembly name</returns>
        private static string GetCallingCategory()
        {
            // Get stack trace method
            string path = new StackTrace()?.GetFrame(3)?.GetMethod().DeclaringType.Name;
            if (string.IsNullOrEmpty(path))
            {
                return "NoStacktrace";
            }
            // Return path
            return path;
        }

        /// <summary>
        /// Determines a category from the script name that called the previous method
        /// </summary>
        /// <returns>Assembly name</returns>
        private static void WrapWithCallingLink(StringBuilder builder, int startIndex)
        {
            #if UNITY_EDITOR && UNITY_2021_2_OR_NEWER
            StackTrace stackTrace = new StackTrace(true);
            StackFrame stackFrame = stackTrace.GetFrame(3);
            string callingFileName = stackFrame.GetFileName().Replace('\\', '/');
            int callingFileLine = stackFrame.GetFileLineNumber();
            builder.Insert(startIndex, $"<a href=\"{callingFileName}\" line=\"{callingFileLine}\">");
            builder.Append("</a>");
            #endif
        }

        /// <summary>
        /// Get hex value for each log type
        /// </summary>
        private static void WrapWithLogColor(StringBuilder builder, int startIndex, VLogLevel logType)
        {
            #if UNITY_EDITOR
            string hex;
            switch (logType)
            {
                case VLogLevel.Error:
                    hex = "FF0000";
                    break;
                case VLogLevel.Warning:
                    hex = "FFFF00";
                    break;
                default:
                    hex = "00FF00";
                    break;
            }
            builder.Insert(startIndex, $"<color=#{hex}>");
            builder.Append("</color>");
            #endif
        }
    }
}
