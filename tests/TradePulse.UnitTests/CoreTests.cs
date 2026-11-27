using TradePulse.Application;
using TradePulse.Domain;
using TradePulse.Mt5Bridge;
using Xunit;

namespace TradePulse.UnitTests;

public sealed class CoreTests
{
    [Fact]
    public void MaskLogin_ShouldHideAllExceptLastThreeDigits()
    {
        Assert.Equal("*****384", FinancialFormatter.MaskLogin(12345384));
    }

    [Fact]
    public void FormatMoney_ShouldRespectCurrencyDigitsAndSign()
    {
        Assert.Equal("+1,250 JPY", FinancialFormatter.FormatMoney(1250.4m, "JPY", 0, includeSign: true));
        Assert.Equal("-45.13 EUR", FinancialFormatter.FormatMoney(-45.126m, "EUR", 2, includeSign: true));
    }

    [Fact]
    public void Analytics_ShouldExcludeDepositAndReturnNullProfitFactorWithoutLosses()
    {
        var engine = new TradeAnalyticsEngine();
        var deals = new List<Deal>
        {
            new(1, 0, 0, "", 2, 0, 0m, 0m, 0m, 0m, 10000m, 0m, 2, 1000, 1000000, 0), // Deposit
            new(2, 10, 100, "EURUSD", 0, 0, 0.1m, 1.1000m, -0.7m, 0m, 0m, 0m, 2, 2000, 2000000, 0),
            new(3, 11, 100, "EURUSD", 1, 1, 0.1m, 1.1050m, -0.7m, -0.1m, 50m, 0m, 2, 3000, 3000000, 0)
        };

        var summary = engine.Compute(deals, Array.Empty<Position>(), "USD", 2);
        Assert.Equal(48.50m, summary.RealizedNetPnl);
        Assert.Equal(1, summary.ClosedPositionsCount);
        Assert.Equal(100m, summary.WinRatePercent);
        Assert.Null(summary.ProfitFactor);
    }

    [Fact]
    public void TerminalValidator_ShouldRejectUncAndPathTraversal()
    {
        Assert.False(TerminalProcessValidator.IsSafeTerminalExecutablePath(@"\\server\share\terminal64.exe", out _));
        Assert.False(TerminalProcessValidator.IsSafeTerminalExecutablePath(@"C:\MT5\..\terminal64.exe", out _));
    }
}
