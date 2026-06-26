using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class AccountTests
{
    [Fact]
    public void Create_ValidInput_CreatesAccountWithActiveStatus()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", 100000);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("ACC-001", account.AccountNumber);
        Assert.Equal(AccountType.Asset, account.AccountType);
        Assert.Equal("NGN", account.Currency);
        Assert.Equal(100000, account.Balance);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(DateTimeKind.Utc, account.CreatedAtUtc.Kind);
    }

    [Fact]
    public void Create_NullAccountNumber_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Account.Create(null!, AccountType.Asset, "NGN"));
    }

    [Fact]
    public void Create_EmptyCurrency_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Account.Create("ACC-001", AccountType.Asset, ""));
    }

    [Fact]
    public void IsDebitNormal_AssetAccount_ReturnsTrue()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        Assert.True(account.IsDebitNormal);
    }

    [Fact]
    public void IsDebitNormal_ExpenseAccount_ReturnsTrue()
    {
        var account = Account.Create("ACC-001", AccountType.Expense, "NGN");
        Assert.True(account.IsDebitNormal);
    }

    [Fact]
    public void IsDebitNormal_LiabilityAccount_ReturnsFalse()
    {
        var account = Account.Create("ACC-001", AccountType.Liability, "NGN");
        Assert.False(account.IsDebitNormal);
    }

    [Fact]
    public void IsDebitNormal_RevenueAccount_ReturnsFalse()
    {
        var account = Account.Create("ACC-001", AccountType.Revenue, "NGN");
        Assert.False(account.IsDebitNormal);
    }

    [Fact]
    public void Debit_SufficientBalance_DecreasesBalance()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", 10000);
        account.Debit(3000);
        Assert.Equal(7000, account.Balance);
    }

    [Fact]
    public void Debit_InsufficientBalance_OnDebitNormalAccount_ThrowsInvalidOperationException()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", 1000);
        Assert.Throws<InvalidOperationException>(() => account.Debit(1001));
    }

    [Fact]
    public void Debit_InsufficientBalance_OnCreditNormalAccount_AllowsNegative()
    {
        var account = Account.Create("ACC-001", AccountType.Liability, "NGN", 1000);
        account.Debit(2000);
        Assert.Equal(-1000, account.Balance);
    }

    [Fact]
    public void Debit_ZeroAmount_ThrowsArgumentException()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", 10000);
        Assert.Throws<ArgumentException>(() => account.Debit(0));
    }

    [Fact]
    public void Credit_IncreasesBalance()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", 5000);
        account.Credit(3000);
        Assert.Equal(8000, account.Balance);
    }

    [Fact]
    public void Credit_ZeroAmount_ThrowsArgumentException()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", 5000);
        Assert.Throws<ArgumentException>(() => account.Credit(0));
    }

    [Fact]
    public void Freeze_ActiveAccount_SetsStatusToFrozen()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        account.Freeze();
        Assert.Equal(AccountStatus.Frozen, account.Status);
    }

    [Fact]
    public void Freeze_ClosedAccount_ThrowsInvalidOperationException()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        account.Close();
        Assert.Throws<InvalidOperationException>(() => account.Freeze());
    }

    [Fact]
    public void Close_SetsStatusToClosed()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        account.Close();
        Assert.Equal(AccountStatus.Closed, account.Status);
    }

    [Fact]
    public void Activate_FrozenAccount_SetsStatusToActive()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        account.Freeze();
        account.Activate();
        Assert.Equal(AccountStatus.Active, account.Status);
    }

    [Fact]
    public void Activate_ClosedAccount_ThrowsInvalidOperationException()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        account.Close();
        Assert.Throws<InvalidOperationException>(() => account.Activate());
    }

    [Fact]
    public void Create_TimestampsHaveMillisecondPrecision()
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN");
        Assert.Equal(0, account.CreatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(0, account.UpdatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }
}
