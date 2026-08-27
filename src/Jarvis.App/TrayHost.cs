using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Jarvis.App;

internal sealed class TrayHost : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayHost(MainWindow window, Action toggleWindow, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide Jarvis", image: null, (_, _) =>
            window.Dispatcher.Invoke(toggleWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", image: null, (_, _) => exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Jarvis — Ready",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => window.Dispatcher.Invoke(toggleWindow);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
