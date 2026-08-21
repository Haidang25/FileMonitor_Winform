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

        /// <summary>
        /// Xuất danh sách nhật ký ra tệp CSV để mở bằng Excel.
        /// </summary>
        /// <param name="destinationPath">Đường dẫn tệp CSV cần tạo.</param>
        /// <param name="entries">Danh sách bản ghi cần xuất.</param>
        /// <exception cref="ArgumentException">Đường dẫn đích rỗng.</exception>
        public void ExportCsv(string destinationPath, IList<FileEventLog> entries)
        {
            if (string.IsNullOrEmpty(destinationPath) || destinationPath.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định tệp CSV cần tạo.", "destinationPath");
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Thời gian,Loại sự kiện,Tên tệp,Đường dẫn,Đường dẫn cũ");

            if (entries != null)
            {
                foreach (FileEventLog entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    builder.AppendLine(string.Join(",", new string[]
                    {
                        CsvField(entry.Time.ToString(FileEventLog.TimeFormat, CultureInfo.InvariantCulture)),
                        CsvField(entry.EventType.ToString()),
                        CsvField(entry.FileName),
                        CsvField(entry.FullPath),
                        CsvField(entry.OldFullPath)
                    }));
                }
            }

            // Ghi kèm BOM để Excel nhận đúng UTF-8, nếu không tiếng Việt sẽ bị lỗi font.
            File.WriteAllText(destinationPath, builder.ToString(), new UTF8Encoding(true));
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
        /// Bọc một ô dữ liệu theo quy tắc CSV: đặt trong dấu nháy kép,
        /// nháy kép bên trong được nhân đôi.
        /// </summary>
        private static string CsvField(string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
