#define DEBUG

using Elements.Core;
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
		public override string Version => "1.0.0";
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
				if (PluginSelectPatch.SelectedPlugins.Count == 0 && PluginSelectPatch.PluginsToRemove.Count == 0) return true;
				Debug("Starting session with custom assemblies...");
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
			internal static HashSet<AssemblyTypeRegistry> SelectedPlugins = new();
			internal static HashSet<AssemblyTypeRegistry> PluginsToRemove = new();
			internal static bool UsePatch = false;
			internal static NewWorldDialog CurrentInstance = null;
			internal static List<AssemblyTypeRegistry> GetAssembliesToLoad()
			{
				List<AssemblyTypeRegistry> list = new();
				foreach (var coreAsm in GlobalTypeRegistry.CoreAssemblies)
					list.Add(coreAsm);
				foreach (var pluginAsm in PluginSelectPatch.SelectedPlugins)
					list.Add(pluginAsm);
				foreach (var toRemoveAsm in PluginSelectPatch.PluginsToRemove)
					list.Remove(toRemoveAsm);
				return list;
			}
			static HashSet<AssemblyTypeRegistry> InitLoadedPlugins()
			{
				Msg("Getting loaded plugins...");
				var dataModelAssemblies = GlobalTypeRegistry.DataModelAssemblies;
				HashSet<AssemblyTypeRegistry> plugins = new();
				foreach (var dataModelAssembly in dataModelAssemblies)
				{
					Debug($"{dataModelAssembly.AssemblyName} {dataModelAssembly.AssemblyType} {dataModelAssembly.Assembly.Location}");
					var dirInfo = new DirectoryInfo(Path.GetDirectoryName(dataModelAssembly.Assembly.Location));
					if (dataModelAssembly.AssemblyType != Elements.Data.DataModelAssemblyType.UserspaceCore && (dataModelAssembly.AssemblyType == Elements.Data.DataModelAssemblyType.Optional || dirInfo.Name == "Libraries"))
					{
						Msg("Found plugin: " + dataModelAssembly.AssemblyName);
						plugins.Add(dataModelAssembly);
					}
				}
				return plugins;
			}
			internal static void Reset()
			{
				SelectedPlugins.Clear();
				PluginsToRemove.Clear();
				UsePatch = false;
				CurrentInstance = null;
			}
			static string GetText(string msg)
			{
				return $"<color=hero.green><b>{msg}</b></color>";
			}
			static string GetSelectedPluginsText()
			{
				return GetText($"Selected Plugins: {SelectedPluginsCount()}");
			}
			static bool IsPluginSelected(AssemblyTypeRegistry atr)
			{
				return !PluginsToRemove.Contains(atr) && (SelectedPlugins.Contains(atr) || GlobalTypeRegistry.CoreAssemblies.Contains(atr));
			}
			static int SelectedPluginsCount()
			{
				return _loadedPlugins.Where(IsPluginSelected).Count();
			}
			static void Postfix(NewWorldDialog __instance)
			{
				Debug("BuildUI Postfix");
				_loadedPlugins ??= InitLoadedPlugins();
				Reset();
				__instance.RunInUpdates(3, () => 
				{
					if (__instance.FilterWorldElement() is null) return;
					//__instance.OpenInspectorForTarget();
					
					CurrentInstance = __instance;

					var ui = new UIBuilder(__instance.Slot);
					RadiantUI_Constants.SetupDefaultStyle(ui);
					ui.Style.MinHeight = 32f;
					ui.Style.PreferredHeight = 32f;
					ui.NestInto(__instance.Slot[2][1][0]);
					string text = GetSelectedPluginsText();
					var selectPluginsButton = ui.Button(text);
					selectPluginsButton.LocalPressed += (btn, data) => 
					{
						RectTransform root = null;
						if (__instance.Slot.GetComponentInParents<ModalOverlayManager>() != null)
							root = __instance.Slot.OpenModalOverlay(new float2(0.3f, 0.5f), "Plugins");
						else
						{
							var newSlot = __instance.Slot.GetObjectRoot().AddSlot("Plugins Panel");
							root = RadiantUI_Panel.SetupPanel(newSlot, "Plugins", new float2(300f, 500f)).CurrentRect;
							newSlot.PersistentSelf = false;
							//newSlot.LocalScale *= 0.0005f;
							newSlot.CopyTransform(__instance.Slot);
							Sync<float3> position_Field = newSlot.Position_Field;
							float3 a = newSlot.LocalPosition;
							floatQ q = newSlot.LocalRotation;
							float3 v = float3.Forward;
							float3 v2 = q * v;
							float3 b = v2 * -30f;
							position_Field.TweenTo(a + b, 0.2f);
							__instance.Slot.Destroyed += (destroyable) => 
							{ 
								newSlot.Destroy();
							};
						}
							
						if (root != null)
						{
							ui = new UIBuilder(root);
							RadiantUI_Constants.SetupDefaultStyle(ui);
							ui.Style.MinHeight = 32f;
							ui.Style.PreferredHeight = 32f;
							ui.ScrollArea();
							ui.VerticalLayout(4f);
							ui.FitContent(SizeFit.Disabled, SizeFit.PreferredSize);
							if (_loadedPlugins.Count == 0)
							{
								ui.Text("No plugins are loaded.");
								return;
							}
							foreach (var asm in _loadedPlugins)
							{
								ui.Checkbox(asm.AssemblyName, IsPluginSelected(asm)).State.OnValueChange += field => 
								{ 
									if (__instance.FilterWorldElement() is null) return;
									if (field.Value)
									{
										SelectedPlugins.Add(asm);
										PluginsToRemove.Remove(asm);

									}
									else
									{
										SelectedPlugins.Remove(asm);
										if (GlobalTypeRegistry.CoreAssemblies.Contains(asm))
											PluginsToRemove.Add(asm);
									}
									selectPluginsButton.LabelText = GetSelectedPluginsText();
								};
							}
						}
					};
					__instance.Slot.OnPrepareDestroy += (slot) => 
					{
						if (UsePatch) return;
						Reset();
						//Userspace.UserspaceWorld.RunInUpdates(7, () => 
						//{
							//if (UsePatch) return;
						//});
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