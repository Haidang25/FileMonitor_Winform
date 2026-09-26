# FileMonitorApps

Công cụ desktop trên Windows dùng để **giám sát và ghi nhật ký các thay đổi tệp tin** trong một thư mục: tạo mới, sửa đổi, xóa và đổi tên.

> Đồ án môn **Kỹ thuật lập trình (KTLT)** — Nhan Nguyen Huu.

## 1. Mục tiêu

Windows không có sẵn cách thuận tiện để người dùng thông thường xem lại lịch sử thay đổi của một thư mục. Khi một tệp bị mất hoặc bị sửa sai, người dùng gần như không có căn cứ để biết chuyện xảy ra lúc nào. Đồ án xây dựng một công cụ nhẹ: theo dõi thay đổi theo thời gian thực, ghi lại nhật ký, và cho phép tìm kiếm, lọc, xuất nhật ký để tra cứu sau.

## 2. Phạm vi

Công cụ **chỉ phát hiện và ghi nhận** thay đổi, **không ngăn chặn** thay đổi. Đây là giới hạn phạm vi có chủ ý.

| Khía cạnh | Giới hạn |
|---|---|
| Hệ điều hành | Chỉ Windows |
| Vị trí giám sát | Ổ đĩa cục bộ. Ổ mạng vẫn chọn được nhưng chương trình sẽ cảnh báo, và không cam kết hoạt động đúng |
| Số thư mục | Một thư mục tại một thời điểm, có tùy chọn bao gồm thư mục con |
| Mức độ phát hiện | Tên, đường dẫn, loại thay đổi, kích thước. **Không** so sánh nội dung tệp |
| Chế độ chạy | Cửa sổ chạy trực tiếp, **không** chạy nền dạng Windows Service |
| Lưu trữ | Tệp văn bản, **không** dùng cơ sở dữ liệu |

## 3. Công nghệ

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | C# |
| Nền tảng | .NET Framework 4.7.2 |
| Giao diện | Windows Forms |
| Cơ chế giám sát | `System.IO.FileSystemWatcher`: hướng sự kiện, không quét lại theo chu kỳ |
| Lưu trữ nhật ký | Tệp văn bản, mỗi ngày một tệp; xuất ra CSV khi cần |
| IDE | Visual Studio |

## 4. Chức năng

**Giám sát (tab "Giám sát")**
- Chọn thư mục bằng hộp thoại hoặc gõ đường dẫn. Đường dẫn được kiểm tra kỹ trước khi bắt đầu: rỗng, ký tự cấm, đường dẫn tương đối, ổ đĩa không có, trỏ vào tệp, không tồn tại, không có quyền, trùng thư mục nhật ký.
- Tùy chọn bao gồm thư mục con và lọc theo phần mở rộng (`*.*`, `*.txt`, `*.docx`, `*.xlsx`, `*.pdf`, `*.png`, `*.cs`, `*.log`).
- Bắt đầu / dừng giám sát. Nhãn trạng thái có các mức:
  - Chưa giám sát
  - Đang giám sát
  - Bỏ sót N lần (tràn bộ đệm)
  - Lỗi ghi nhật ký
  - Lỗi — đã dừng
- Phát hiện 4 loại sự kiện: Created, Changed, Deleted, Renamed (giữ lại cả tên cũ).
- Chống trùng sự kiện: một lần lưu tệp chỉ ghi một dòng.
- Bảng hiển thị thời gian thực (giữ tối đa 5.000 dòng gần nhất), tô màu theo loại, có cột kích thước.
- Bộ đếm sự kiện theo từng loại; nút xóa danh sách trên màn hình (không xóa nhật ký).

**Nhật ký (tab "Nhật ký")**
- Tải nhật ký theo khoảng ngày; chỉ đọc đúng các tệp của những ngày được chọn.
- Tìm kiếm theo tên tệp hoặc đường dẫn:
  - không phân biệt hoa/thường và dấu tiếng Việt (gõ `bao cao` tìm ra `Báo cáo`);
  - gõ nhiều từ thì kết quả phải chứa đủ các từ;
  - tìm được cả theo tên cũ của tệp đã đổi tên.
- Lọc theo loại sự kiện và khoảng ngày.
- Bấm tiêu đề cột để sắp xếp theo giá trị thật (thời gian, số byte).
- Xuất ra CSV tại vị trí người dùng chọn:
  - chọn dấu phân cách `;` hoặc `,` (tự chọn sẵn theo cài đặt vùng của Windows);
  - có BOM để Excel hiển thị đúng tiếng Việt;
  - chống CSV injection.
- Xóa nhật ký, có hộp thoại xác nhận ghi rõ số ngày sẽ bị xóa.

**Xử lý lỗi**
- Thư mục bị xóa, bỏ vào Thùng rác hoặc di chuyển trong lúc giám sát: tự dừng và báo đúng nguyên nhân.
- Không đủ quyền (`UnauthorizedAccessException`): báo rõ ràng. Nếu thư mục chương trình không ghi được thì tự chuyển nhật ký sang `%LocalAppData%`.
- Tệp bị khóa hoặc đã biến mất (`IOException`): kích thước hiển thị "N/A" kèm lý do. Tệp nhật ký bị giữ tạm thời thì chương trình thử ghi lại.
- Tràn bộ đệm của `FileSystemWatcher`: vẫn giám sát tiếp và báo số lần bỏ sót.
- Lỗi không lường trước: hiện thông báo dễ hiểu và ghi chi tiết vào `loi-chuong-trinh.log`.

## 5. Kiến trúc

Ba lớp. Lớp nghiệp vụ **không tham chiếu Windows Forms**, nên có thể dùng lại nguyên vẹn nếu thay giao diện.

```
┌──────────────────────────────────────────────────────────────┐
│ GIAO DIỆN     MainForm                                        │
│               nhận thao tác · nghe sự kiện · chuyển về luồng  │
│               giao diện · hiển thị                            │
└───────────────┬──────────────────────────────────────────────┘
                │ gọi xuống / nhận sự kiện lên
┌───────────────▼──────────────────────────────────────────────┐
│ NGHIỆP VỤ     MonitoringSession   một phiên: theo dõi → ghi   │
│                                   nhật ký → đếm → xử lý sự cố │
│               FileMonitorService  bọc FileSystemWatcher       │
│               LogService          ghi/đọc/lọc/xuất/xóa        │
│               FolderValidator     kiểm tra thư mục            │
│               MonitorErrorClassifier  phân loại sự cố         │
│               FileSizeProbe       đọc kích thước tệp an toàn  │
│               EventDebouncer      chống trùng sự kiện         │
│               EventCounter        đếm theo loại               │
└───────────────┬──────────────────────────────────────────────┘
                │ dùng
┌───────────────▼──────────────────────────────────────────────┐
│ DỮ LIỆU       FileEventLog · FileEventType · LogFilter ·      │
│               FileEventDetectedEventArgs                      │
└──────────────────────────────────────────────────────────────┘
```

Luồng chính: `FileSystemWatcher` phát sự kiện trên **luồng nền** → `FileMonitorService` chống trùng, đọc kích thước → `MonitoringSession` ghi nhật ký, đếm → `MainForm` gom bản ghi rồi dùng `BeginInvoke` đưa về **luồng giao diện** để hiển thị.

### Bốn điểm kỹ thuật cốt lõi

Tìm chuỗi `ĐIỂM KỸ THUẬT` trong mã nguồn để thấy đúng vị trí xử lý.

| # | Vấn đề | Cách xử lý | Vị trí |
|---|---|---|---|
| ① | Cross-thread: watcher chạy trên luồng nền, không được đụng tới control | Gom bản ghi vào hàng chờ, một lượt `BeginInvoke` cho cả loạt | `MainForm.Session_EventRecorded`, `FlushPendingEvents` |
| ② | Một lần lưu tệp sinh 2–4 sự kiện Changed | Chống trùng theo từng đường dẫn, ngưỡng 500 ms | `EventDebouncer.ShouldReport` |
| ③ | Tràn bộ đệm khi thay đổi dồn dập, sự kiện mất vĩnh viễn | Bộ đệm 16 KB (64 KB khi có thư mục con); vẫn chạy tiếp và báo số lần bỏ sót | `FileMonitorService.Start`, `MonitoringSession` |
| ④ | Tệp đang bị khóa hoặc đã biến mất | Bắt `IOException` và các lớp con; hiển thị "N/A"; thử mở lại tệp nhật ký | `FileSizeProbe`, `LogService.OpenForAppend` |

## 6. Nhật ký

- **Vị trí:** `<thư mục chương trình>\Logs\`. Nếu thư mục này không ghi được (ví dụ chương trình nằm trong `Program Files`) thì dùng `%LocalAppData%\FileMonitorApps\Logs\`.
- **Tên tệp:** mỗi ngày một tệp, `filemonitor-yyyyMMdd.log`.
- **Định dạng:** mỗi dòng một bản ghi, 7 cột cách nhau bằng TAB (đường dẫn Windows không bao giờ chứa TAB):

  ```
  Thời gian <TAB> Loại <TAB> Tên tệp <TAB> Đường dẫn <TAB> Đường dẫn cũ <TAB> Kích thước (byte) <TAB> Ghi chú
  2026-09-26 14:05:09	Renamed	bản cuối.docx	D:\MonitorTest\bản cuối.docx	D:\MonitorTest\bản nháp.docx	18432
  ```

- Tệp nhật ký một tệp duy nhất của phiên bản cũ (`Logs\filemonitor.log`) được tự chuyển sang tệp theo ngày khi mở chương trình, rồi đổi tên thành `.imported`.
- Lỗi không lường trước được ghi riêng vào `Logs\loi-chuong-trinh.log`.

## 7. Cấu trúc mã nguồn

```
FileMonitorApps.slnx            Tệp giải pháp
FileMonitorApps.csproj          Tệp dự án
Program.cs                      Điểm vào, bắt lỗi toàn cục, bản đồ mã nguồn
MainForm.cs                     Giao diện (hai tab Giám sát / Nhật ký)
MainForm.Designer.cs            Bố cục control (sinh bởi Designer)
MonitoringSession.cs            Phiên giám sát
FileMonitorService.cs           Bọc FileSystemWatcher
LogService.cs                   Đọc/ghi/lọc/xuất nhật ký
LogFilter.cs                    Điều kiện lọc: ngày, loại, từ khóa
FolderValidator.cs              Kiểm tra thư mục giám sát
MonitorErrorClassifier.cs       Phân loại sự cố
FileSizeProbe.cs                Đọc kích thước tệp an toàn
EventDebouncer.cs               Chống trùng sự kiện
EventCounter.cs                 Đếm sự kiện theo loại
FileEventLog.cs                 Một bản ghi nhật ký
FileEventType.cs                Kiểu liệt kê loại sự kiện
FileEventDetectedEventArgs.cs   Delegate và dữ liệu của sự kiện
App.config                      Cấu hình ứng dụng
Properties/                     AssemblyInfo, Resources, Settings
```

## 8. Cách chạy

1. Mở `FileMonitorApps.slnx` bằng Visual Studio.
2. Nhấn `F5` để biên dịch và chạy ở chế độ Debug.
3. Ở tab **Giám sát**: chọn thư mục (ví dụ `D:\MonitorTest`) rồi bấm **Bắt đầu giám sát**. Tạo, sửa, đổi tên hoặc xóa tệp trong thư mục đó để thấy sự kiện hiện lên bảng.
4. Ở tab **Nhật ký**: bấm **Tải log** để xem lại, tìm kiếm, lọc hoặc xuất ra CSV.

Phím tắt: `Alt+B` bắt đầu, `Alt+D` dừng, `Alt+T` chọn thư mục, `Alt+L` tải log, `Alt+X` xuất log.

## 9. Hạn chế đã biết

- `FileSystemWatcher` có thể **bỏ sót sự kiện** khi thư mục thay đổi quá dồn dập (tràn bộ đệm). Chương trình báo số lần bỏ sót nhưng không lấy lại được các sự kiện đã mất.
- **Di chuyển tệp sang thư mục khác** được ghi thành một cặp Deleted + Created, không phải Renamed. Đây là cách Windows báo sự kiện.
- Chống trùng dùng ngưỡng 500 ms: hai lần sửa thật sự cách nhau dưới ngưỡng này sẽ bị tính là một.
- Kích thước là giá trị **tại thời điểm phát hiện**; sự kiện Created thường ghi 0 B vì tệp được tạo rỗng trước khi có nội dung.
- Nếu thư mục giám sát bị chuyển đi rồi một thư mục mới cùng tên được tạo lại trong vòng 2 giây, chương trình không nhận ra.
- Ổ mạng và đường dẫn UNC nằm ngoài phạm vi cam kết.

## 10. Trạng thái

Đã hoàn thiện chức năng (nhóm A–E). Đang kiểm thử trên Windows (Tuần 7).
