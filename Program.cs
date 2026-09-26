using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileMonitorApps
{
    /// <summary>
    /// Điểm vào của chương trình FileMonitor.
    /// </summary>
    /// <remarks>
    /// BẢN ĐỒ MÃ NGUỒN (dùng khi viết Chương 3 và khi bảo vệ)
    ///
    /// Lớp giao diện (Presentation)
    ///   MainForm                 Chỉ nhận thao tác, nghe sự kiện, chuyển luồng, hiển thị.
    ///
    /// Lớp nghiệp vụ (Services)
    ///   MonitoringSession        Một phiên giám sát: theo dõi → ghi nhật ký → đếm → xử lý sự cố.
    ///   FileMonitorService       Bọc FileSystemWatcher, chống trùng, phát hiện mất thư mục.
    ///   LogService               Ghi/đọc/lọc/xuất/xóa nhật ký, mỗi ngày một tệp.
    ///   FolderValidator          Kiểm tra thư mục trước khi giám sát.
    ///   MonitorErrorClassifier   Phân loại sự cố và viết lời giải thích.
    ///   FileSizeProbe            Đọc kích thước tệp, bắt mọi ngoại lệ.
    ///   EventDebouncer           Chống trùng sự kiện.
    ///   EventCounter             Đếm sự kiện theo loại.
    ///
    /// Lớp dữ liệu (Models)
    ///   FileEventLog, FileEventType, LogFilter, FileEventDetectedEventArgs
    ///
    /// BỐN ĐIỂM KỸ THUẬT CỐT LÕI — tìm chuỗi "ĐIỂM KỸ THUẬT" trong mã nguồn để thấy đúng chỗ:
    ///   ① Cross-thread      MainForm.Session_EventRecorded, MainForm.FlushPendingEvents
    ///   ② Sự kiện trùng lặp EventDebouncer.ShouldReport, FileMonitorService.Watcher_Changed
    ///   ③ Tràn bộ đệm       FileMonitorService.Start (InternalBufferSize),
    ///                        MonitoringSession.Monitor_ErrorOccurred
    ///   ④ Tệp bị khóa       FileSizeProbe.TryGetSize, LogService.OpenForAppend, LogService.ReadLines
    /// </remarks>
    internal static class Program
    {
        /// <summary>
        /// Điểm bắt đầu của chương trình: bật giao diện kiểu Windows hiện đại, đăng ký bộ bắt
        /// lỗi toàn cục, rồi mở cửa sổ chính và chạy vòng lặp thông điệp.
        /// </summary>
        /// <remarks>
        /// [STAThread]: Windows Forms (hộp thoại chọn thư mục, clipboard...) bắt buộc luồng
        /// giao diện chạy ở chế độ Single-Threaded Apartment của COM.
        /// </remarks>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Lưới an toàn cuối cùng. Mọi chỗ đã biết có thể lỗi (thiếu quyền, tệp bị khóa...)
            // đều đã có try/catch riêng; hai bộ xử lý dưới đây chỉ dành cho lỗi KHÔNG lường trước,
            // để người dùng thấy một thông báo dễ hiểu thay vì hộp thoại lỗi mặc định của .NET,
            // và để lại tệp ghi chi tiết phục vụ sửa lỗi.
            // Phải đăng ký TRƯỚC khi tạo bất kỳ Form/control nào.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Application.Run(new MainForm());
        }

        /// <summary>
        /// Lỗi chưa được bắt trên LUỒNG GIAO DIỆN (bấm nút, gõ phím...).
        /// Chương trình vẫn chạy tiếp được sau khi thông báo.
        /// </summary>
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Exception error = e.Exception;
            string reportPath = WriteErrorReport(error);

            string message = error is UnauthorizedAccessException
                ? "Chương trình không đủ quyền để thực hiện thao tác vừa rồi." + Environment.NewLine +
                  "Hãy thử chọn thư mục khác, hoặc chạy chương trình bằng quyền Administrator."
                : "Đã xảy ra lỗi ngoài dự kiến. Chương trình vẫn tiếp tục chạy.";

            MessageBox.Show(
                message + Environment.NewLine + Environment.NewLine +
                "Chi tiết: " + error.Message +
                (reportPath.Length > 0 ? Environment.NewLine + Environment.NewLine +
                    "Thông tin lỗi đã được lưu tại:" + Environment.NewLine + reportPath : string.Empty),
                "Lỗi",
                MessageBoxButtons.OK,
                error is UnauthorizedAccessException ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
        }

        /// <summary>
        /// Lỗi chưa được bắt trên LUỒNG NỀN (ví dụ luồng của FileSystemWatcher).
        /// .NET sẽ đóng chương trình ngay sau hàm này; việc duy nhất làm được là để lại dấu vết.
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception error = e.ExceptionObject as Exception;
            string reportPath = WriteErrorReport(error);

            try
            {
                MessageBox.Show(
                    "Chương trình gặp lỗi nghiêm trọng và phải đóng lại." + Environment.NewLine +
                    Environment.NewLine + "Chi tiết: " + (error != null ? error.Message : "không rõ") +
                    (reportPath.Length > 0 ? Environment.NewLine + Environment.NewLine +
                        "Thông tin lỗi đã được lưu tại:" + Environment.NewLine + reportPath : string.Empty),
                    "Lỗi nghiêm trọng",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // Đang trong lúc chương trình sắp đóng; không hiện được thông báo thì thôi.
            }
        }

        /// <summary>
        /// Ghi chi tiết lỗi (kèm stack trace) vào tệp loi-chuong-trinh.log trong thư mục nhật ký.
        /// Trả về đường dẫn tệp, hoặc chuỗi rỗng nếu không ghi được.
        /// </summary>
        /// <remarks>
        /// Tên tệp không theo mẫu filemonitor-yyyyMMdd.log nên không lẫn vào nhật ký giám sát,
        /// và nút "Xóa log" cũng không xóa nó.
        /// Tự bắt mọi lỗi: hàm báo lỗi mà lại ném lỗi thì không còn gì cứu được nữa.
        /// </remarks>
        private static string WriteErrorReport(Exception error)
        {
            try
            {
                string folder = LogService.GetDefaultLogFolder();
                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "loi-chuong-trinh.log");
                File.AppendAllText(path,
                    "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====" + Environment.NewLine +
                    (error != null ? error.ToString() : "Không rõ lỗi") + Environment.NewLine + Environment.NewLine);
                return path;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
