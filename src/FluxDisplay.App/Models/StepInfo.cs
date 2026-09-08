namespace FluxDisplay.App.Models;

public sealed record StepInfo(int Index, string Title, string Description, bool IsCompleted, bool IsCurrent)
{
    public string DisplayIndex => (Index + 1).ToString();
}
