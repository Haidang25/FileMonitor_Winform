using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FileMonitorApps
{
    /// <summary>
    /// Chịu trách nhiệm đọc/ghi tệp nhật ký trên đĩa.
    /// </summary>
    /// <remarks>
    /// Lớp này chỉ lo việc truy cập tệp; phần chuyển đổi giữa bản ghi và dòng văn bản
    /// do chính lớp FileEventLog đảm nhiệm. Tách như vậy để khi đổi định dạng lưu trữ
    /// thì không phải sửa ở đây, và ngược lại.
    /// Lớp không tham chiếu tới control nào nên kiểm thử được độc lập.
    /// </remarks>
    internal static class LogStorage
    {
        private const string LogFolderName = "Logs";
        private const string LogFileName = "filemonitor.log";

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
        /// Ghi thêm một bản ghi vào cuối tệp nhật ký, tự tạo thư mục nếu chưa có.
        /// </summary>
        public static void Append(FileEventLog entry)
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

            // Mở ở chế độ ghi nối để không phải giữ tệp mở suốt quá trình giám sát.
            using (StreamWriter writer = new StreamWriter(LogFilePath, true, new UTF8Encoding(false)))
            {
                writer.WriteLine(entry.ToLogLine());
            }
        }

        /// <summary>
        /// Đọc toàn bộ nhật ký theo thứ tự đã ghi (cũ trước, mới sau).
        /// Dòng hỏng sẽ bị bỏ qua thay vì làm hỏng cả lần đọc.
        /// </summary>
        public static List<FileEventLog> ReadAll()
        {
            List<FileEventLog> entries = new List<FileEventLog>();

            if (!File.Exists(LogFilePath))
            {
                return entries;
            }

            foreach (string line in File.ReadAllLines(LogFilePath, Encoding.UTF8))
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
        /// <param name="entries">Danh sách bản ghi cần xuất.</param>
        public static void ExportCsv(string destinationPath, IList<FileEventLog> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Thời gian,Loại sự kiện,Tên tệp,Đường dẫn,Đường dẫn cũ");

            if (entries != null)
            {
                foreach (FileEventLog entry in entries)
                {
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
