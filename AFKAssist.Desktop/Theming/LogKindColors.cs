using AFKAssist.Data.Enums;
using MudBlazor;

namespace AFKAssist.Desktop.Theming;

public static class LogKindColors
{
    public static string TextClassOf(LogKind kind)
    {
        return kind switch
        {
            LogKind.Input => "mud-text-disabled",
            LogKind.Success => "mud-success-text",
            LogKind.Warning => "mud-warning-text",
            LogKind.Error => "mud-error-text",
            _ => "mud-text-secondary",
        };
    }

    public static Severity SeverityOf(LogKind kind)
    {
        return kind == LogKind.Error ? Severity.Error : Severity.Warning;
    }
}
