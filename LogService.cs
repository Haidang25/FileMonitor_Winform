using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileMonitorApps
{
    /// <summary>
    /// Chịu trách nhiệm đọc và ghi tệp nhật ký trên đĩa.
    /// </summary>
    /// <remarks>
    /// Lớp này chỉ lo việc truy cập tệp; phần chuyển đổi giữa bản ghi và dòng văn bản
    /// do chính lớp FileEventLog đảm nhiệm. Tách như vậy để khi đổi định dạng lưu trữ
    /// thì không phải sửa ở đây, và ngược lại.
    ///
    /// Vì sao là lớp thường chứ không phải lớp static:
    /// - Đường dẫn tệp truyền vào qua constructor, nên kiểm thử chỉ định được tệp tạm
    ///   riêng thay vì cùng ghi vào một tệp dùng chung.
    /// - Không phụ thuộc Windows Forms. Đường dẫn mặc định lấy từ
    ///   AppDomain.CurrentDomain.BaseDirectory chứ không dùng Application.StartupPath.
    ///   Nhờ vậy toàn bộ phần lõi của chương trình sạch khỏi tham chiếu giao diện.
    ///
    /// Khóa ghi tệp là khóa của TỪNG đối tượng. Hai đối tượng LogService cùng trỏ vào
    /// một tệp sẽ không chặn được nhau, nên chương trình chỉ dùng một đối tượng duy nhất
    /// cho suốt vòng đời của mình.
    /// </remarks>
    internal class LogService
    {
        /// <summary>Tên thư mục con chứa nhật ký, nằm cạnh tệp chương trình.</summary>
        public const string DefaultFolderName = "Logs";

        /// <summary>Tên tệp nhật ký mặc định.</summary>
        public const string DefaultFileName = "filemonitor.log";

        /// <summary>
        /// Khóa dùng chung cho mọi thao tác đọc/ghi tệp của đối tượng này.
        /// </summary>
        /// <remarks>
        /// FileSystemWatcher phát sự kiện trên các luồng khác nhau của thread pool,
        /// nên Append có thể được gọi đồng thời từ nhiều luồng. Không khóa lại thì
        /// hai luồng cùng mở tệp sẽ gây lỗi tranh chấp hoặc ghi đè lên nhau.
        /// </remarks>
        private readonly object fileLock = new object();

        /// <summary>Đường dẫn đầy đủ tới tệp nhật ký của đối tượng này.</summary>
        public string LogFilePath { get; private set; }

        /// <summary>Tệp nhật ký đã tồn tại trên đĩa hay chưa.</summary>
        public bool Exists
        {
            get { return File.Exists(LogFilePath); }
        }

        /// <summary>Kích thước tệp nhật ký, tính bằng byte. Trả về 0 nếu tệp chưa có.</summary>
        public long FileSizeBytes
        {
            get
            {
                lock (fileLock)
                {
                    if (!File.Exists(LogFilePath))
                    {
                        return 0;
                    }

                    return new FileInfo(LogFilePath).Length;
                }
            }
        }

        /// <summary>
        /// Tạo đối tượng dùng tệp nhật ký mặc định, nằm trong thư mục con "Logs"
        /// cạnh tệp chương trình để người dùng dễ tìm.
        /// </summary>
        public LogService()
            : this(GetDefaultLogFilePath())
        {
        }

        /// <param name="logFilePath">Đường dẫn tệp nhật ký cần dùng.</param>
        /// <exception cref="ArgumentException">Đường dẫn rỗng.</exception>
        public LogService(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath) || logFilePath.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định đường dẫn tệp nhật ký.", "logFilePath");
            }

            LogFilePath = logFilePath;
        }

        /// <summary>
        /// Đường dẫn tệp nhật ký mặc định: &lt;thư mục chương trình&gt;\Logs\filemonitor.log
        /// </summary>
        public static string GetDefaultLogFilePath()
        {
            return Path.Combine(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFolderName),
                DefaultFileName);
        }

        /// <summary>
        /// Ghi thêm một bản ghi vào cuối tệp nhật ký, tự tạo thư mục nếu chưa có.
        /// </summary>
        public void Append(FileEventLog entry)
        {
            if (entry == null)
            {
                return;
            }

            lock (fileLock)
            {
                EnsureFolderExists();

                // Mở ở chế độ ghi nối để không phải giữ tệp mở suốt quá trình giám sát.
                using (StreamWriter writer = new StreamWriter(LogFilePath, true, new UTF8Encoding(false)))
                {
                    writer.WriteLine(entry.ToLogLine());
                }
            }
        }

        /// <summary>
        /// Ghi thêm nhiều bản ghi trong một lần mở tệp.
        /// </summary>
        /// <remarks>
        /// Dùng khi có sẵn cả lô: mở và đóng tệp một lần thay vì mở lại cho từng bản ghi.
        /// </remarks>
        public void AppendRange(IEnumerable<FileEventLog> entries)
        {
            if (entries == null)
            {
                return;
            }

            lock (fileLock)
            {
                EnsureFolderExists();

                using (StreamWriter writer = new StreamWriter(LogFilePath, true, new UTF8Encoding(false)))
                {
                    foreach (FileEventLog entry in entries)
                    {
                        if (entry != null)
                        {
                            writer.WriteLine(entry.ToLogLine());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Đọc toàn bộ nhật ký theo thứ tự đã ghi (cũ trước, mới sau).
        /// Dòng hỏng sẽ bị bỏ qua thay vì làm hỏng cả lần đọc.
        /// </summary>
        public List<FileEventLog> ReadAll()
        {
            List<FileEventLog> entries = new List<FileEventLog>();

            string[] lines;
            lock (fileLock)
            {
                if (!File.Exists(LogFilePath))
                {
                    return entries;
                }

                lines = File.ReadAllLines(LogFilePath, Encoding.UTF8);
            }

            foreach (string line in lines)
            {
                FileEventLog entry;
                if (FileEventLog.TryParse(line, out entry))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// Xóa toàn bộ nội dung nhật ký (giữ lại tệp rỗng).
        /// </summary>
        public void Clear()
        {
            lock (fileLock)
            {
                if (File.Exists(LogFilePath))
                {
                    File.WriteAllText(LogFilePath, string.Empty, new UTF8Encoding(false));
                }
            }
        }

        /// <summary>Dấu phân cách cột mặc định của tệp CSV.</summary>
        public const char DefaultCsvSeparator = ',';

        /// <summary>
        /// Xuất danh sách ra tệp CSV, dùng dấu phẩy làm dấu phân cách.
        /// </summary>
        /// <returns>Số bản ghi đã xuất.</returns>
        /// <exception cref="ArgumentException">Đường dẫn đích rỗng.</exception>
        public int ExportCsv(string destinationPath, IList<FileEventLog> entries)
        {
            return ExportCsv(destinationPath, entries, DefaultCsvSeparator);
        }

        /// <summary>
        /// Xuất danh sách ra tệp CSV để mở bằng Excel, với dấu phân cách tùy chọn.
        /// </summary>
        /// <param name="destinationPath">Đường dẫn tệp CSV cần tạo (ghi đè nếu đã có).</param>
        /// <param name="entries">Danh sách bản ghi cần xuất.</param>
        /// <param name="separator">Dấu phân cách cột, thường là ',' hoặc ';'.</param>
        /// <returns>Số bản ghi đã xuất.</returns>
        /// <remarks>
        /// Vì sao cho chọn dấu phân cách: khi mở tệp CSV bằng cách nhấp đúp, Excel dùng
        /// "List separator" trong cài đặt vùng của Windows. Máy đặt vùng Việt Nam dùng dấu
        /// phẩy làm dấu thập phân nên List separator là dấu chấm phẩy — gặp tệp phân cách
        /// bằng dấu phẩy, Excel dồn cả dòng vào một cột. Giao diện có thể truyền vào
        /// CultureInfo.CurrentCulture.TextInfo.ListSeparator để khớp với máy người dùng.
        ///
        /// Không dùng dòng "sep=;" ở đầu tệp: Excel hiểu dòng đó nhưng khi gặp nó lại bỏ
        /// qua BOM, làm tiếng Việt bị lỗi font.
        ///
        /// Tệp được ghi kèm BOM (UTF8Encoding(true)): thiếu BOM, Excel đọc tệp theo bảng mã
        /// ANSI và tiếng Việt hiện thành ký tự lạ. Dòng kết thúc bằng CRLF theo RFC 4180.
        ///
        /// Không cần giữ fileLock: hàm chỉ làm việc với danh sách trong bộ nhớ và một
        /// tệp đích do người dùng chọn, không đụng tới tệp nhật ký.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Đường dẫn đích rỗng, hoặc dấu phân cách là nháy kép / ký tự xuống dòng.
        /// </exception>
        public int ExportCsv(string destinationPath, IList<FileEventLog> entries, char separator)
        {
            if (string.IsNullOrEmpty(destinationPath) || destinationPath.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định tệp CSV cần tạo.", "destinationPath");
            }

            if (separator == '"' || separator == '\r' || separator == '\n')
            {
                throw new ArgumentException("Dấu phân cách không hợp lệ.", "separator");
            }

            int count = 0;

            using (StreamWriter writer = new StreamWriter(destinationPath, false, new UTF8Encoding(true)))
            {
                // Tự đặt ký tự xuống dòng, không phụ thuộc Environment.NewLine của hệ điều hành.
                writer.NewLine = "\r\n";

                writer.WriteLine(CsvRow(separator,
                    "Thời gian", "Loại sự kiện", "Tên tệp", "Đường dẫn", "Đường dẫn cũ"));

                if (entries != null)
                {
                    foreach (FileEventLog entry in entries)
                    {
                        if (entry == null)
                        {
                            continue;
                        }

                        writer.WriteLine(CsvRow(separator,
                            entry.Time.ToString(FileEventLog.TimeFormat, CultureInfo.InvariantCulture),
                            entry.EventType.ToString(),
                            entry.FileName,
                            entry.FullPath,
                            entry.OldFullPath));
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Tạo thư mục chứa tệp nhật ký nếu chưa có.
        /// Phải được gọi khi đang giữ fileLock.
        /// </summary>
        private void EnsureFolderExists()
        {
            string folder = Path.GetDirectoryName(LogFilePath);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        /// <summary>
        /// Ghép các ô thành một dòng CSV, mỗi ô đã được xử lý qua CsvField.
        /// </summary>
        private static string CsvRow(char separator, params string[] values)
        {
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(CsvField(values[i], separator));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Xử lý một ô theo quy tắc CSV (RFC 4180) để ký tự đặc biệt không làm lệch cột.
        /// </summary>
        /// <remarks>
        /// Tên tệp và thư mục trên Windows được phép chứa dấu phẩy, dấu chấm phẩy và
        /// dấu nháy đơn, ví dụ D:\Báo cáo, quý 3\bản cuối.txt. Ghi thẳng ra thì dấu phẩy
        /// đó bị hiểu là ranh giới cột, cả dòng bị lệch sang phải.
        ///
        /// Quy tắc áp dụng:
        /// 1. Ô chứa dấu phân cách, nháy kép, CR hoặc LF → bọc trong nháy kép.
        /// 2. Nháy kép bên trong ô → nhân đôi ("" ). Windows cấm nháy kép trong tên tệp,
        ///    nhưng vẫn xử lý vì hàm này không nên phụ thuộc vào giả định đó.
        /// 3. Ô bắt đầu bằng = + - @ (hoặc TAB, CR) → thêm dấu nháy đơn ở đầu.
        ///    Đây là chống "CSV injection": Excel coi ô như vậy là CÔNG THỨC và thực thi nó.
        ///    Một tệp tên "=HYPERLINK(...).txt" là tên hợp lệ trên Windows; không chặn thì chỉ
        ///    cần mở tệp CSV xuất ra là Excel chạy công thức do người khác đặt vào tên tệp.
        ///    Với công cụ giám sát thì tên tệp là dữ liệu do BẤT KỲ AI tạo ra, nên phải coi
        ///    là không tin cậy.
        ///    Đánh đổi: tệp tên "-nhap.txt" sẽ hiện là "'-nhap.txt" trong Excel.
        ///
        /// Ô không có gì đặc biệt thì giữ nguyên, không bọc nháy kép, cho tệp gọn và dễ đọc
        /// bằng Notepad.
        /// </remarks>
        internal static string CsvField(string value, char separator)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (IsFormulaTrigger(value[0]))
            {
                value = "'" + value;
            }

            bool mustQuote = value.IndexOf(separator) >= 0
                          || value.IndexOf('"') >= 0
                          || value.IndexOf('\r') >= 0
                          || value.IndexOf('\n') >= 0;

            if (!mustQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Ký tự đầu ô khiến Excel / LibreOffice hiểu ô là công thức.
        /// </summary>
        private static bool IsFormulaTrigger(char c)
        {
            return c == '=' || c == '+' || c == '-' || c == '@' || c == '\t' || c == '\r';
        }
    }
}
