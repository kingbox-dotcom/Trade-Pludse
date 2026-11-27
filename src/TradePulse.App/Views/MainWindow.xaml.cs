using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using TradePulse.Mt5Bridge;

namespace TradePulse.App.Views;

public sealed record PositionRow(long Ticket, string Symbol, string Side, decimal Volume, decimal Open, decimal Current, decimal Profit, decimal Swap);
public sealed record OrderRow(long Ticket, string Symbol, string Type, decimal Volume, decimal Open, decimal StopLoss, decimal TakeProfit);
public sealed record DealRow(long Ticket, string Symbol, string Type, string Entry, decimal Volume, decimal Price, decimal Profit, decimal Commission, decimal Swap, decimal Fee);

public partial class MainWindow : Window
{
    private readonly Mt5Gateway? _gateway;
    private readonly DispatcherTimer _refreshTimer;
    private readonly ObservableCollection<PositionRow> _positions = [];
    private readonly ObservableCollection<OrderRow> _orders = [];
    private readonly ObservableCollection<DealRow> _deals = [];
    private bool _connected;
    private string _currency = "USD";
    private int _currencyDigits = 2;

    public MainWindow()
    {
        InitializeComponent();
        PositionsGrid.ItemsSource = _positions;
        OrdersGrid.ItemsSource = _orders;
        HistoryGrid.ItemsSource = _deals;

        try
        {
            _gateway = new Mt5Gateway();
            DiagnosticsPythonText.Text = "Python 3.12 + MetaTrader5: sẵn sàng";
        }
        catch (Exception ex)
        {
            _gateway = null;
            StatusDetail.Text = ex.Message;
            ConnectButton.IsEnabled = false;
            DiagnosticsPythonText.Text = $"Python/MetaTrader5: lỗi - {ex.Message}";
        }

        Closed += (_, _) => _gateway?.Dispose();
        var defaultPath = FindRunningTerminalPath();
        TerminalPathTextBox.Text = defaultPath ?? @"C:\Program Files\MetaTrader 5\terminal64.exe";
        SettingsTerminalPathTextBox.Text = TerminalPathTextBox.Text;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshSnapshotAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null)
        {
            StatusTitle.Text = "CONNECTION FAILED — Không thể khởi chạy bridge";
            return;
        }

        var terminalPath = TerminalPathTextBox.Text.Trim();
        ConnectButton.IsEnabled = false;
        StatusTitle.Text = "CONNECTING — Đang thử kết nối";
        StatusDetail.Text = "Đang kiểm tra terminal và tài khoản MT5...";

        try
        {
            var response = await _gateway.RequestAsync("connect_attach", new { TerminalPath = terminalPath });
            if (!response.Success)
            {
                SetDisconnected(FormatError(response));
                return;
            }

            _connected = true;
            StatusTitle.Text = "CONNECTED — Đã kết nối MetaTrader 5";
            StatusDetail.Text = "TradePulse đang đọc dữ liệu theo chế độ read-only.";
            AccountCards.IsEnabled = true;
            DisconnectButton.IsEnabled = true;
            SettingsTerminalPathTextBox.Text = terminalPath;
            DiagnosticsMt5Text.Text = "MT5: đã attach và đọc được tài khoản";
            await RefreshSnapshotAsync();
            await LoadHistoryAsync();
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            SetDisconnected($"Không thể khởi chạy bridge: {ex.Message}");
        }
        finally
        {
            ConnectButton.IsEnabled = !_connected;
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        if (_gateway is not null)
        {
            try { await _gateway.RequestAsync("disconnect"); } catch { }
        }
        SetDisconnected("Đã ngắt kết nối.");
        DiagnosticsMt5Text.Text = "MT5: chưa kết nối";
        LoginText.Text = BalanceText.Text = EquityText.Text = PositionsText.Text = "--";
        AccountServerText.Text = AccountCompanyText.Text = "--";
        MarginText.Text = MarginLevelText.Text = "Margin: --";
        _positions.Clear();
        _orders.Clear();
        _deals.Clear();
    }

    private void SetDisconnected(string detail)
    {
        _connected = false;
        _refreshTimer.Stop();
        StatusTitle.Text = "DISCONNECTED — Chưa kết nối MetaTrader 5";
        StatusDetail.Text = detail;
        AccountCards.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        ConnectButton.IsEnabled = true;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn terminal64.exe",
            Filter = "MetaTrader terminal (terminal64.exe)|terminal64.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            TerminalPathTextBox.Text = dialog.FileName;
            SettingsTerminalPathTextBox.Text = dialog.FileName;
        }
    }

    private async Task RefreshSnapshotAsync()
    {
        if (!_connected || _gateway is null)
            return;

        try
        {
            var response = await _gateway.RequestAsync("get_snapshot");
            if (!response.Success || response.Data is null)
            {
                StatusDetail.Text = FormatError(response);
                return;
            }

            using var document = JsonDocument.Parse(response.Data.Value.GetRawText());
            var root = document.RootElement;
            var account = root.GetProperty("account");
            var terminal = root.TryGetProperty("terminal", out var terminalElement) ? terminalElement : default;

            _currency = GetString(account, "currency", "USD");
            _currencyDigits = GetInt(account, "currency_digits", 2);
            var login = GetString(account, "login", "--");
            var balance = GetDecimal(account, "balance");
            var equity = GetDecimal(account, "equity", balance);
            var margin = GetDecimal(account, "margin");
            var marginFree = GetDecimal(account, "margin_free");
            var marginLevel = TryDecimal(account, "margin_level");
            var profit = GetDecimal(account, "profit");

            LoginText.Text = login;
            BalanceText.Text = FormatMoney(balance);
            EquityText.Text = FormatMoney(equity);
            AccountServerText.Text = GetString(account, "server", "--");
            AccountCompanyText.Text = GetString(account, "company", "--");
            MarginText.Text = $"Margin: {FormatMoney(margin)}";
            MarginLevelText.Text = marginLevel.HasValue
                ? $"Margin level: {marginLevel.Value:N2}%"
                : "Margin level: --";
            RiskMarginText.Text = FormatMoney(margin);
            RiskFreeMarginText.Text = FormatMoney(marginFree);
            RiskMarginLevelText.Text = marginLevel.HasValue ? $"{marginLevel.Value:N2}%" : "--";
            FloatingPnlText.Text = FormatMoney(profit, true);

            if (root.TryGetProperty("positions", out var positions))
            {
                _positions.Clear();
                foreach (var position in positions.EnumerateArray())
                {
                    _positions.Add(new PositionRow(
                        GetInt64(position, "ticket"),
                        GetString(position, "symbol", "--"),
                        GetInt(position, "type", 0) == 0 ? "BUY" : "SELL",
                        GetDecimal(position, "volume"),
                        GetDecimal(position, "price_open"),
                        GetDecimal(position, "price_current"),
                        GetDecimal(position, "profit"),
                        GetDecimal(position, "swap")));
                }
                PositionsText.Text = _positions.Count.ToString(CultureInfo.InvariantCulture);
                RiskStatusText.Text = marginLevel switch
                {
                    null => "Không có margin level; theo dõi margin và vốn theo thời gian thực.",
                    <= 100m => "CẢNH BÁO: Margin level thấp hoặc nằm trong vùng stop-out.",
                    <= 150m => "Cảnh báo: Margin level đang thấp.",
                    _ => "Mức margin hiện không cho thấy cảnh báo từ phía tài khoản."
                };
            }

            if (root.TryGetProperty("orders", out var orders))
            {
                _orders.Clear();
                foreach (var order in orders.EnumerateArray())
                {
                    _orders.Add(new OrderRow(
                        GetInt64(order, "ticket"),
                        GetString(order, "symbol", "--"),
                        GetInt(order, "type", 0).ToString(CultureInfo.InvariantCulture),
                        GetDecimal(order, "volume_initial"),
                        GetDecimal(order, "price_open"),
                        GetDecimal(order, "sl"),
                        GetDecimal(order, "tp")));
                }
            }

            DiagnosticsSnapshotText.Text = $"Lần cập nhật: {DateTime.Now:HH:mm:ss}";
            if (terminal.ValueKind == JsonValueKind.Object)
                DiagnosticsMt5Text.Text = GetString(terminal, "connected", "true") == "true"
                    ? "MT5: đã kết nối terminal"
                    : "MT5: terminal không báo connected";
        }
        catch (Exception ex)
        {
            StatusDetail.Text = $"Không thể đọc dữ liệu: {ex.Message}";
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (!_connected || _gateway is null)
            return;

        try
        {
            var response = await _gateway.RequestAsync("get_history_deals");
            if (!response.Success || response.Data is null)
                return;

            using var document = JsonDocument.Parse(response.Data.Value.GetRawText());
            var root = document.RootElement;
            _deals.Clear();
            if (!root.TryGetProperty("deals", out var deals))
                return;

            decimal realized = 0m;
            var closedNet = new Dictionary<long, decimal>();
            foreach (var deal in deals.EnumerateArray())
            {
                var type = GetInt(deal, "type", -1);
                var entry = GetInt(deal, "entry", -1);
                var profit = GetDecimal(deal, "profit");
                var commission = GetDecimal(deal, "commission");
                var swap = GetDecimal(deal, "swap");
                var fee = GetDecimal(deal, "fee");
                var net = profit + commission + swap + fee;
                if (type is 0 or 1)
                {
                    realized += net;
                    var positionId = GetInt64(deal, "position_id");
                    if (entry is 1 or 2 or 3)
                        closedNet[positionId] = closedNet.GetValueOrDefault(positionId) + net;
                }

                _deals.Add(new DealRow(
                    GetInt64(deal, "ticket"),
                    GetString(deal, "symbol", "--"),
                    type.ToString(CultureInfo.InvariantCulture),
                    entry.ToString(CultureInfo.InvariantCulture),
                    GetDecimal(deal, "volume"),
                    GetDecimal(deal, "price"),
                    profit,
                    commission,
                    swap,
                    fee));
            }

            RealizedPnlText.Text = FormatMoney(realized, true);
            var closed = closedNet.Where(x => x.Value != 0m).Select(x => x.Value).ToList();
            var wins = closed.Count(x => x > 0m);
            var losses = closed.Where(x => x < 0m).ToList();
            WinRateText.Text = closed.Count > 0
                ? $"{(decimal)wins / closed.Count * 100m:N2}%"
                : "--";
            if (losses.Count > 0)
            {
                var profitSum = closed.Where(x => x > 0m).Sum();
                var lossSum = Math.Abs(losses.Sum());
                ProfitFactorText.Text = lossSum == 0m ? "--" : (profitSum / lossSum).ToString("N2", CultureInfo.InvariantCulture);
            }
            else
            {
                ProfitFactorText.Text = "--";
            }
        }
        catch (Exception ex)
        {
            StatusDetail.Text = $"Không thể đọc history: {ex.Message}";
        }
    }

    private string FormatMoney(decimal value, bool includeSign = false)
    {
        var format = "N" + _currencyDigits;
        var sign = includeSign && value > 0m ? "+" : "";
        return $"{sign}{value.ToString(format, CultureInfo.InvariantCulture)} {_currency}";
    }

    private void ShowPanel(UIElement panel)
    {
        if (DashboardPanel is null || PositionsPanel is null || OrdersPanel is null ||
            HistoryPanel is null || AnalyticsPanel is null || RiskPanel is null ||
            SettingsPanel is null || DiagnosticsPanel is null)
            return;

        DashboardPanel.Visibility = panel == DashboardPanel ? Visibility.Visible : Visibility.Collapsed;
        PositionsPanel.Visibility = panel == PositionsPanel ? Visibility.Visible : Visibility.Collapsed;
        OrdersPanel.Visibility = panel == OrdersPanel ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = panel == HistoryPanel ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsPanel.Visibility = panel == AnalyticsPanel ? Visibility.Visible : Visibility.Collapsed;
        RiskPanel.Visibility = panel == RiskPanel ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = panel == DiagnosticsPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavDashboard_Checked(object sender, RoutedEventArgs e) => ShowPanel(DashboardPanel);
    private void NavPositions_Checked(object sender, RoutedEventArgs e) => ShowPanel(PositionsPanel);
    private void NavOrders_Checked(object sender, RoutedEventArgs e) => ShowPanel(OrdersPanel);
    private void NavHistory_Checked(object sender, RoutedEventArgs e)
    {
        ShowPanel(HistoryPanel);
        _ = LoadHistoryAsync();
    }
    private void NavAnalytics_Checked(object sender, RoutedEventArgs e)
    {
        ShowPanel(AnalyticsPanel);
        _ = LoadHistoryAsync();
    }
    private void NavRisk_Checked(object sender, RoutedEventArgs e) => ShowPanel(RiskPanel);
    private void NavSettings_Checked(object sender, RoutedEventArgs e) => ShowPanel(SettingsPanel);
    private void NavDiagnostics_Checked(object sender, RoutedEventArgs e) => ShowPanel(DiagnosticsPanel);

    private static string GetString(JsonElement element, string property, string fallback)
    {
        if (!element.TryGetProperty(property, out var value))
            return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }
    private static int GetInt(JsonElement element, string property, int fallback = 0)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static long GetInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static decimal GetDecimal(JsonElement element, string property, decimal fallback = 0m)
        => TryDecimal(element, property) ?? fallback;
    private static decimal? TryDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : null;
    }

    private static string FormatError(GatewayResponse response) => response.Error switch
    {
        "MT5_PYTHON_MODULE_NOT_INSTALLED" => "Thiếu gói Python MetaTrader5. Cài bằng: python -m pip install -r src\\sidecar\\requirements.txt",
        "TERMINAL_PATH_REQUIRED" => "Chưa chọn đường dẫn terminal64.exe.",
        "TERMINAL_PATH_NOT_ABSOLUTE" => "Đường dẫn terminal không hợp lệ; phải là đường dẫn tuyệt đối.",
        "INVALID_TERMINAL_FILE" => "Chỉ chấp nhận file terminal64.exe.",
        "TERMINAL_NOT_FOUND" => "Không tìm thấy terminal64.exe tại đường dẫn đã chọn.",
        "MT5_ATTACH_FAILED" => "Không attach được MT5. Kiểm tra terminal đang mở và Python/MT5 cùng kiến trúc x64.",
        "ACCOUNT_NOT_LOGGED_IN" => "MT5 đang mở nhưng chưa có tài khoản đăng nhập.",
        _ => response.Error is null ? "MT5 từ chối kết nối." : response.Error,
    };

    private static string? FindRunningTerminalPath()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("terminal64"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) &&
                    string.Equals(System.IO.Path.GetFileName(path), "terminal64.exe", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            catch { }
        }
        return null;
    }
}
