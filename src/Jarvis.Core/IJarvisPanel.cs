namespace Jarvis.Core;

public interface IJarvisPanel
{
    bool IsPanelVisible { get; }

    void ShowPanel();

    void HidePanel();
}
