# TradePulse MT5 Monitor (v1.0.0-rc1)

Ứng dụng Windows Desktop (`.NET 10.0-windows x64`, WPF) theo dõi tài khoản MetaTrader 5 theo chế độ **READ-ONLY**, không nhập mật khẩu, không gửi dữ liệu ra Internet.

## Yêu cầu môi trường build
- Windows 10/11 x64
- .NET 10.0 SDK (x64)
- Python 3.12 x64 + `pip install -r src\sidecar\requirements.txt`

## Lệnh build, test và đóng gói Portable ZIP
```powershell
.\scripts\build-rc-win-x64.ps1 -Version "1.0.0-rc1"
```
