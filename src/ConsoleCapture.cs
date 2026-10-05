using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace ValheimConsoleCapture
{
	internal static class ConsoleCapture
	{
		private static readonly Regex RichTextTags = new Regex(
			"</?(?:b|i|u|s|color|size|material|quad|sprite|link|mark|noparse)(?:\\s[^>]*|=[^>]*)?>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly object Gate = new object();

		private static readonly Queue<string> Lines = new Queue<string>();

		private static StreamWriter _writer;

		private static bool _runtimeEnabled;

		private static bool _expectCommandEcho;

		private static bool _shutdown;

		private static bool _inRecord;

		private static bool _fileFault;

		private static bool _loggedFault;

		private static string _lastAutocomplete;

		private static List<string> _scope;

		internal static void Start()
		{
			_shutdown = false;
			_runtimeEnabled = ValheimConsoleCapturePlugin.Enabled == null || ValheimConsoleCapturePlugin.Enabled.Value;
			if (_runtimeEnabled)
			{
				lock (Gate)
				{
					EnsureWriter();
				}
			}
		}

		internal static void Shutdown()
		{
			lock (Gate)
			{
				_shutdown = true;
				_runtimeEnabled = false;
				CloseWriter();
			}
		}

		internal static void SetCaptureEnabled(bool enabled)
		{
			lock (Gate)
			{
				_runtimeEnabled = enabled;
				if (!enabled)
				{
					_lastAutocomplete = null;
					return;
				}

				EnsureWriter();
			}
		}

		internal static void ExpectCommandEcho()
		{
			_expectCommandEcho = true;
		}

		internal static void EndCommandEcho()
		{
			_expectCommandEcho = false;
		}

		internal static bool IsScoping()
		{
			lock (Gate)
			{
				return _scope != null;
			}
		}

		internal static void BeginScope()
		{
			lock (Gate)
			{
				_scope = new List<string>();
			}
		}

		internal static string[] EndScope()
		{
			lock (Gate)
			{
				string[] lines = _scope == null ? Array.Empty<string>() : _scope.ToArray();
				_scope = null;
				return lines;
			}
		}

		internal static void OnTerminalLine(string text)
		{
			bool isCommand = _expectCommandEcho;
			_expectCommandEcho = false;

			if (text == null)
			{
				return;
			}

			bool scoping = IsScoping();
			bool keep = _runtimeEnabled;
			if (keep)
			{
				if (isCommand)
				{
					if (ValheimConsoleCapturePlugin.CaptureCommands != null && !ValheimConsoleCapturePlugin.CaptureCommands.Value)
					{
						keep = false;
					}
				}
				else if (ValheimConsoleCapturePlugin.CaptureOutput != null && !ValheimConsoleCapturePlugin.CaptureOutput.Value)
				{
					keep = false;
				}
			}

			if (!keep && !scoping)
			{
				return;
			}

			foreach (string line in SplitLines(text))
			{
				string processed = ApplyRichText(line);
				if (keep)
				{
					Record(processed);
				}
				else
				{
					RememberScope(processed);
				}
			}
		}

		internal static void OnAutocomplete(string text)
		{
			if (!_runtimeEnabled)
			{
				return;
			}

			if (ValheimConsoleCapturePlugin.CaptureAutocomplete == null || !ValheimConsoleCapturePlugin.CaptureAutocomplete.Value)
			{
				return;
			}

			if (string.IsNullOrEmpty(text))
			{
				_lastAutocomplete = null;
				return;
			}

			string processed = ApplyRichText(text);
			if (processed.Length == 0 || processed == _lastAutocomplete)
			{
				return;
			}

			_lastAutocomplete = processed;
			Record(processed);
		}

		internal static string[] StatusLines()
		{
			int count;
			int max;
			string fileStatus;
			lock (Gate)
			{
				count = Lines.Count;
				max = MaxLines();
				fileStatus = FileStatus();
			}

			return new[]
			{
				"Console capture: " + (_runtimeEnabled ? "Enabled" : "Disabled"),
				"File logging: " + fileStatus,
				"Buffered lines: " + count.ToString(CultureInfo.InvariantCulture) + " / " + max.ToString(CultureInfo.InvariantCulture)
			};
		}

		internal static bool TryCopy(int? count, out string message)
		{
			if (ValheimConsoleCapturePlugin.EnableClipboard != null && !ValheimConsoleCapturePlugin.EnableClipboard.Value)
			{
				message = "Clipboard copying is disabled in config.";
				return false;
			}

			string[] lines = CopyRecent(count);
			if (lines.Length == 0)
			{
				message = "Console buffer is empty.";
				return false;
			}

			try
			{
				GUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, lines);
			}
			catch (Exception ex)
			{
				if (ValheimConsoleCapturePlugin.ModLogger != null)
				{
					ValheimConsoleCapturePlugin.ModLogger.LogError("Clipboard copy failed: " + ex.Message);
				}

				message = "Failed to copy console lines to the clipboard.";
				return false;
			}

			message = lines.Length == 1
				? "Copied 1 console line to clipboard."
				: "Copied " + lines.Length.ToString(CultureInfo.InvariantCulture) + " console lines to clipboard.";
			return true;
		}

		internal static bool TryDump(int? count, out string message)
		{
			string[] lines = CopyRecent(count);
			if (lines.Length == 0)
			{
				message = "Console buffer is empty.";
				return false;
			}

			return TryWriteDump(lines, null, out message);
		}

		internal static bool TryWriteDump(string[] lines, string nameHint, out string message)
		{
			if (lines == null || lines.Length == 0)
			{
				message = "Nothing to dump.";
				return false;
			}

			try
			{
				string directory = ResolveUnderGame(Path.Combine("BepInEx", "ConsoleCaptures"));
				Directory.CreateDirectory(directory);
				string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
				string hint = SanitizeFileHint(nameHint);
				string stem = string.IsNullOrEmpty(hint) ? "console-" + stamp : "console-" + hint + "-" + stamp;
				string path = Path.Combine(directory, stem + ".txt");
				int suffix = 2;
				while (File.Exists(path))
				{
					path = Path.Combine(directory, stem + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".txt");
					suffix++;
				}

				File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
				string countText = lines.Length.ToString(CultureInfo.InvariantCulture);
				message = "Dumped " + countText + " console " + (lines.Length == 1 ? "line" : "lines") + " to " + DisplayPath(path) + ".";
				return true;
			}
			catch (Exception ex)
			{
				if (ValheimConsoleCapturePlugin.ModLogger != null)
				{
					ValheimConsoleCapturePlugin.ModLogger.LogError("Console dump failed: " + ex.Message);
				}

				message = "Failed to write the console dump.";
				return false;
			}
		}

		internal static string ClearBuffer()
		{
			lock (Gate)
			{
				Lines.Clear();
				_lastAutocomplete = null;
			}

			return "Console buffer cleared.";
		}

		private static void Record(string line)
		{
			if (_inRecord)
			{
				return;
			}

			_inRecord = true;
			try
			{
				lock (Gate)
				{
					if (_scope != null)
					{
						_scope.Add(line ?? "");
					}

					if (!_runtimeEnabled || _shutdown)
					{
						return;
					}

					Lines.Enqueue(line ?? "");
					Trim();
					WriteLine(line ?? "");
				}
			}
			finally
			{
				_inRecord = false;
			}
		}

		private static void RememberScope(string line)
		{
			lock (Gate)
			{
				if (_scope != null)
				{
					_scope.Add(line ?? "");
				}
			}
		}

		private static string SanitizeFileHint(string nameHint)
		{
			if (string.IsNullOrWhiteSpace(nameHint))
			{
				return "";
			}

			string token = nameHint.Trim().Split(' ')[0];
			var builder = new StringBuilder();
			for (int i = 0; i < token.Length && builder.Length < 40; i++)
			{
				char c = token[i];
				if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
				{
					builder.Append(char.ToLowerInvariant(c));
				}
			}

			return builder.ToString();
		}

		private static void Trim()
		{
			int max = MaxLines();
			while (Lines.Count > max)
			{
				Lines.Dequeue();
			}
		}

		private static int MaxLines()
		{
			if (ValheimConsoleCapturePlugin.BufferedLines == null)
			{
				return 1000;
			}

			int max = ValheimConsoleCapturePlugin.BufferedLines.Value;
			return max < 1 ? 1 : max;
		}

		private static string[] CopyRecent(int? count)
		{
			lock (Gate)
			{
				Trim();
				string[] all = Lines.ToArray();
				if (all.Length == 0)
				{
					return Array.Empty<string>();
				}

				int take = all.Length;
				if (count.HasValue && count.Value < take)
				{
					take = count.Value;
				}

				if (take == all.Length)
				{
					return all;
				}

				string[] slice = new string[take];
				Array.Copy(all, all.Length - take, slice, 0, take);
				return slice;
			}
		}

		private static void EnsureWriter()
		{
			if (_shutdown || _writer != null)
			{
				return;
			}

			if (ValheimConsoleCapturePlugin.LogToFile == null || !ValheimConsoleCapturePlugin.LogToFile.Value)
			{
				return;
			}

			try
			{
				string path = ResolveUnderGame(ValheimConsoleCapturePlugin.LogPath == null
					? "BepInEx/ValheimConsole.log"
					: ValheimConsoleCapturePlugin.LogPath.Value);
				string directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}

				FileMode mode = ValheimConsoleCapturePlugin.AppendMode == null || ValheimConsoleCapturePlugin.AppendMode.Value
					? FileMode.Append
					: FileMode.Create;
				FileStream stream = new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite);
				_writer = new StreamWriter(stream, new UTF8Encoding(false));
				_fileFault = false;
				string started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
				_writer.WriteLine("============================================================");
				_writer.WriteLine("Valheim Console Capture");
				_writer.WriteLine("Session started: " + started);
				_writer.WriteLine("============================================================");
				_writer.Flush();
			}
			catch (Exception ex)
			{
				NoteFault(ex);
			}
		}

		private static void WriteLine(string line)
		{
			if (ValheimConsoleCapturePlugin.LogToFile == null || !ValheimConsoleCapturePlugin.LogToFile.Value)
			{
				return;
			}

			EnsureWriter();
			if (_writer == null)
			{
				return;
			}

			try
			{
				if (ValheimConsoleCapturePlugin.TimestampLines == null || ValheimConsoleCapturePlugin.TimestampLines.Value)
				{
					string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
					_writer.WriteLine("[" + stamp + "] " + line);
				}
				else
				{
					_writer.WriteLine(line);
				}

				if (ValheimConsoleCapturePlugin.FlushImmediately == null || ValheimConsoleCapturePlugin.FlushImmediately.Value)
				{
					_writer.Flush();
				}
			}
			catch (Exception ex)
			{
				NoteFault(ex);
			}
		}

		private static void CloseWriter()
		{
			if (_writer == null)
			{
				return;
			}

			try
			{
				_writer.Flush();
				_writer.Dispose();
			}
			catch (Exception ex)
			{
				NoteFault(ex);
			}

			_writer = null;
		}

		private static string FileStatus()
		{
			if (ValheimConsoleCapturePlugin.LogToFile == null || !ValheimConsoleCapturePlugin.LogToFile.Value)
			{
				return "Disabled";
			}

			if (_fileFault)
			{
				return "Error";
			}

			return "Enabled";
		}

		private static void NoteFault(Exception ex)
		{
			_fileFault = true;
			if (_loggedFault || ValheimConsoleCapturePlugin.ModLogger == null)
			{
				return;
			}

			_loggedFault = true;
			ValheimConsoleCapturePlugin.ModLogger.LogError("Console log write failed: " + ex);
		}

		private static string ApplyRichText(string line)
		{
			if (string.IsNullOrEmpty(line))
			{
				return line ?? "";
			}

			if (ValheimConsoleCapturePlugin.StripRichText == null || !ValheimConsoleCapturePlugin.StripRichText.Value)
			{
				return line;
			}

			return RichTextTags.Replace(line, "");
		}

		private static IEnumerable<string> SplitLines(string text)
		{
			if (text.Length == 0)
			{
				yield return "";
				yield break;
			}

			string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
			bool trailingBreak = normalized[normalized.Length - 1] == '\n';
			string[] parts = normalized.Split('\n');
			int count = parts.Length;
			if (trailingBreak && count > 1)
			{
				count--;
			}

			for (int i = 0; i < count; i++)
			{
				yield return parts[i];
			}
		}

		private static string ResolveUnderGame(string relativeOrAbsolute)
		{
			if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
			{
				relativeOrAbsolute = "BepInEx/ValheimConsole.log";
			}

			if (Path.IsPathRooted(relativeOrAbsolute))
			{
				return Path.GetFullPath(relativeOrAbsolute);
			}

			string normalized = relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			string bepPrefix = "BepInEx" + Path.DirectorySeparatorChar;
			if (normalized.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
			{
				return Paths.BepInExRootPath;
			}

			if (normalized.StartsWith(bepPrefix, StringComparison.OrdinalIgnoreCase))
			{
				return Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, normalized.Substring(bepPrefix.Length)));
			}

			return Path.GetFullPath(Path.Combine(Paths.GameRootPath, normalized));
		}

		private static string DisplayPath(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath))
			{
				return fullPath;
			}

			string bepRoot = Paths.BepInExRootPath;
			if (!string.IsNullOrEmpty(bepRoot))
			{
				string bepPrefix = bepRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath.StartsWith(bepPrefix, StringComparison.OrdinalIgnoreCase))
				{
					return "BepInEx/" + fullPath.Substring(bepPrefix.Length).Replace('\\', '/');
				}
			}

			string gameRoot = Paths.GameRootPath;
			if (!string.IsNullOrEmpty(gameRoot))
			{
				string gamePrefix = gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath.StartsWith(gamePrefix, StringComparison.OrdinalIgnoreCase))
				{
					return fullPath.Substring(gamePrefix.Length).Replace('\\', '/');
				}
			}

			return fullPath;
		}
	}
}
