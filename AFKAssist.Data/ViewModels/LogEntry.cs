using AFKAssist.Data.Enums;

namespace AFKAssist.Data.ViewModels;

public sealed record LogEntry(string Time, string Message, LogKind Kind);
