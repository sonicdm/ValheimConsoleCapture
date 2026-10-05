using HarmonyLib;
using TMPro;

namespace ValheimConsoleCapture
{
	// The title and PlatformUserID overloads both end in AddString(string).
	// Console.InputText echoes the submitted line through that method, then runs the command.
	[HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), new[] { typeof(string) })]
	internal static class TerminalAddStringPatch
	{
		private static void Postfix(Terminal __instance, string text)
		{
			if (!(__instance is global::Console))
			{
				return;
			}

			ConsoleCapture.OnTerminalLine(text);
		}
	}

	[HarmonyPatch(typeof(Terminal), "InputText")]
	internal static class TerminalInputTextPatch
	{
		private static void Prefix(Terminal __instance)
		{
			if (__instance is global::Console)
			{
				ConsoleCapture.ExpectCommandEcho();
			}
		}

		private static void Postfix()
		{
			ConsoleCapture.EndCommandEcho();
		}
	}

	// Server Devcommands splits TryRunCommand on every ';', including inside quotes.
	// Keep a quoted dumpcommand chain intact so dumpcommand "pos; seed" stays one command.
	[HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
	internal static class TerminalTryRunCommandPatch
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(ref string text)
		{
			ConsoleCommands.ProtectChain(ref text);
		}
	}

	// Restore shielded semicolons before the command reads its arguments.
	[HarmonyPatch(typeof(Terminal.ConsoleCommand), nameof(Terminal.ConsoleCommand.RunAction))]
	internal static class TerminalRunActionPatch
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(Terminal.ConsoleEventArgs args)
		{
			ConsoleCommands.RestoreChain(args);
		}
	}

	// Suggestions are drawn into Terminal.m_search. They never become console lines.
	[HarmonyPatch(typeof(Terminal), "updateSearch")]
	internal static class TerminalAutocompletePatch
	{
		private static void Postfix(Terminal __instance)
		{
			if (!(__instance is global::Console))
			{
				return;
			}

			TMP_Text search = __instance.m_search;
			if (search == null)
			{
				return;
			}

			ConsoleCapture.OnAutocomplete(search.text);
		}
	}
}
