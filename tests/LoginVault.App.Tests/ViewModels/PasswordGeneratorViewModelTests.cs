using LoginVault.App.ViewModels;
using LoginVault.Core.Model;

namespace LoginVault.App.Tests.ViewModels;

public class PasswordGeneratorViewModelTests
{
    private static bool HasUpper(string s) => s.Any(c => c is >= 'A' and <= 'Z');

    [Fact]
    public void Defaults_HaveOtherOptionsTurnedOn()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions());

        Assert.True(vm.ExcludeAmbiguous);
        Assert.True(vm.NoRepeatedCharacters);
    }

    [Fact]
    public void InitializesFromOptions_AndShowsACandidateImmediately()
    {
        var options = new PasswordGeneratorOptions { Length = 16, UseDigits = false, EnabledSymbols = "-_", ExcludeAmbiguous = true };

        var vm = new PasswordGeneratorViewModel(options);

        Assert.Equal(PasswordLengthChoice.Sixteen, vm.LengthChoice);
        Assert.True(vm.UseUppercase);
        Assert.False(vm.UseDigits);
        Assert.True(vm.ExcludeAmbiguous);
        Assert.Equal(19, vm.Symbols.Count);
        Assert.Equal(new[] { '-', '_' }, vm.Symbols.Where(s => s.IsEnabled).Select(s => s.Symbol).ToArray());
        Assert.Contains(vm.Symbols, s => s.Label == "-（ハイフン）");
        Assert.Equal(16, vm.Candidate.Length);
        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.CanAccept);
    }

    [Fact]
    public void ChangingAnOption_RegeneratesTheCandidate()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { Length = 32 });
        var before = vm.Candidate;

        vm.UseUppercase = false;

        Assert.NotEqual(before, vm.Candidate);
        Assert.False(HasUpper(vm.Candidate));
        Assert.Equal(32, vm.Candidate.Length);
    }

    [Fact]
    public void CustomLength_IsUsed_AndInvalidTextShowsAnErrorInsteadOfACandidate()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions());

        vm.LengthChoice = PasswordLengthChoice.Custom;
        vm.CustomLength = "20";
        Assert.Equal(20, vm.Candidate.Length);
        Assert.Equal(20, vm.ToOptions().Length);

        vm.CustomLength = "abc";
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.CanAccept);
        Assert.Equal(string.Empty, vm.Candidate);

        vm.CustomLength = "200";
        Assert.Contains("4〜128", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void PresetLengths_MapToOptions()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { Length = 12 });
        Assert.Equal(PasswordLengthChoice.Twelve, vm.LengthChoice);

        vm.LengthChoice = PasswordLengthChoice.Eight;
        Assert.Equal(8, vm.ToOptions().Length);
        Assert.Equal(8, vm.Candidate.Length);

        var custom = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { Length = 40 });
        Assert.Equal(PasswordLengthChoice.Custom, custom.LengthChoice);
        Assert.Equal("40", custom.CustomLength);
    }

    [Fact]
    public void DeselectingEveryClass_ShowsAnError_UntilOneIsEnabledAgain()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions());

        vm.UseUppercase = false;
        vm.UseLowercase = false;
        vm.UseDigits = false;
        vm.UseSymbols = false;

        Assert.Contains("文字の種類", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.CanAccept);

        vm.UseDigits = true;

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.CanAccept);
        Assert.All(vm.Candidate, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void SymbolSelection_RoundTripsThroughOptions_AndRegenerates()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { Length = 24, UseUppercase = false, UseLowercase = false, UseDigits = false });
        var before = vm.Candidate;

        foreach (var symbol in vm.Symbols.Where(s => s.Symbol is not ('-' or '_')))
        {
            symbol.IsEnabled = false;
        }

        Assert.Equal("-_", vm.ToOptions().EnabledSymbols);
        Assert.NotEqual(before, vm.Candidate);
        Assert.All(vm.Candidate, c => Assert.Contains(c, "-_"));

        // 記号を使わない設定にしても個別の選択は保持される
        vm.UseSymbols = false;
        Assert.Contains("文字の種類", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("-_", vm.ToOptions().EnabledSymbols);
    }

    [Fact]
    public void Regenerate_ProducesADifferentCandidate()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { Length = 16 });
        var before = vm.Candidate;

        vm.RegenerateCommand.Execute(null);

        Assert.NotEqual(before, vm.Candidate);
    }

    [Fact]
    public void ToString_DoesNotRevealTheCandidate()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions());

        Assert.DoesNotContain(vm.Candidate, vm.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToggleAllSymbols_WhenAllSelected_DeselectsAll()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions());

        Assert.True(vm.AreAllSymbolsSelected);
        Assert.Equal("すべて解除", vm.ToggleAllSymbolsButtonText);

        vm.ToggleAllSymbolsCommand.Execute(null);

        Assert.False(vm.AreAllSymbolsSelected);
        Assert.Equal("すべて選択", vm.ToggleAllSymbolsButtonText);
        Assert.All(vm.Symbols, s => Assert.False(s.IsEnabled));
    }

    [Fact]
    public void ToggleAllSymbols_WhenSomeDeselected_SelectsAll()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions());

        // 1 つだけ解除
        vm.Symbols[0].IsEnabled = false;

        Assert.False(vm.AreAllSymbolsSelected);
        Assert.Equal("すべて選択", vm.ToggleAllSymbolsButtonText);

        // 「すべて選択」を実行
        vm.ToggleAllSymbolsCommand.Execute(null);

        Assert.True(vm.AreAllSymbolsSelected);
        Assert.Equal("すべて解除", vm.ToggleAllSymbolsButtonText);
        Assert.All(vm.Symbols, s => Assert.True(s.IsEnabled));
    }

    [Fact]
    public void ToggleAllSymbols_WhenNoneSelected_SelectsAll()
    {
        var vm = new PasswordGeneratorViewModel(new PasswordGeneratorOptions { EnabledSymbols = string.Empty });

        Assert.False(vm.AreAllSymbolsSelected);
        Assert.Equal("すべて選択", vm.ToggleAllSymbolsButtonText);

        vm.ToggleAllSymbolsCommand.Execute(null);

        Assert.True(vm.AreAllSymbolsSelected);
        Assert.Equal("すべて解除", vm.ToggleAllSymbolsButtonText);
        Assert.All(vm.Symbols, s => Assert.True(s.IsEnabled));
    }
}
