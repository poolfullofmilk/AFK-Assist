using AFKAssist.Data.Enums;

namespace AFKAssist.Data.ViewModels;

public readonly record struct SimulationAction(
    string LogMessage,
    ActionKind Kind,
    Func<CancellationToken, Task> RunAsync
);
