using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace ValheimConsoleCapture
{
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
	public sealed class ValheimConsoleCapturePlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.sonicdm.valheim.valheimconsolecapture";

		public const string PluginName = "ValheimConsoleCapture";

		public const string PluginVersion = "1.0.0";

		internal static ManualLogSource ModLogger;

		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> LogToFile;

		internal static ConfigEntry<string> LogPath;

		internal static ConfigEntry<bool> AppendMode;

		internal static ConfigEntry<bool> TimestampLines;

		internal static ConfigEntry<bool> FlushImmediately;

		internal static ConfigEntry<int> BufferedLines;

		internal static ConfigEntry<bool> EnableClipboard;

		internal static ConfigEntry<bool> CaptureCommands;

		internal static ConfigEntry<bool> CaptureOutput;

		internal static ConfigEntry<bool> StripRichText;

		internal static ConfigEntry<bool> CaptureAutocomplete;

		private Harmony _harmony;

		private void Awake()
		{
			ModLogger = Logger;

			Enabled = Config.Bind("General", "Enabled", true, "Capture F5 console lines when the game starts. consolecapture on/off overrides this until the next launch.");

			LogToFile = Config.Bind("File", "LogToFile", true, "Append captured lines to the console log file.");
			LogPath = Config.Bind(
				"File",
				"LogPath",
				"BepInEx/ValheimConsole.log",
				"Log file path. A BepInEx/... path is inside the active BepInEx folder (the Gale profile when using a mod manager). Other relative paths start at the game folder. Applied when the file is opened.");
			AppendMode = Config.Bind(
				"File",
				"AppendMode",
				true,
				"Append to an existing log. When false, the file is replaced at the start of the session. Applied when the file is opened.");
			TimestampLines = Config.Bind("File", "TimestampLines", true, "Prefix each logged line with the local time. Clipboard and dump files stay unstamped.");
			FlushImmediately = Config.Bind("File", "FlushImmediately", true, "Flush the log after every line so a crash still keeps the output.");

			BufferedLines = Config.Bind(
				"Buffer",
				"BufferedLines",
				1000,
				new ConfigDescription(
					"How many recent console lines to keep in memory for copyconsole and dumpconsole.",
					new AcceptableValueRange<int>(1, 100000)));

			EnableClipboard = Config.Bind("Clipboard", "EnableClipboard", true, "Allow copyconsole to write to the system clipboard.");

			CaptureCommands = Config.Bind("Filtering", "CaptureCommands", true, "Record the command line Valheim echoes when you press Enter.");
			CaptureOutput = Config.Bind("Filtering", "CaptureOutput", true, "Record command results and other F5 console lines.");
			StripRichText = Config.Bind(
				"Filtering",
				"StripRichText",
				false,
				"Remove Unity rich-text tags such as <color=yellow> from the log, clipboard, and dumps.");
			CaptureAutocomplete = Config.Bind(
				"Filtering",
				"CaptureAutocomplete",
				true,
				"Record the live suggestion bar. Tab completion writes that text field directly and does not call Terminal.AddString.");

			ConsoleCapture.Start();

			_harmony = new Harmony(PluginGuid);
			try
			{
				_harmony.PatchAll(typeof(ValheimConsoleCapturePlugin).Assembly);
				ConsoleCommands.Register();
				Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
			}
			catch (Exception ex)
			{
				ConsoleCapture.Shutdown();
				Logger.LogError(PluginName + " failed to start: " + ex);
			}
		}

		private void OnDestroy()
		{
			// ScriptEngine reload calls this. UnpatchSelf uses this plugin's Harmony id.
			if (_harmony != null)
			{
				try
				{
					_harmony.UnpatchSelf();
				}
				catch (Exception ex)
				{
					Logger.LogWarning("Unpatch failed: " + ex.Message);
				}

				_harmony = null;
			}

			ConsoleCapture.Shutdown();
		}
	}
}
