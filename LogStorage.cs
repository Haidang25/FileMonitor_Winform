using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FileMonitorApps
{
    /// <summary>
    /// Một dòng nhật ký: mô tả một thay đổi đã được ghi nhận.
    /// </summary>
    internal class LogEntry
    {
        public DateTime Time { get; set; }
        public string EventType { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
    }

    /// <summary>
    /// Chịu trách nhiệm đọc/ghi tệp nhật ký trên đĩa.
    /// Lớp này không tham chiếu tới bất kỳ control nào nên có thể kiểm thử độc lập
    /// và tái sử dụng cho cả tab Giám sát lẫn tab Nhật ký.
    /// </summary>
    internal static class LogStorage
    {
        private const string LogFolderName = "Logs";
        private const string LogFileName = "filemonitor.log";

        /// <summary>Định dạng thời gian dùng trong tệp nhật ký (không phụ thuộc ngôn ngữ máy).</summary>
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>Ký tự ngăn cách các cột. Dùng TAB vì đường dẫn Windows không chứa TAB.</summary>
        private const char Separator = '\t';

        private static string logFilePath;

        /// <summary>
        /// Đường dẫn đầy đủ tới tệp nhật ký, nằm trong thư mục con "Logs"
        /// cạnh tệp chương trình để người dùng dễ tìm.
        /// </summary>
        public static string LogFilePath
        {
            get
            {
                if (logFilePath == null)
                {
                    logFilePath = Path.Combine(
                        Path.Combine(Application.StartupPath, LogFolderName),
                        LogFileName);
                }
                return logFilePath;
            }
        }

        /// <summary>
        /// Ghi thêm một dòng vào cuối tệp nhật ký, tự tạo thư mục nếu chưa có.
        /// </summary>
        public static void Append(LogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            string folder = Path.GetDirectoryName(LogFilePath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string line = string.Join(Separator.ToString(), new string[]
            {
                entry.Time.ToString(TimeFormat, CultureInfo.InvariantCulture),
                Sanitize(entry.EventType),
                Sanitize(entry.FileName),
                Sanitize(entry.FullPath)
            });

            // Mở ở chế độ ghi nối để không phải giữ tệp mở suốt quá trình giám sát.
            using (StreamWriter writer = new StreamWriter(LogFilePath, true, new UTF8Encoding(false)))
            {
                writer.WriteLine(line);
            }
        }

        /// <summary>
        /// Đọc toàn bộ nhật ký theo thứ tự đã ghi (cũ trước, mới sau).
        /// Dòng hỏng sẽ bị bỏ qua thay vì làm hỏng cả lần đọc.
        /// </summary>
        public static List<LogEntry> ReadAll()
        {
            List<LogEntry> entries = new List<LogEntry>();

            if (!File.Exists(LogFilePath))
            {
                return entries;
            }

            foreach (string line in File.ReadAllLines(LogFilePath, Encoding.UTF8))
            {
                LogEntry entry = ParseLine(line);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// Xóa toàn bộ nội dung nhật ký (giữ lại tệp rỗng).
        /// </summary>
        public static void Clear()
        {
            if (File.Exists(LogFilePath))
            {
                File.WriteAllText(LogFilePath, string.Empty, new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// Xuất danh sách nhật ký ra tệp CSV để mở bằng Excel.
        /// </summary>
        /// <param name="destinationPath">Đường dẫn tệp CSV cần tạo.</param>
        /// <param name="entries">Danh sách dòng nhật ký cần xuất.</param>
        public static void ExportCsv(string destinationPath, IList<LogEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Thời gian,Loại sự kiện,Tên tệp,Đường dẫn");

            if (entries != null)
            {
                foreach (LogEntry entry in entries)
                {
                    builder.AppendLine(string.Join(",", new string[]
                    {
                        CsvField(entry.Time.ToString(TimeFormat, CultureInfo.InvariantCulture)),
                        CsvField(entry.EventType),
                        CsvField(entry.FileName),
                        CsvField(entry.FullPath)
                    }));
                }
            }

            // Ghi kèm BOM để Excel nhận đúng UTF-8, nếu không tiếng Việt sẽ bị lỗi font.
            File.WriteAllText(destinationPath, builder.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// Tách một dòng trong tệp nhật ký thành đối tượng LogEntry.
        /// Trả về null nếu dòng không đúng định dạng.
        /// </summary>
        private static LogEntry ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            string[] parts = line.Split(Separator);
            if (parts.Length < 4)
            {
                return null;
            }

            DateTime time;
            if (!DateTime.TryParseExact(parts[0], TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out time))
            {
                return null;
            }

            return new LogEntry
            {
                Time = time,
                EventType = parts[1],
                FileName = parts[2],
                FullPath = parts[3]
            };
        }

        /// <summary>
        /// Loại bỏ ký tự ngăn cách và ký tự xuống dòng để mỗi bản ghi luôn nằm gọn trên một dòng.
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
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
