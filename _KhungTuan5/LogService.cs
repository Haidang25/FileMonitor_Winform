using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileMonitorApps
{
    /// <summary>
    /// Ghi, đọc, tìm kiếm, lọc và xuất nhật ký giám sát.
    /// </summary>
    /// <remarks>
    /// KHUNG TUẦN 5 — mọi phương thức đều đang ném NotImplementedException.
    /// Làm theo thứ tự số bước ghi trong TODO: bước sau dùng lại kết quả bước trước.
    ///
    /// Thay đổi so với bản LogService hiện tại:
    ///   1. Mỗi ngày một tệp: Logs\filemonitor-yyyyMMdd.log (checklist F).
    ///      Đọc theo khoảng ngày thì chỉ mở đúng những tệp cần, không đọc cả lịch sử.
    ///   2. Tìm kiếm / lọc chuyển từ MainForm sang đây (checklist G:
    ///      "MainForm.cs không chứa logic nghiệp vụ").
    ///   3. Ghi lỗi không còn bị nuốt im lặng: TryAppend trả về false và
    ///      LastWriteError cho biết lý do, để MainForm báo cho người dùng.
    ///
    /// Không đổi:
    ///   - Chuyển bản ghi ↔ dòng văn bản vẫn do FileEventLog lo (ToLogLine / TryParse).
    ///   - Khóa ghi tệp là khóa của từng đối tượng → cả chương trình chỉ dùng MỘT đối tượng.
    ///   - Không tham chiếu Windows Forms.
    /// </remarks>
    internal class LogService
    {
        #region Hằng số

        /// <summary>Tên thư mục con chứa nhật ký, nằm cạnh tệp chương trình.</summary>
        public const string DefaultFolderName = "Logs";

        /// <summary>Tiền tố tên tệp nhật ký.</summary>
        public const string FilePrefix = "filemonitor-";

        /// <summary>Phần mở rộng tệp nhật ký.</summary>
        public const string FileExtension = ".log";

        /// <summary>
        /// Định dạng ngày trong tên tệp. Dạng năm-tháng-ngày để sắp xếp theo tên
        /// cũng chính là sắp xếp theo thời gian.
        /// </summary>
        public const string FileDateFormat = "yyyyMMdd";

        #endregion

        #region Trường và thuộc tính

        /// <summary>
        /// Khóa dùng chung cho mọi thao tác đọc/ghi tệp của đối tượng này.
        /// FileSystemWatcher gọi Append từ nhiều luồng thread pool cùng lúc.
        /// </summary>
        private readonly object fileLock = new object();

        /// <summary>Thư mục chứa các tệp nhật ký.</summary>
        public string LogFolder { get; private set; }

        /// <summary>
        /// Mô tả lỗi của lần ghi thất bại gần nhất, rỗng nếu chưa từng lỗi.
        /// </summary>
        public string LastWriteError { get; private set; }

        /// <summary>Số lần ghi thất bại kể từ khi tạo đối tượng.</summary>
        public int WriteFailureCount { get; private set; }

        #endregion

        #region Khởi tạo

        /// <summary>
        /// Dùng thư mục mặc định: &lt;thư mục chương trình&gt;\Logs
        /// </summary>
        public LogService()
            : this(GetDefaultLogFolder())
        {
        }

        /// <param name="logFolder">Thư mục chứa tệp nhật ký. Kiểm thử truyền thư mục tạm vào đây.</param>
        /// <exception cref="ArgumentException">Đường dẫn rỗng.</exception>
        public LogService(string logFolder)
        {
            if (string.IsNullOrEmpty(logFolder) || logFolder.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định thư mục chứa nhật ký.", "logFolder");
            }

            LogFolder = logFolder;
            LastWriteError = string.Empty;
            WriteFailureCount = 0;

            // KHÔNG tạo thư mục ở đây. Thư mục chỉ được tạo lúc ghi bản ghi đầu tiên
            // (xem EnsureFolderExists), vì hai lẽ:
            // - Chỉ mở chương trình rồi tắt thì không để lại thư mục rỗng nào.
            // - Constructor chạy ngay khi tạo MainForm; nếu tạo thư mục ở đây mà thiếu
            //   quyền ghi thì chương trình không mở lên được, dù người dùng chỉ muốn xem.
        }

        /// <summary>
        /// Thư mục mặc định. Dùng AppDomain.CurrentDomain.BaseDirectory,
        /// KHÔNG dùng Application.StartupPath (để không phụ thuộc WinForms).
        /// </summary>
        public static string GetDefaultLogFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFolderName);
        }

        #endregion

        #region Tên tệp theo ngày

        /// <summary>
        /// Đường dẫn tệp nhật ký của một ngày, ví dụ Logs\filemonitor-20260926.log
        /// </summary>
        public string GetLogFilePath(DateTime day)
        {
            // InvariantCulture: tên tệp không phụ thuộc cài đặt ngôn ngữ của máy
            // (có lịch như lịch Phật giáo Thái sẽ ra năm 2569 thay vì 2026).
            string fileName = FilePrefix
                + day.ToString(FileDateFormat, CultureInfo.InvariantCulture)
                + FileExtension;

            return Path.Combine(LogFolder, fileName);
        }

        /// <summary>
        /// Đọc ngày từ tên tệp. Trả về false nếu tên tệp không đúng mẫu
        /// (ví dụ tệp người dùng tự bỏ vào thư mục Logs).
        /// </summary>
        private static bool TryParseDayFromFileName(string filePath, out DateTime day)
        {
            day = DateTime.MinValue;

            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            string name = Path.GetFileNameWithoutExtension(filePath);

            if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string datePart = name.Substring(FilePrefix.Length);

            // TryParseExact với đúng định dạng: "filemonitor-backup.log" hay
            // "filemonitor-2026.log" đều bị loại, không bị hiểu nhầm thành một ngày.
            return DateTime.TryParseExact(datePart, FileDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out day);
        }

        /// <summary>
        /// Liệt kê các ngày đang có tệp nhật ký, tăng dần.
        /// </summary>
        /// <remarks>
        /// Dùng để biết có dữ liệu từ ngày nào tới ngày nào, và cho ClearAll.
        /// </remarks>
        public List<DateTime> GetAvailableDays()
        {
            List<DateTime> days = new List<DateTime>();

            if (!Directory.Exists(LogFolder))
            {
                return days;
            }

            string[] files = Directory.GetFiles(LogFolder, FilePrefix + "*" + FileExtension);

            foreach (string file in files)
            {
                DateTime day;
                if (TryParseDayFromFileName(file, out day))
                {
                    days.Add(day);
                }
            }

            // Directory.GetFiles không cam kết thứ tự trả về nên phải tự sắp xếp.
            days.Sort();
            return days;
        }

        #endregion

        #region Ghi

        /// <summary>
        /// Ghi thêm một bản ghi vào tệp của NGÀY TRONG BẢN GHI (entry.Time),
        /// không phải ngày hiện tại.
        /// </summary>
        /// <remarks>
        /// Lấy theo entry.Time để sự kiện xảy ra lúc 23:59:59.9 nhưng tới lúc 00:00:00.1
        /// mới được ghi vẫn nằm đúng tệp của ngày hôm trước.
        /// </remarks>
        /// <returns>true nếu ghi thành công; false nếu lỗi, xem LastWriteError.</returns>
        public bool TryAppend(FileEventLog entry)
        {
            if (entry == null)
            {
                return false;
            }

            lock (fileLock)
            {
                try
                {
                    EnsureFolderExists();

                    // append = true: tệp chưa có thì StreamWriter tự tạo, có rồi thì ghi nối
                    // vào cuối. Vì vậy sang ngày mới là tự sinh tệp mới, không cần code riêng.
                    // UTF8Encoding(false): không ghi BOM, tránh BOM lặp lại giữa tệp.
                    using (StreamWriter writer = new StreamWriter(
                        GetLogFilePath(entry.Time), true, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(entry.ToLogLine());
                    }

                    return true;
                }
                catch (IOException ex)
                {
                    // Đĩa đầy, tệp đang bị chương trình khác khóa...
                    RecordWriteFailure(ex);
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Chương trình đặt trong thư mục không có quyền ghi, ví dụ Program Files.
                    RecordWriteFailure(ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Ghi nhận một lần ghi thất bại. Phải gọi khi đang giữ fileLock.
        /// </summary>
        private void RecordWriteFailure(Exception ex)
        {
            WriteFailureCount++;
            LastWriteError = ex != null ? ex.Message : string.Empty;
        }

        /// <summary>
        /// Tạo thư mục Logs nếu chưa có. Phải gọi khi đang giữ fileLock.
        /// </summary>
        private void EnsureFolderExists()
        {
            // Kiểm tra lại ở MỖI lần ghi chứ không chỉ lần đầu: người dùng có thể xóa
            // thư mục Logs trong lúc chương trình đang chạy.
            // CreateDirectory tạo luôn các thư mục cha còn thiếu, và không báo lỗi
            // nếu thư mục đã tồn tại; câu if chỉ để tránh một lời gọi hệ thống thừa.
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }
        }

        #endregion

        #region Đọc

        /// <summary>
        /// Đọc toàn bộ bản ghi trong khoảng ngày [from, to], cũ trước mới sau.
        /// Chỉ mở những tệp thuộc khoảng ngày đó.
        /// </summary>
        public List<FileEventLog> ReadRange(DateTime from, DateTime to)
        {
            // TODO (bước 5):
            //   - đổi chỗ nếu from > to
            //   - duyệt day từ from.Date tới to.Date (day = day.AddDays(1)):
            //       entries.AddRange(ReadFile(GetLogFilePath(day)));
            //   Vì tên tệp đã là ngày nên KHÔNG cần sắp xếp lại sau khi gộp.
            throw new NotImplementedException();
        }

        /// <summary>
        /// Đọc một tệp. Tệp không có → danh sách rỗng. Dòng hỏng → bỏ qua.
        /// </summary>
        private List<FileEventLog> ReadFile(string filePath)
        {
            // TODO (bước 5):
            //   - lock (fileLock): nếu tệp không tồn tại → trả về rỗng;
            //       lines = File.ReadAllLines(filePath, Encoding.UTF8)
            //   - NGOÀI khóa: duyệt lines, FileEventLog.TryParse → thêm vào danh sách
            //   Phân tích dòng nằm ngoài khóa để luồng watcher không phải chờ lâu.
            throw new NotImplementedException();
        }

        #endregion

        #region Tìm kiếm và lọc (chuyển từ MainForm sang)

        /// <summary>
        /// Đọc và lọc trong một bước. Kết quả sắp xếp mới nhất lên đầu, sẵn để hiển thị.
        /// </summary>
        public List<FileEventLog> Query(LogFilter filter)
        {
            // TODO (bước 7):
            //   - filter == null → dùng new LogFilter()
            //   - entries = ReadRange(filter.FromDate, filter.ToDate)
            //   - result = Filter(entries, filter); result.Reverse(); return result;
            throw new NotImplementedException();
        }

        /// <summary>
        /// Lọc một danh sách có sẵn trong bộ nhớ, không đụng tới đĩa.
        /// </summary>
        /// <remarks>
        /// Tách riêng để MainForm dùng khi người dùng gõ tìm kiếm: lọc lại trên danh sách
        /// đã tải, không đọc lại tệp mỗi lần nhấn phím. Hàm tĩnh, kiểm thử được độc lập.
        /// </remarks>
        public static List<FileEventLog> Filter(IEnumerable<FileEventLog> entries, LogFilter filter)
        {
            // TODO (bước 6):
            //   - entries == null → danh sách rỗng
            //   - filter == null → trả về bản sao của entries
            //   - ngược lại: giữ những entry có filter.Matches(entry) == true
            //   Trả về List MỚI, không sửa danh sách đầu vào.
            throw new NotImplementedException();
        }

        #endregion

        #region Xuất và xóa

        /// <summary>
        /// Xuất danh sách ra tệp CSV để mở bằng Excel.
        /// </summary>
        /// <exception cref="ArgumentException">Đường dẫn đích rỗng.</exception>
        public void ExportCsv(string destinationPath, IList<FileEventLog> entries)
        {
            // TODO (bước 8): chép lại gần như nguyên bản cũ:
            //   - dòng tiêu đề "Thời gian,Loại sự kiện,Tên tệp,Đường dẫn,Đường dẫn cũ"
            //   - mỗi ô bọc qua CsvField
            //   - File.WriteAllText(..., new UTF8Encoding(true))  // có BOM cho Excel
            throw new NotImplementedException();
        }

        /// <summary>
        /// Xóa mọi tệp nhật ký. Trả về số tệp đã xóa.
        /// </summary>
        /// <remarks>
        /// Chỉ xóa tệp khớp mẫu tên (qua GetAvailableDays), không xóa tệp lạ
        /// người dùng tự bỏ vào thư mục Logs.
        /// </remarks>
        public int ClearAll()
        {
            // TODO (bước 9):
            //   - lock (fileLock): foreach day in GetAvailableDays() → File.Delete(GetLogFilePath(day))
            //   - Lưu ý: GetAvailableDays không tự khóa nên gọi bên trong lock là an toàn
            //     (lock trong C# cho phép cùng một luồng vào lại).
            throw new NotImplementedException();
        }

        /// <summary>
        /// Bọc một ô theo quy tắc CSV: đặt trong nháy kép, nháy kép bên trong nhân đôi.
        /// </summary>
        private static string CsvField(string value)
        {
            // TODO (bước 8): chép từ bản cũ
            throw new NotImplementedException();
        }

        #endregion
    }
}
