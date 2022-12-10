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
            if (!this.plugin.SettingsVisible)
            {
                return;
            }

            bool settingsVisible = this.plugin.SettingsVisible;
            if (ImGui.Begin("Oops, All Female!", ref settingsVisible, ImGuiWindowFlags.AlwaysAutoResize))
            {
                bool shouldChangeOthers = this.plugin.config.ShouldChangeOthers;
                ImGui.Checkbox("Change other players", ref shouldChangeOthers);

                ImGui.Separator();

                ImGui.End();
            }

            this.plugin.SettingsVisible = settingsVisible;
            this.plugin.SaveConfig();
        }
    }
}