namespace Jarvis.Core;

public sealed class JarvisController(IAssistantResponder responder)
{
    public AssistantViewState State { get; private set; } = AssistantViewState.Initial;

    public event EventHandler<AssistantViewState>? StateChanged;

    public async Task SubmitAsync(string request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            SetState(new AssistantViewState(
                AssistantPhase.Failed,
                "A request is required",
                Error: "Enter a request before starting the tracer."));
            return;
        }

        var normalizedRequest = request.Trim();
        SetState(new AssistantViewState(
            AssistantPhase.Active,
            "Working",
            Request: normalizedRequest));

        try
        {
            var response = await responder
                .RespondAsync(normalizedRequest, cancellationToken)
                .ConfigureAwait(false);

            SetState(new AssistantViewState(
                AssistantPhase.Completed,
                "Completed",
                Request: normalizedRequest,
                Response: response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(new AssistantViewState(
                AssistantPhase.Idle,
                "Cancelled",
                Request: normalizedRequest));
        }
        catch (Exception exception)
        {
            SetState(new AssistantViewState(
                AssistantPhase.Failed,
                "Failed",
                Request: normalizedRequest,
                Error: exception.Message));
        }
    }

    public void Reset() => SetState(AssistantViewState.Initial);

    private void SetState(AssistantViewState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
