# FileMonitorApps

Công cụ desktop trên Windows dùng để **giám sát và ghi nhật ký các thay đổi tệp tin** trong một thư mục được chỉ định: tạo mới, sửa đổi, xóa và đổi tên.

> Đồ án môn **Kỹ thuật lập trình (KTLT)** — Nhan Nguyen Huu.

## 1. Mục tiêu

Windows không cung cấp công cụ thuận tiện cho người dùng thông thường để xem lại lịch sử thay đổi của một thư mục. Các giải pháp sẵn có hoặc quá đơn giản (không tùy biến được cấu trúc nhật ký), hoặc thuộc nhóm doanh nghiệp với chi phí triển khai vượt quá nhu cầu. Đồ án xây dựng một công cụ nhẹ, dễ sử dụng, cho phép theo dõi thay đổi theo thời gian thực và lưu lại nhật ký để tra cứu sau.

## 2. Công nghệ sử dụng

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | C# |
| Nền tảng | .NET Framework 4.7.2 |
| Giao diện | Windows Forms |
| Cơ chế giám sát | `System.IO.FileSystemWatcher` (hướng sự kiện, không quét lại theo chu kỳ) |
| Lưu trữ nhật ký | Ghi tệp văn bản (file I/O) |
| IDE | Visual Studio |

## 3. Phạm vi

Công cụ **chỉ phát hiện và ghi nhận** thay đổi; **không ngăn chặn** thay đổi. Đây là quyết định giới hạn phạm vi có chủ ý của đồ án.

## 4. Kiến thức được vận dụng

- Lập trình hướng sự kiện (`delegate`, `event`)
- Xử lý đa luồng và cập nhật giao diện an toàn giữa các luồng (thread-safe UI update trong WinForms)
- Thao tác vào/ra tệp tin (file I/O)
- Xử lý ngoại lệ

## 5. Cấu trúc thư mục

```
FileMonitorApps.slnx        # Tệp giải pháp (solution)
FileMonitorApps.csproj      # Tệp dự án
Program.cs                  # Điểm vào ứng dụng
Form1.cs                    # Logic giao diện chính
Form1.Designer.cs           # Mã sinh tự động bởi Designer
App.config                  # Cấu hình ứng dụng
Properties/                 # AssemblyInfo, Resources, Settings
```

## 6. Cách chạy

1. Mở `FileMonitorApps.slnx` bằng Visual Studio.
2. Khôi phục NuGet packages nếu được yêu cầu.
3. Nhấn `F5` để biên dịch và chạy ở chế độ Debug.

## 7. Trạng thái

Đang phát triển.
