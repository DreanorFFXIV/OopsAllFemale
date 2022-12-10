#undef DEBUG

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;

namespace OopsAllFemale
{
    public class Plugin : IDalamudPlugin
    {
        private const uint FLAG_INVIS = (1 << 1) | (1 << 11);
        private const uint CHARA_WINDOW_ACTOR_ID = 0xE0000000;
        private const int OFFSET_RENDER_TOGGLE = 0x104;

        private static readonly short[,] RACE_STARTER_GEAR_ID_MAP =
        {
            {84, 85}, // Hyur
            {86, 87}, // Elezen
            {92, 93}, // Lalafell
            {88, 89}, // Miqo
            {90, 91}, // Roe
            {257, 258}, // Au Ra
            {597, -1}, // Hrothgar
            {-1, 581}, // Viera
        };

        private static readonly short[] RACE_STARTER_GEAR_IDS;

        public string Name => "Oops, All Females!";

        [PluginService] private DalamudPluginInterface pluginInterface { get; set; }
        [PluginService] private ObjectTable objectTable { get; set; }
        [PluginService] private CommandManager commandManager { get; set; }
        [PluginService] private SigScanner sigScanner { get; set; }
        [PluginService] private ClientState clientState { get; set; }

        public Configuration config { get; private set; }

        private bool unsavedConfigChanges;
        private PluginUI ui;
        public bool SettingsVisible = false;

        private delegate IntPtr CharacterIsMount(IntPtr actor);

        private delegate IntPtr CharacterInitialize(IntPtr actorPtr, IntPtr customizeDataPtr);

        private Hook<CharacterIsMount> charaMountedHook;
        private Hook<CharacterInitialize> charaInitHook;

        private IntPtr lastActor;
        private bool lastWasPlayer;
        private bool lastWasModified;

        static Plugin()
        {
            var list = new List<short>();
            foreach (short id in RACE_STARTER_GEAR_ID_MAP)
            {
                if (id != -1)
                {
                    list.Add(id);
                }
            }

            RACE_STARTER_GEAR_IDS = list.ToArray();
        }

        public Plugin()
        {
            this.config = (Configuration) this.pluginInterface.GetPluginConfig() ?? new Configuration();
            this.config.Initialize(pluginInterface);

            this.ui = new PluginUI(this);

            this.pluginInterface.UiBuilder.Draw += this.ui.Draw;
            this.pluginInterface.UiBuilder.OpenConfigUi += OpenSettingsMenu;

            this.commandManager.AddHandler(
                "/poafem",
                new CommandInfo(this.OpenSettingsMenuCommand)
                {
                    HelpMessage = "Opens the Oops, All Female! settings menu.",
                    ShowInHelp = true
                }
            );

            var charaIsMountAddr =
                this.sigScanner.ScanText("40 53 48 83 EC 20 48 8B 01 48 8B D9 FF 50 10 83 F8 08 75 08");
            PluginLog.Log($"Found IsMount address: {charaIsMountAddr.ToInt64():X}");
            this.charaMountedHook ??=
                new Hook<CharacterIsMount>(charaIsMountAddr, CharacterIsMountDetour);
            this.charaMountedHook.Enable();

            var charaInitAddr = this.sigScanner.ScanText(
                "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 48 8B F9 48 8B EA 48 81 C1 ?? ?? ?? ?? E8 ?? ?? ?? ??");
            PluginLog.Log($"Found Initialize address: {charaInitAddr.ToInt64():X}");
            this.charaInitHook ??=
                new Hook<CharacterInitialize>(charaInitAddr, CharacterInitializeDetour);
            this.charaInitHook.Enable();

            // Trigger an initial refresh of all players
            RefreshAllPlayers();
        }

        private IntPtr CharacterIsMountDetour(IntPtr actorPtr)
        {
            // TODO: use native FFXIVClientStructs unsafe methods?
            if (Marshal.ReadByte(actorPtr + 0x8C) == (byte) ObjectKind.Player)
            {
                lastActor = actorPtr;
                lastWasPlayer = true;
            }
            else
            {
                lastWasPlayer = false;
            }

            return charaMountedHook.Original(actorPtr);
        }

        private IntPtr CharacterInitializeDetour(IntPtr drawObjectBase, IntPtr customizeDataPtr)
        {
            if (lastWasPlayer)
            {
                lastWasModified = false;
                var actor = this.objectTable.CreateObjectReference(lastActor);
                if (actor != null
                    && this.clientState.LocalPlayer != null
                    //&& actor.ObjectId != this.clientState.LocalPlayer.ObjectId //QQ muh morals whine more
                    && this.config.ShouldChangeOthers)
                {
                    this.ChangeGender(customizeDataPtr);
                }
            }

            return charaInitHook.Original(drawObjectBase, customizeDataPtr);
        }

        private void ChangeGender(IntPtr customizeDataPtr)
        {
            var customData = Marshal.PtrToStructure<CharaCustomizeData>(customizeDataPtr);

            if (customData.Gender != 1)
            {
                
                if (customData.Race == Race.HROTHGAR)
                {
                    customData.Race = Race.MIQOTE;
                    customData.HairStyle = (byte) (customData.HairStyle % RaceMappings.RaceHairs[Race.MIQOTE] + 1);
                }
               
                // Modify the tribe accordingly
                customData.Tribe = (byte) ((byte) customData.Race * 2 - customData.Tribe % 2);
                
                //female
                customData.Gender = 1;

                // Constrain face type to 0-3 so we don't decapitate the character
                customData.FaceType %= 4;

                // Constrain body type to 0-1 so we don't crash the game
                customData.ModelType %= 2;

                Marshal.StructureToPtr(customData, customizeDataPtr, true);

                lastWasModified = true;
            }
        }
        public bool SaveConfig()
        {
            if (unsavedConfigChanges)
            {
                config.Save();
                unsavedConfigChanges = false;
                RefreshAllPlayers();
                return true;
            }

            return false;
        }

        public void ToggleChangeOthers(bool changeOthers)
        {
            if (config.ShouldChangeOthers == changeOthers)
            {
                return;
            }

            config.ShouldChangeOthers = changeOthers;
            unsavedConfigChanges = true;
        }
        
        public async void RefreshAllPlayers()
        {
            // Workaround to prevent literally genociding the actor table if we load at the same time as Dalamud + Dalamud is loading while ingame
            await Task.Delay(100); // LMFAOOOOOOOOOOOOOOOOOOO
            var localPlayer = this.clientState.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            for (var i = 0; i < this.objectTable.Length; i++)
            {
                var actor = this.objectTable[i];

                if (actor != null && actor.ObjectKind == ObjectKind.Player)
                {
                    RerenderActor(actor);
                }
            }
        }

        private async void RerenderActor(GameObject actor)
        {
            try
            {
                var addrRenderToggle = actor.Address + OFFSET_RENDER_TOGGLE;
                var val = Marshal.ReadInt32(addrRenderToggle);

                // Trigger a rerender
                val |= (int) FLAG_INVIS;
                Marshal.WriteInt32(addrRenderToggle, val);
                await Task.Delay(100);
                val &= ~(int) FLAG_INVIS;
                Marshal.WriteInt32(addrRenderToggle, val);
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex.ToString());
            }
        }

        public void OpenSettingsMenuCommand(string command, string args)
        {
            OpenSettingsMenu();
        }

        private void OpenSettingsMenu()
        {
            this.SettingsVisible = true;
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.pluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsMenu;
            this.pluginInterface.UiBuilder.Draw -= this.ui.Draw;
            this.SaveConfig();

            this.charaMountedHook.Disable();
            this.charaInitHook.Disable();

            this.charaMountedHook.Dispose();
            this.charaInitHook.Dispose();

            // Refresh all players again
            RefreshAllPlayers();

            this.commandManager.RemoveHandler("/poafem");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
