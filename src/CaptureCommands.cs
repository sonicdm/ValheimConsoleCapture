using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimConsoleCapture
{
	internal static class ConsoleCommands
	{
		private const char ChainSeparator = '\u001e';

		internal static void ProtectChain(ref string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			int start = 0;
			while (start < text.Length && char.IsWhiteSpace(text[start]))
			{
				start++;
			}

			const string name = "dumpcommand";
			if (text.Length - start < name.Length)
			{
				return;
			}

			if (!string.Equals(text.Substring(start, name.Length), name, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			int afterName = start + name.Length;
			if (afterName < text.Length && !char.IsWhiteSpace(text[afterName]))
			{
				return;
			}

			int open = text.IndexOf('"', afterName);
			int close = text.LastIndexOf('"');
			if (open < 0 || close <= open)
			{
				return;
			}

			var chars = text.ToCharArray();
			for (int i = open + 1; i < close; i++)
			{
				if (chars[i] == ';')
				{
					chars[i] = ChainSeparator;
				}
			}

			text = new string(chars);
		}

		internal static void Register()
		{
			// Vanilla commands overwrite Terminal.commands on each ScriptEngine reload.
			// Jötunn's command list keeps the first instance and would keep calling the old assembly.
			new Terminal.ConsoleCommand(
				"copyconsole",
				"Copy buffered F5 lines to the clipboard. Optional count: copyconsole 50",
				Copy);

			new Terminal.ConsoleCommand(
				"dumpconsole",
				"Write buffered F5 lines to BepInEx/ConsoleCaptures. Optional count: dumpconsole 100",
				Dump);

			new Terminal.ConsoleCommand(
				"dumpcommand",
				"Run one or more console commands and dump only their output. dumpcommand altbioms    dumpcommand \"pos; seed\"",
				DumpCommand);

			new Terminal.ConsoleCommand(
				"clearconsolebuffer",
				"Clear the in-memory console capture buffer. Does not clear the F5 console or delete the log.",
				Clear);

			new Terminal.ConsoleCommand(
				"consolecapture",
				"Show capture status, or toggle it: consolecapture on / consolecapture off",
				Capture,
				optionsFetcher: Options,
				alwaysRefreshTabOptions: true);
		}

		private static List<string> Options()
		{
			return new List<string> { "on", "off" };
		}

		private static void Copy(Terminal.ConsoleEventArgs args)
		{
			if (!TryParseCount(Rest(args), "copyconsole", out int? count, out string error))
			{
				Reply(args, error);
				return;
			}

			ConsoleCapture.TryCopy(count, out string message);
			Reply(args, message);
		}

		private static void Dump(Terminal.ConsoleEventArgs args)
		{
			if (!TryParseCount(Rest(args), "dumpconsole", out int? count, out string error))
			{
				Reply(args, error);
				return;
			}

			ConsoleCapture.TryDump(count, out string message);
			Reply(args, message);
		}

		private static void DumpCommand(Terminal.ConsoleEventArgs args)
		{
			string[] commands = SplitChain(CommandText(args));
			if (commands.Length == 0)
			{
				Reply(args, "Usage: dumpcommand <command>    or    dumpcommand \"command; command\"");
				return;
			}

			for (int i = 0; i < commands.Length; i++)
			{
				string name = commands[i].Split(' ')[0];
				if (string.Equals(name, "dumpcommand", StringComparison.OrdinalIgnoreCase))
				{
					Reply(args, "dumpcommand cannot run itself.");
					return;
				}
			}

			if (ConsoleCapture.IsScoping())
			{
				Reply(args, "dumpcommand is already running.");
				return;
			}

			Terminal context = args != null ? args.Context : null;
			if (context == null)
			{
				context = global::Console.instance;
			}

			if (context == null)
			{
				Reply(args, "Console is not available.");
				return;
			}

			ConsoleCapture.BeginScope();
			string failed = null;
			try
			{
				for (int i = 0; i < commands.Length; i++)
				{
					// A raw ';' makes Server Devcommands queue the command for the next frame,
					// which is after this dump has already been written.
					context.TryRunCommand(ShieldSegment(commands[i]));
				}
			}
			catch (Exception ex)
			{
				failed = commands.Length == 1 ? commands[0] : "the command chain";
				if (ValheimConsoleCapturePlugin.ModLogger != null)
				{
					ValheimConsoleCapturePlugin.ModLogger.LogError("dumpcommand failed: " + ex.Message);
				}
			}

			string[] lines = ConsoleCapture.EndScope();
			if (lines.Length == 0)
			{
				Reply(args, failed != null ? "Failed to run " + failed + "." : "The command produced no console lines.");
				return;
			}

			ConsoleCapture.TryWriteDump(lines, ChainHint(commands), out string message);
			Reply(args, message);
			if (failed != null)
			{
				Reply(args, "Failed to run " + failed + ".");
			}
		}

		internal static string ShieldSegment(string command)
		{
			if (string.IsNullOrEmpty(command) || command.IndexOf(';') < 0)
			{
				return command;
			}

			return command.Replace(';', ChainSeparator);
		}

		internal static void RestoreChain(Terminal.ConsoleEventArgs args)
		{
			if (args == null)
			{
				return;
			}

			args.FullLine = Restore(args.FullLine);
			args.ArgsAll = Restore(args.ArgsAll);
			if (args.Args == null)
			{
				return;
			}

			for (int i = 0; i < args.Args.Length; i++)
			{
				args.Args[i] = Restore(args.Args[i]);
			}
		}

		private static string Restore(string text)
		{
			if (string.IsNullOrEmpty(text) || text.IndexOf(ChainSeparator) < 0)
			{
				return text;
			}

			return text.Replace(ChainSeparator, ';');
		}

		private static string CommandText(Terminal.ConsoleEventArgs args)
		{
			string text = args == null || args.ArgsAll == null ? "" : args.ArgsAll.Trim();
			if (text.IndexOf(ChainSeparator) >= 0)
			{
				text = text.Replace(ChainSeparator, ';');
			}

			if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
			{
				text = text.Substring(1, text.Length - 2).Trim();
			}

			return text;
		}

		private static string[] SplitChain(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return Array.Empty<string>();
			}

			var parts = new List<string>();
			var current = new System.Text.StringBuilder();
			bool quoted = false;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c == '"')
				{
					quoted = !quoted;
					current.Append(c);
					continue;
				}

				if (c == ';' && !quoted)
				{
					string part = current.ToString().Trim();
					if (part.Length > 0)
					{
						parts.Add(part);
					}

					current.Length = 0;
					continue;
				}

				current.Append(c);
			}

			string last = current.ToString().Trim();
			if (last.Length > 0)
			{
				parts.Add(last);
			}

			return parts.ToArray();
		}

		private static string ChainHint(string[] commands)
		{
			var builder = new System.Text.StringBuilder();
			for (int i = 0; i < commands.Length; i++)
			{
				string token = commands[i].Split(' ')[0];
				var clean = new System.Text.StringBuilder();
				for (int c = 0; c < token.Length && clean.Length < 20; c++)
				{
					char ch = token[c];
					if (char.IsLetterOrDigit(ch))
					{
						clean.Append(char.ToLowerInvariant(ch));
					}
				}

				if (clean.Length == 0)
				{
					continue;
				}

				if (builder.Length > 0)
				{
					if (builder.Length + 1 + clean.Length > 40)
					{
						break;
					}

					builder.Append('-');
				}

				builder.Append(clean);
			}

			return builder.ToString();
		}

		private static void Clear(Terminal.ConsoleEventArgs args)
		{
			Reply(args, ConsoleCapture.ClearBuffer());
		}

		private static void Capture(Terminal.ConsoleEventArgs args)
		{
			string[] rest = Rest(args);
			if (rest.Length > 1)
			{
				Reply(args, "Usage: consolecapture [on|off]");
				return;
			}

			if (rest.Length == 1)
			{
				if (string.Equals(rest[0], "on", StringComparison.OrdinalIgnoreCase))
				{
					ConsoleCapture.SetCaptureEnabled(true);
				}
				else if (string.Equals(rest[0], "off", StringComparison.OrdinalIgnoreCase))
				{
					ConsoleCapture.SetCaptureEnabled(false);
				}
				else
				{
					Reply(args, "Usage: consolecapture [on|off]");
					return;
				}
			}

			string[] status = ConsoleCapture.StatusLines();
			for (int i = 0; i < status.Length; i++)
			{
				Reply(args, status[i]);
			}
		}

		private static string[] Rest(Terminal.ConsoleEventArgs args)
		{
			if (args == null || args.Args == null || args.Args.Length <= 1)
			{
				return Array.Empty<string>();
			}

			string[] rest = new string[args.Args.Length - 1];
			Array.Copy(args.Args, 1, rest, 0, rest.Length);
			return rest;
		}

		private static bool TryParseCount(string[] args, string command, out int? count, out string error)
		{
			count = null;
			error = null;
			if (args == null || args.Length == 0)
			{
				return true;
			}

			if (args.Length == 1 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 1)
			{
				count = value;
				return true;
			}

			error = "Usage: " + command + " [count]";
			return false;
		}

		private static void Reply(Terminal.ConsoleEventArgs args, string message)
		{
			Terminal context = args != null ? args.Context : null;
			if (context != null)
			{
				context.AddString(message);
				return;
			}

			global::Console console = global::Console.instance;
			if (console != null)
			{
				console.AddString(message);
			}
			else if (ValheimConsoleCapturePlugin.ModLogger != null)
			{
				ValheimConsoleCapturePlugin.ModLogger.LogInfo(message);
			}
		}
	}
}
