using System.Threading;
using System.Windows;
using Jarvis.Core;

namespace Jarvis.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\Viekeoleas.Jarvis.Mvp";

    private Mutex? _singleInstanceMutex;
    private MainWindow? _window;
    private TrayHost? _trayHost;
    private GlobalHotKey? _hotKey;
    private PanelController? _panelController;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        var controller = new JarvisController(new CompatibilityResponder());
        _window = new MainWindow(controller);
        _panelController = new PanelController(_window);
        _trayHost = new TrayHost(_window, _panelController.TogglePanel, ExitApplication);
        _hotKey = new GlobalHotKey(_window, _panelController.TogglePanel);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _trayHost?.Dispose();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ExitApplication()
    {
        _window?.AllowClose();
        Shutdown();
    }
}
