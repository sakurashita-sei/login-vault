using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoginVault.Core.Model;
using LoginVault.Core.Operations;

namespace LoginVault.App.ViewModels;

/// <summary>Length presets of the generator dialog; Custom uses the typed number.</summary>
public enum PasswordLengthChoice
{
    Eight,
    Twelve,
    Sixteen,
    Custom,
}

/// <summary>One symbol with its check box state and the label shown next to it (for example "-（ハイフン）").</summary>
public sealed partial class SymbolOption : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    public SymbolOption(char symbol, string name)
    {
        Symbol = symbol;
        Label = $"{symbol}（{name}）";
    }

    public char Symbol { get; }

    public string Label { get; }

    public override string ToString() => $"SymbolOption({Symbol})";
}

/// <summary>
/// Options and the current candidate of the password generator dialog. Any change regenerates the candidate at once.
/// The candidate never leaves the dialog except through <see cref="Candidate"/> when the user accepts it.
/// </summary>
public sealed partial class PasswordGeneratorViewModel : ObservableObject
{
    private static readonly (char Symbol, string Name)[] SymbolCatalog =
    {
        ('-', "ハイフン"), ('_', "アンダーバー"), ('/', "スラッシュ"), ('*', "アスタリスク"), ('+', "プラス"), ('.', "ドット"), (',', "カンマ"),
        ('!', "エクスクラメーション"), ('#', "シャープ"), ('$', "ドル"), ('%', "パーセント"), ('&', "アンド"), ('(', "左かっこ"), (')', "右かっこ"),
        ('~', "チルダ"), ('|', "パイプ"), ('@', "アットマーク"), ('^', "キャレット"), ('=', "イコール"),
    };

    private readonly Func<int, int>? _random;
    private readonly bool _initialized;

    [ObservableProperty]
    private PasswordLengthChoice _lengthChoice;

    [ObservableProperty]
    private string _customLength = string.Empty;

    [ObservableProperty]
    private bool _useUppercase;

    [ObservableProperty]
    private bool _useLowercase;

    [ObservableProperty]
    private bool _useDigits;

    [ObservableProperty]
    private bool _useSymbols;

    [ObservableProperty]
    private bool _excludeAmbiguous = true;

    [ObservableProperty]
    private bool _noRepeatedCharacters = true;

    [ObservableProperty]
    private string _candidate = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public PasswordGeneratorViewModel(PasswordGeneratorOptions options, Func<int, int>? random = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _random = random;
        LengthChoice = options.Length switch
        {
            8 => PasswordLengthChoice.Eight,
            12 => PasswordLengthChoice.Twelve,
            16 => PasswordLengthChoice.Sixteen,
            _ => PasswordLengthChoice.Custom,
        };
        CustomLength = options.Length.ToString(CultureInfo.InvariantCulture);
        UseUppercase = options.UseUppercase;
        UseLowercase = options.UseLowercase;
        UseDigits = options.UseDigits;
        UseSymbols = options.UseSymbols;
        ExcludeAmbiguous = options.ExcludeAmbiguous;
        NoRepeatedCharacters = options.NoRepeatedCharacters;
        var enabled = options.EnabledSymbols ?? string.Empty;
        Symbols = SymbolCatalog.Select(s => new SymbolOption(s.Symbol, s.Name) { IsEnabled = enabled.Contains(s.Symbol) }).ToList();
        foreach (var symbol in Symbols)
        {
            symbol.PropertyChanged += (_, _) =>
            {
                if (!_suppressRegenerate)
                {
                    UpdateSymbolProperties();
                    Regenerate();
                }
            };
        }

        _initialized = true;
        Regenerate();
    }

    private bool _suppressRegenerate;

    public IReadOnlyList<SymbolOption> Symbols { get; }

    /// <summary>True when every available symbol is enabled.</summary>
    public bool AreAllSymbolsSelected => Symbols.Count > 0 && Symbols.All(s => s.IsEnabled);

    /// <summary>Label of the button that selects or deselects all symbols: "すべて解除" when all are selected, "すべて選択" otherwise.</summary>
    public string ToggleAllSymbolsButtonText => AreAllSymbolsSelected ? "すべて解除" : "すべて選択";

    /// <summary>Toggles selection of all symbols: deselects all if all are selected, otherwise selects all (D-052).</summary>
    [RelayCommand]
    public void ToggleAllSymbols()
    {
        var selectAll = !AreAllSymbolsSelected;
        _suppressRegenerate = true;
        try
        {
            foreach (var symbol in Symbols)
            {
                symbol.IsEnabled = selectAll;
            }
        }
        finally
        {
            _suppressRegenerate = false;
        }

        UpdateSymbolProperties();
        Regenerate();
    }

    private void UpdateSymbolProperties()
    {
        OnPropertyChanged(nameof(AreAllSymbolsSelected));
        OnPropertyChanged(nameof(ToggleAllSymbolsButtonText));
    }

    public bool IsCustomLength => LengthChoice == PasswordLengthChoice.Custom;

    public string LengthRangeText => $"{PasswordGeneratorOptions.MinLength}〜{PasswordGeneratorOptions.MaxLength}";

    /// <summary>True when a candidate exists for the current options.</summary>
    public bool CanAccept => ErrorMessage is null && Candidate.Length > 0;

    /// <summary>The options as currently chosen (Length is 0 when the custom length is not a number).</summary>
    public PasswordGeneratorOptions ToOptions() => BuildOptions(ResolveLength() ?? 0);

    /// <summary>Makes a new candidate from the current options, or shows why none can be made.</summary>
    [RelayCommand]
    public void Regenerate()
    {
        if (!_initialized)
        {
            return;
        }

        var length = ResolveLength();
        if (length is null)
        {
            Candidate = string.Empty;
            ErrorMessage = $"文字数は {LengthRangeText} の数値で入力してください。";
            return;
        }

        var options = BuildOptions(length.Value);
        var error = options.Validate();
        if (error is not null)
        {
            Candidate = string.Empty;
            ErrorMessage = error;
            return;
        }

        Candidate = PasswordGenerator.Generate(options, _random);
        ErrorMessage = null;
    }

    // 候補（将来のパスワード）を診断出力へ混ぜない
    public override string ToString() => "PasswordGeneratorViewModel";

    partial void OnLengthChoiceChanged(PasswordLengthChoice value)
    {
        OnPropertyChanged(nameof(IsCustomLength));
        Regenerate();
    }

    partial void OnCustomLengthChanged(string value) => Regenerate();

    partial void OnUseUppercaseChanged(bool value) => Regenerate();

    partial void OnUseLowercaseChanged(bool value) => Regenerate();

    partial void OnUseDigitsChanged(bool value) => Regenerate();

    partial void OnUseSymbolsChanged(bool value) => Regenerate();

    partial void OnExcludeAmbiguousChanged(bool value) => Regenerate();

    partial void OnNoRepeatedCharactersChanged(bool value) => Regenerate();

    partial void OnCandidateChanged(string value) => OnPropertyChanged(nameof(CanAccept));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(CanAccept));

    private int? ResolveLength() => LengthChoice switch
    {
        PasswordLengthChoice.Eight => 8,
        PasswordLengthChoice.Twelve => 12,
        PasswordLengthChoice.Sixteen => 16,
        _ => int.TryParse(CustomLength?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
    };

    private PasswordGeneratorOptions BuildOptions(int length) => new()
    {
        Length = length,
        UseUppercase = UseUppercase,
        UseLowercase = UseLowercase,
        UseDigits = UseDigits,
        UseSymbols = UseSymbols,
        EnabledSymbols = string.Concat(Symbols.Where(s => s.IsEnabled).Select(s => s.Symbol)),
        ExcludeAmbiguous = ExcludeAmbiguous,
        NoRepeatedCharacters = NoRepeatedCharacters,
    };
}
