//#define DEBUG

using Elements.Core;
using Elements.Data;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

#if DEBUG
using ResoniteHotReloadLib;
#endif

namespace PluginSelect
{
	class PluginSelect : ResoniteMod
	{
		public override string Name => "PluginSelect";
		public override string Author => "Nytra";
		public override string Version => "1.0.1";
		public override string Link => "https://github.com/Nytra/ResonitePluginSelect";

		static Harmony harmony;

		[HarmonyPatch(typeof(World), "StartSession")]
		class StartSessionPatch
		{
			static bool Prefix(ref World __result, WorldManager manager, WorldAction init, ushort port = 0, string forceSessionId = null, DataTreeNode load = null, FrooxEngine.Store.Record record = null, bool unsafeMode = false, IEnumerable<AssemblyTypeRegistry> assemblies = null)
			{
				if (assemblies != null) return true;
				if (!PluginSelectPatch.UsePatch) return true;
				PluginSelectPatch.UsePatch = false;
				if (PluginSelectPatch.SelectedOptionalPlugins.Count == 0 && PluginSelectPatch.CorePluginsToRemove.Count == 0) return true;
				Msg("Starting session with custom assemblies...");
				var newAsms = PluginSelectPatch.GetAssembliesToLoad();
				PluginSelectPatch.Reset();
				__result = World.StartSession(manager, init, port, forceSessionId, load, record, unsafeMode, newAsms);
				return false;
			}
		}

		[HarmonyPatch(typeof(NewWorldDialog), "OnStartSession")]
		class Patch2
		{
			static bool Prefix(NewWorldDialog __instance)
			{
				var _port = (SyncRef<TextField>)__instance.GetSyncMember("_port");
				if (!__instance.AutoPort && __instance.PortSelectionEnabled && !ushort.TryParse(_port.Target.TargetString, out var port))
				{
					// getting here means a world will not be opened
				}
				else
				{
					if (__instance == PluginSelectPatch.CurrentInstance)
						PluginSelectPatch.UsePatch = true;
					PluginSelectPatch.CurrentInstance = null;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(NewWorldDialog), "BuildUI")]
		class PluginSelectPatch
		{
			static HashSet<AssemblyTypeRegistry> _loadedPlugins = null;
			internal static HashSet<AssemblyTypeRegistry> SelectedOptionalPlugins = new();
			internal static HashSet<AssemblyTypeRegistry> CorePluginsToRemove = new();
			internal static bool UsePatch = false;
			internal static NewWorldDialog CurrentInstance = null;
			internal static List<AssemblyTypeRegistry> GetAssembliesToLoad()
			{
				// Using a List because that's what FrooxEngine normally uses

				List<AssemblyTypeRegistry> list = new();
				foreach (var coreAsm in GlobalTypeRegistry.CoreAssemblies) // default assemblies
					list.Add(coreAsm);
				foreach (var toRemoveAsm in PluginSelectPatch.CorePluginsToRemove) // core assembly plugins that the user doesn't want for this session (e.g. Project Obsidian)
					list.Remove(toRemoveAsm);
				foreach (var optionalPluginAsm in PluginSelectPatch.SelectedOptionalPlugins) // optional plugins
					list.Add(optionalPluginAsm);
				return list;
			}
			static HashSet<AssemblyTypeRegistry> InitLoadedPlugins()
			{
				// A plugin can be:
				// A data model assembly marked as Optional
				// A data model assembly marked as Core and located in the Libraries folder (e.g. Project Obsidian)
				//     - Obsidian has its own way to toggle its presence in the core assemblies list, but it's also good to support it in this mod

				Msg("Getting loaded plugins...");
				var dataModelAssemblies = GlobalTypeRegistry.DataModelAssemblies;
				HashSet<AssemblyTypeRegistry> plugins = new();
				foreach (var dataModelAssembly in dataModelAssemblies)
				{
					Debug($"{dataModelAssembly.AssemblyName} {dataModelAssembly.AssemblyType} {dataModelAssembly.Assembly.Location}");
					var dirInfo = new DirectoryInfo(Path.GetDirectoryName(dataModelAssembly.Assembly.Location));
					if (dataModelAssembly.AssemblyType != DataModelAssemblyType.UserspaceCore && (dataModelAssembly.AssemblyType == DataModelAssemblyType.Optional || dirInfo.Name == "Libraries"))
					{
						Msg("Found plugin: " + dataModelAssembly.AssemblyName);
						plugins.Add(dataModelAssembly);
					}
				}
				return plugins;
			}
			internal static void Reset()
			{
				SelectedOptionalPlugins.Clear();
				CorePluginsToRemove.Clear();
				UsePatch = false;
				CurrentInstance = null;
			}
			static string GetSelectedPluginsText()
			{
				var str = $"<b>Selected Plugins: {SelectedPluginsCount()}/{_loadedPlugins.Count}</b>";
				str = $"<color=hero.green>{str}</color>";
				return str;
			}
			static bool IsPluginSelected(AssemblyTypeRegistry atr)
			{
				return !CorePluginsToRemove.Contains(atr) && (SelectedOptionalPlugins.Contains(atr) || GlobalTypeRegistry.CoreAssemblies.Contains(atr));
			}
			static int SelectedPluginsCount()
			{
				return _loadedPlugins.Where(IsPluginSelected).Count();
			}
			static void Postfix(NewWorldDialog __instance)
			{
				_loadedPlugins ??= InitLoadedPlugins();

				__instance.RunInUpdates(3, () => 
				{
					if (__instance.FilterWorldElement() is null) return;

#if DEBUG
					//__instance.OpenInspectorForTarget();
#endif

					var mainUi = new UIBuilder(__instance.Slot);
					RadiantUI_Constants.SetupDefaultStyle(mainUi);
					mainUi.Style.MinHeight = 32f;
					mainUi.Style.PreferredHeight = 32f;
					mainUi.NestInto(__instance.Slot[2][1][0]); // Could throw errors if the slot layout of the NewWorldDialog ever changes
					if (_loadedPlugins.Count == 0)
					{
						mainUi.Text("No plugins are loaded.");
						return;
					}
					Reset();
					CurrentInstance = __instance;
					var selectPluginsButton = mainUi.Button(GetSelectedPluginsText());
					selectPluginsButton.LocalPressed += (btn, data) => 
					{
						RectTransform root = __instance.Slot.OpenModalOverlay(new float2(0.3f, 0.5f), "Plugins");
						UIBuilder pluginsUi = null;
						if (root is null)
						{
							// create physical panel as a backup
							var newSlot = __instance.Slot.GetObjectRoot().AddSlot("Plugin Select Panel");
							pluginsUi = RadiantUI_Panel.SetupPanel(newSlot, "Plugins", new float2(300f, 500f));
							root = pluginsUi.CurrentRect;
							newSlot.PersistentSelf = false;
							Sync<float3> position_Field = newSlot.Position_Field;
							float3 a = newSlot.LocalPosition;
							floatQ q = newSlot.LocalRotation;
							float3 v = float3.Forward;
							float3 v2 = q * v;
							float3 b = v2 * -60f;
							position_Field.TweenTo(a + b, 0.2f);
							__instance.Slot.OnPrepareDestroy += (slot) => 
							{ 
								newSlot.FilterWorldElement()?.Destroy();
							};
						}

						pluginsUi ??= new UIBuilder(root);
						RadiantUI_Constants.SetupDefaultStyle(pluginsUi);
						pluginsUi.Style.MinHeight = 32f;
						pluginsUi.Style.PreferredHeight = 32f;
						pluginsUi.ScrollArea();
						pluginsUi.VerticalLayout(4f);
						pluginsUi.FitContent(SizeFit.Disabled, SizeFit.PreferredSize);
						if (_loadedPlugins.Count == 0)
						{
							pluginsUi.Text("No plugins are loaded.");
							return;
						}
						foreach (var asm in _loadedPlugins)
						{
							pluginsUi.Checkbox(asm.AssemblyName, IsPluginSelected(asm)).State.OnValueChange += field =>
							{
								if (__instance.Slot.FilterWorldElement() is null) return;
								if (field.Value)
								{
									if (!GlobalTypeRegistry.CoreAssemblies.Contains(asm))
										SelectedOptionalPlugins.Add(asm);
									CorePluginsToRemove.Remove(asm);

								}
								else
								{
									SelectedOptionalPlugins.Remove(asm);
									if (GlobalTypeRegistry.CoreAssemblies.Contains(asm))
										CorePluginsToRemove.Add(asm);
								}
								selectPluginsButton.LabelText = GetSelectedPluginsText();
							};
						}
					};
					__instance.Slot.OnPrepareDestroy += (slot) => 
					{
						// UsePatch being true here means a session is about to be started, so the state needs to be kept until that happens
						if (UsePatch) return;
						Reset();
					};
				});
			}
		}

		public override void OnEngineInit()
		{
#if DEBUG
			HotReloader.RegisterForHotReload(this);
#endif
			PatchStuff();
		}

#if DEBUG
		static void BeforeHotReload()
		{
			harmony.UnpatchAll(harmony.Id);
		}

		static void OnHotReload(ResoniteMod modInstance)
		{
			PatchStuff();
		}
#endif

		static void PatchStuff()
		{
#if DEBUG
			//UniLog.FlushEveryMessage = true;
#endif
			harmony = new Harmony("owo.Nytra.PluginSelect");
			harmony.PatchAll();
		}
	}
}