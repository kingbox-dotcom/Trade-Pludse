namespace TradePulse.Domain;

public enum ConnectionStatus
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Stale = 3,
    Error = 4
}

public enum DataFreshness
{
    Live = 0,
    Stale = 1,
    Unavailable = 2
}

public enum AccountPermissionState
{
    Unknown = 0,
    UserAttestedInvestorUnverified = 1,
    ServerTradeDisabled = 2,
    MasterTradingPermitted = 3,
    PermissionUnverifiedOffline = 4
}

public sealed record AccountSnapshot(
    string AccountKey,
    long Login,
    string Server,
    string Company,
    string Currency,
    int CurrencyDigits,
    int Leverage,
    decimal Balance,
    decimal Equity,
    decimal Profit,
    decimal Credit,
    decimal Margin,
    decimal MarginFree,
    decimal? MarginLevel,
    bool? TradeAllowedFlag,
    bool? TradeExpertFlag,
    DateTimeOffset CapturedAtUtc,
    DataFreshness Freshness);

public sealed record Position(
    long Ticket,
    long Identifier,
    string Symbol,
    int Type,
    decimal Volume,
    decimal PriceOpen,
    decimal PriceCurrent,
    decimal StopLoss,
    decimal TakeProfit,
    decimal Profit,
    decimal Swap,
    long BrokerTimeSec,
    long BrokerTimeMsc,
    long Magic);

public sealed record PendingOrder(
    long Ticket,
    string Symbol,
    int Type,
    int State,
    decimal VolumeInitial,
    decimal VolumeCurrent,
    decimal PriceOpen,
    decimal PriceCurrent,
    decimal StopLoss,
    decimal TakeProfit,
    long TimeSetupSec,
    long TimeExpirationSec);

public sealed record Deal(
    long Ticket,
    long OrderTicket,
    long PositionId,
    string Symbol,
    int Type,
    int Entry,
    decimal Volume,
    decimal Price,
    decimal Commission,
    decimal Swap,
    decimal Profit,
    decimal Fee,
    int CurrencyDigits,
    long BrokerTimeSec,
    long BrokerTimeMsc,
    long Magic);

public sealed record HistoricalOrder(
    long Ticket,
    string Symbol,
    int Type,
    int State,
    decimal VolumeInitial,
    decimal VolumeCurrent,
    decimal PriceOpen,
    long TimeSetupSec,
    long TimeDoneSec,
    long Magic);

public sealed record EquitySnapshot(
    string AccountKey,
    DateTimeOffset CapturedAtUtc,
    decimal Balance,
    decimal Equity,
    decimal Margin,
    decimal MarginFree,
    decimal? MarginLevel,
    decimal Profit,
    int OpenPositionsCount,
    int PendingOrdersCount);

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ISensitiveDataProtector : IDisposable
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public interface IMt5ReadOnlyClient
{
    Task<AccountSnapshot?> GetAccountSnapshotAsync(CancellationToken ct);
    Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken ct);
    Task<IReadOnlyList<PendingOrder>> GetPendingOrdersAsync(CancellationToken ct);
    Task<IReadOnlyList<Deal>?> GetHistoryDealsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}

public static class FinancialFormatter
{
    public static string MaskLogin(long login)
    {
        var text = login.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length <= 3) return "***";
        return new string('*', text.Length - 3) + text[^3..];
    }

    public static decimal RoundCurrency(decimal value, int currencyDigits)
    {
        var digits = Math.Clamp(currencyDigits, 0, 8);
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    public static string FormatMoney(decimal value, string currency, int currencyDigits, bool includeSign = false)
    {
        var rounded = RoundCurrency(value, currencyDigits);
        var fmt = "N" + Math.Clamp(currencyDigits, 0, 8);
        var sign = includeSign && rounded > 0 ? "+" : string.Empty;
        return $"{sign}{rounded.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture)} {currency}";
    }
}
