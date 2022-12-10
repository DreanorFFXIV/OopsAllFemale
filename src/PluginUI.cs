using ImGuiNET;

namespace OopsAllFemale
{
    public class PluginUI
    {
        private readonly Plugin plugin;

        public PluginUI(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void Draw()
        {
            if (!plugin.SettingsVisible)
            {
                return;
            }

            bool settingsVisible = plugin.SettingsVisible;
            if (ImGui.Begin("Oops, All Female!", ref settingsVisible, ImGuiWindowFlags.AlwaysAutoResize))
            {
                bool shouldChangeOthers = plugin.config.ShouldChangeOthers;
                ImGui.Checkbox("Change other players", ref shouldChangeOthers);
                if (shouldChangeOthers)
                {
                    plugin.UnsavedConfigChanges = true;
                }

                ImGui.End();
            }

            plugin.SettingsVisible = settingsVisible;
            plugin.SaveConfig();
        }
    }
}