using AFKAssist.Data.Enums;

namespace AFKAssist.Data.ViewModels;

public sealed record Notice(string Message, LogKind Kind);
