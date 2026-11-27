# Giới hạn Bảo mật & Rủi ro Còn lại (Security Limitations)

TradePulse MT5 Monitor được thiết kế theo nguyên tắc Read-Only, Zero-Credential và Local-First. Tuy nhiên, không có phần mềm nào an toàn tuyệt đối:
1. Không thể bảo vệ dữ liệu trên máy tính đã nhiễm malware hoặc bị chiếm quyền Administrator.
2. Khóa DPAPI (`CurrentUser`) có thể bị giải mã bởi bất kỳ ai đăng nhập cùng tài khoản Windows.
3. Nếu người dùng tự đăng nhập Master Password trong MT5, cửa sổ MT5 vẫn có quyền đặt lệnh.
4. Bản `v1.0.0-rc1` chưa ký Authenticode nên Windows SmartScreen có thể hiển thị cảnh báo khi chạy lần đầu.
