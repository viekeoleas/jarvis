namespace Jarvis.Core;

public sealed class PanelController(IJarvisPanel panel)
{
    public void TogglePanel()
    {
        if (panel.IsPanelVisible)
        {
            panel.HidePanel();
            return;
        }

        panel.ShowPanel();
    }
}
