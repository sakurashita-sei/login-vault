namespace LoginVault.App.ViewModels;

public enum ServiceListItemKind
{
    All,
    EmailOnly,
    Service,
}

/// <summary>One row of the left pane: "すべて", "メールのみ" or a service with its account count.</summary>
public sealed record ServiceListItem(ServiceListItemKind Kind, string Name, Guid? ServiceId, int Count)
{
    public bool IsService => Kind == ServiceListItemKind.Service;

    public string CountText => Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string AutomationName => $"{Name}（{Count}件）";

    public override string ToString() => $"ServiceListItem({Kind}, {ServiceId})";
}
