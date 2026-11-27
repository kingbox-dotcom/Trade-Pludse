namespace TradePulse.Mt5Bridge;

public static class TerminalProcessValidator
{
    public static bool IsSafeTerminalExecutablePath(string? path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Đường dẫn terminal trống.";
            return false;
        }
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
        {
            reason = "Từ chối đường dẫn mạng UNC hoặc path traversal.";
            return false;
        }
        if (!Path.IsPathFullyQualified(path))
        {
            reason = "Yêu cầu đường dẫn tuyệt đối tới terminal64.exe.";
            return false;
        }
        if (!string.Equals(Path.GetFileName(path), "terminal64.exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Chỉ chấp nhận tệp thực thi terminal64.exe.";
            return false;
        }
        if (!File.Exists(path))
        {
            reason = "Không tìm thấy tệp terminal64.exe tại đường dẫn đã chỉ định.";
            return false;
        }
        reason = "OK";
        return true;
    }
}
