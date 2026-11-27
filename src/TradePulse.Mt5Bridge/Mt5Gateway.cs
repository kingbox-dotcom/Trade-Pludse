using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TradePulse.Mt5Bridge;

public sealed class GatewayResponse
{
    public string RequestId { get; init; } = "";
    public string Command { get; init; } = "";
    public bool Success { get; init; }
    public JsonElement? Data { get; init; }
    public string? Error { get; init; }
}

public sealed class Mt5Gateway : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public Mt5Gateway()
    {
        var sidecarRoot = FindSidecarRoot();
        if (sidecarRoot is null)
            throw new InvalidOperationException("Không tìm thấy thư mục sidecar của TradePulse.");

        var script = Path.Combine(sidecarRoot, "tradepulse_sidecar", "mt5_readonly_gateway.py");
        if (!File.Exists(script))
            throw new InvalidOperationException($"Không tìm thấy gateway: {script}");

        var pythonExecutable = FindPythonExecutable();
        if (pythonExecutable is null)
            throw new InvalidOperationException(
                "Không tìm thấy Python 3.12 x64. Cài Python 3.12 rồi chạy pip install -r src\\sidecar\\requirements.txt.");

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = sidecarRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["PYTHONPATH"] = sidecarRoot;
        startInfo.ArgumentList.Add(script);

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Không thể khởi chạy Python sidecar.");
    }

    private static string? FindPythonExecutable()
    {
        var candidates = new[]
        {
            @"C:\Users\Admin\AppData\Local\Programs\Python\Python312\python.exe",
            @"C:\Program Files\Python312\python.exe",
            @"C:\Python312\python.exe",
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var localPython = Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python312", "python.exe");
            if (File.Exists(localPython))
                return localPython;
        }
        return null;
    }

    public async Task<GatewayResponse> RequestAsync(string command, object? payload = null)
    {
        await _requestLock.WaitAsync();
        try
        {
            if (_process.HasExited)
            {
                var errorOutput = await _process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Sidecar đã dừng. {errorOutput}".Trim());
            }

            var request = new Dictionary<string, object?>
            {
                ["requestId"] = Guid.NewGuid().ToString("N"),
                ["command"] = command,
            };
            if (payload is not null)
            {
                foreach (var property in payload.GetType().GetProperties())
                {
                    var value = property.GetValue(payload);
                    if (property.Name == "TerminalPath")
                        request["terminalPath"] = value;
                }
            }

            var json = JsonSerializer.Serialize(request);
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();

            var responseLine = await _process.StandardOutput.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(responseLine))
                throw new InvalidOperationException("Gateway không trả phản hồi.");

            return JsonSerializer.Deserialize<GatewayResponse>(
                responseLine,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Phản hồi gateway không hợp lệ.");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static string? FindSidecarRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "sidecar"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "sidecar"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "sidecar"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath) &&
                File.Exists(Path.Combine(fullPath, "tradepulse_sidecar", "mt5_readonly_gateway.py")))
                return fullPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var fullPath = Path.Combine(directory.FullName, "src", "sidecar");
            if (Directory.Exists(fullPath))
                return fullPath;
            directory = directory.Parent;
        }
        return null;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.StandardInput.WriteLine("{\"requestId\":\"shutdown\",\"command\":\"disconnect\"}");
                _process.StandardInput.Flush();
                _process.WaitForExit(1000);
            }
            catch
            {
            }
            if (!_process.HasExited)
                _process.Kill(true);
        }
        _process.Dispose();
        _requestLock.Dispose();
    }
}
