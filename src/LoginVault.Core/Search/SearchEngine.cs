using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.Core.Search;

public enum AccountFilter
{
    All = 0,
    EmailOnly = 1,
}

/// <summary>Services and accounts that match a search. Services are ordered by name.</summary>
public sealed record SearchResult(IReadOnlyList<ServiceEntry> Services, IReadOnlyList<AccountEntry> Accounts)
{
    public static SearchResult Empty { get; } = new(Array.Empty<ServiceEntry>(), Array.Empty<AccountEntry>());
}

/// <summary>
/// In-memory search over the unlocked document. Case-insensitive substring match, multiple
/// words are combined with AND, passwords are never searched and nothing is written to disk.
/// </summary>
public static class SearchEngine
{
    private static readonly char[] Separators = { ' ', '\t', '\r', '\n', '　' };

    public static SearchResult Search(VaultDocument document, string? query, AccountFilter filter)
    {
        ArgumentNullException.ThrowIfNull(document);
        var words = (query ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var servicesById = new Dictionary<Guid, ServiceEntry>();
        foreach (var service in document.Services)
        {
            servicesById.TryAdd(service.Id, service);
        }

        var accounts = new List<AccountEntry>();
        foreach (var account in document.Accounts)
        {
            if (filter == AccountFilter.EmailOnly && !account.IsEmailAccount)
            {
                continue;
            }

            var serviceName = servicesById.TryGetValue(account.ServiceId, out var service) ? service.Name : string.Empty;
            if (words.All(word => Contains(serviceName, word) || MatchesAccount(document, account, word)))
            {
                accounts.Add(account);
            }
        }

        var matchedServiceIds = accounts.Select(a => a.ServiceId).ToHashSet();
        var services = document.Services
            .Where(s => matchedServiceIds.Contains(s.Id) || IncludeEmptyService(document, s, words, filter))
            .OrderBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();

        return new SearchResult(services, accounts);
    }

    // アカウントを持たないサービスは、絞り込みが「すべて」で、サービス名が全語に一致するときだけ表示する
    private static bool IncludeEmptyService(VaultDocument document, ServiceEntry service, string[] words, AccountFilter filter)
    {
        if (filter != AccountFilter.All || document.Accounts.Any(a => a.ServiceId == service.Id))
        {
            return false;
        }

        return words.All(word => Contains(service.Name, word));
    }

    private static bool MatchesAccount(VaultDocument document, AccountEntry account, string word)
    {
        if (Contains(account.Label, word) || Contains(account.Notes, word))
        {
            return true;
        }

        var resolution = VaultQueries.ResolveLoginId(document, account);
        if (!resolution.IsBroken && Contains(resolution.Value, word))
        {
            return true;
        }

        return account.MfaMethods.Any(m => Contains(DisplayText.Of(m.Type), word) || Contains(m.CustomType, word) || Contains(m.Detail, word));
    }

    private static bool Contains(string? text, string word) =>
        !string.IsNullOrEmpty(text) && text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
