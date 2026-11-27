using TradePulse.Domain;

namespace TradePulse.Application;

public sealed record TradeAnalyticsSummary(
    decimal RealizedNetPnl,
    decimal FloatingPnl,
    int ClosedPositionsCount,
    int WinningPositionsCount,
    int LosingPositionsCount,
    decimal? WinRatePercent,
    decimal? ProfitFactor,
    string Currency,
    int CurrencyDigits);

public sealed class TradeAnalyticsEngine
{
    // Deal types 0 (BUY) and 1 (SELL) are trading deals; 2 (BALANCE), 3 (CREDIT), etc. are excluded.
    public TradeAnalyticsSummary Compute(
        IReadOnlyList<Deal> deals,
        IReadOnlyList<Position> openPositions,
        string currency,
        int currencyDigits)
    {
        var tradingDeals = deals.Where(d => d.Type is 0 or 1).ToList();
        var realizedNet = tradingDeals.Sum(d => d.Profit + d.Commission + d.Swap + d.Fee);
        var floating = openPositions.Sum(p => p.Profit + p.Swap);

        var closedByPosition = tradingDeals
            .Where(d => d.PositionId > 0)
            .GroupBy(d => d.PositionId)
            .Where(g => g.Any(d => d.Entry is 1 or 2 or 3)) // OUT, INOUT, OUT_BY
            .Select(g => g.Sum(d => d.Profit + d.Commission + d.Swap + d.Fee))
            .ToList();

        var wins = closedByPosition.Where(x => x > 0m).ToList();
        var losses = closedByPosition.Where(x => x < 0m).ToList();

        decimal? winRate = closedByPosition.Count > 0
            ? Math.Round((decimal)wins.Count / closedByPosition.Count * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        decimal? profitFactor = losses.Count > 0
            ? Math.Round(wins.Sum() / Math.Abs(losses.Sum()), 2, MidpointRounding.AwayFromZero)
            : null;

        return new TradeAnalyticsSummary(
            FinancialFormatter.RoundCurrency(realizedNet, currencyDigits),
            FinancialFormatter.RoundCurrency(floating, currencyDigits),
            closedByPosition.Count,
            wins.Count,
            losses.Count,
            winRate,
            profitFactor,
            currency,
            currencyDigits);
    }
}
