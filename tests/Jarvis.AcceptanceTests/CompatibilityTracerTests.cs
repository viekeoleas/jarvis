using Jarvis.Core;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class CompatibilityTracerTests
{
    [Fact]
    public async Task Request_crosses_the_application_seam_and_exposes_visible_states()
    {
        var observed = new List<AssistantViewState>();
        var controller = new JarvisController(new StubResponder("At your service."));
        controller.StateChanged += (_, state) => observed.Add(state);

        await controller.SubmitAsync("System check", TestContext.Current.CancellationToken);

        Assert.Collection(
            observed,
            active =>
            {
                Assert.Equal(AssistantPhase.Active, active.Phase);
                Assert.Equal("System check", active.Request);
            },
            completed =>
            {
                Assert.Equal(AssistantPhase.Completed, completed.Phase);
                Assert.Equal("At your service.", completed.Response);
            });
    }

    [Fact]
    public async Task Failure_is_presented_as_a_user_visible_state()
    {
        var controller = new JarvisController(new FailingResponder());

        await controller.SubmitAsync("Fail safely", TestContext.Current.CancellationToken);

        Assert.Equal(AssistantPhase.Failed, controller.State.Phase);
        Assert.Equal("Compatibility failure", controller.State.Error);
    }

    [Fact]
    public async Task Blank_request_never_reaches_the_responder()
    {
        var responder = new RecordingResponder();
        var controller = new JarvisController(responder);

        await controller.SubmitAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(AssistantPhase.Failed, controller.State.Phase);
        Assert.False(responder.WasCalled);
    }

    [Fact]
    public void Activation_crosses_the_Windows_capability_seam()
    {
        var panel = new RecordingPanel();
        var controller = new PanelController(panel);

        controller.TogglePanel();
        controller.TogglePanel();

        Assert.Equal(1, panel.ShowCount);
        Assert.Equal(1, panel.HideCount);
        Assert.False(panel.IsPanelVisible);
    }

    private sealed class StubResponder(string response) : IAssistantResponder
    {
        public Task<string> RespondAsync(string request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class FailingResponder : IAssistantResponder
    {
        public Task<string> RespondAsync(string request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Compatibility failure");
    }

    private sealed class RecordingResponder : IAssistantResponder
    {
        public bool WasCalled { get; private set; }

        public Task<string> RespondAsync(string request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(string.Empty);
        }
    }

    private sealed class RecordingPanel : IJarvisPanel
    {
        public bool IsPanelVisible { get; private set; }

        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public void ShowPanel()
        {
            IsPanelVisible = true;
            ShowCount++;
        }

        public void HidePanel()
        {
            IsPanelVisible = false;
            HideCount++;
        }
    }
}
