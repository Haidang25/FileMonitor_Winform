using System;
using System.Globalization;
using System.IO;

namespace FileMonitorApps
{
    /// <summary>
    /// Một bản ghi nhật ký: mô tả trọn vẹn một thay đổi đã được ghi nhận.
    /// </summary>
    /// <remarks>
    /// Lớp này tự chịu trách nhiệm chuyển mình thành một dòng văn bản và ngược lại
    /// (ToLogLine / TryParse). Nhờ vậy khi cần đổi định dạng lưu trữ thì chỉ sửa ở đây,
    /// còn LogStorage chỉ lo việc đọc ghi tệp.
    /// Lớp không tham chiếu tới control nào nên kiểm thử được độc lập.
    /// </remarks>
    internal class FileEventLog
    {
        /// <summary>Định dạng thời gian trong tệp nhật ký.</summary>
        /// <remarks>
        /// Dùng InvariantCulture để tệp không phụ thuộc ngôn ngữ của máy:
        /// nhật ký ghi trên máy cài tiếng Việt vẫn đọc được trên máy cài tiếng Anh.
        /// </remarks>
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>Ký tự ngăn cách các cột. Dùng TAB vì đường dẫn Windows không chứa TAB.</summary>
        public const char Separator = '\t';

        /// <summary>Thời điểm ghi nhận thay đổi.</summary>
        public DateTime Time { get; set; }

        /// <summary>Loại thay đổi.</summary>
        public FileEventType EventType { get; set; }

        /// <summary>Tên tệp (không kèm đường dẫn).</summary>
        public string FileName { get; set; }

        /// <summary>Đường dẫn đầy đủ tới tệp.</summary>
        public string FullPath { get; set; }

        /// <summary>
        /// Đường dẫn trước khi đổi tên. Chỉ có giá trị với sự kiện Renamed,
        /// các trường hợp khác để chuỗi rỗng.
        /// </summary>
        public string OldFullPath { get; set; }

        public FileEventLog()
        {
            Time = DateTime.Now;
            FileName = string.Empty;
            FullPath = string.Empty;
            OldFullPath = string.Empty;
        }

        #region Tạo bản ghi từ sự kiện của FileSystemWatcher

        /// <summary>
        /// Tạo bản ghi từ sự kiện Created / Changed / Deleted.
        /// </summary>
        public static FileEventLog FromFileSystemEvent(FileSystemEventArgs e)
        {
            if (e == null)
            {
                return null;
            }

            return new FileEventLog
            {
                Time = DateTime.Now,
                EventType = ToEventType(e.ChangeType),
                // Lấy tên tệp từ FullPath chứ không từ Name: với tùy chọn "bao gồm thư mục con",
                // Name có dạng "ThuMucCon\\tep.txt" và FullPath thì luôn được điền đầy đủ.
                FileName = Path.GetFileName(e.FullPath),
                FullPath = e.FullPath,
                OldFullPath = string.Empty
            };
        }

        /// <summary>
        /// Tạo bản ghi từ sự kiện Renamed, giữ lại cả đường dẫn cũ.
        /// </summary>
        public static FileEventLog FromRenamedEvent(RenamedEventArgs e)
        {
            if (e == null)
            {
                return null;
            }

            return new FileEventLog
            {
                Time = DateTime.Now,
                EventType = FileEventType.Renamed,
                FileName = Path.GetFileName(e.FullPath),
                FullPath = e.FullPath,
                OldFullPath = e.OldFullPath != null ? e.OldFullPath : string.Empty
            };
        }

        /// <summary>
        /// Quy đổi WatcherChangeTypes của .NET sang kiểu liệt kê của chương trình.
        /// </summary>
        private static FileEventType ToEventType(WatcherChangeTypes changeType)
        {
            switch (changeType)
            {
                case WatcherChangeTypes.Created:
                    return FileEventType.Created;
                case WatcherChangeTypes.Deleted:
                    return FileEventType.Deleted;
                case WatcherChangeTypes.Renamed:
                    return FileEventType.Renamed;
                default:
                    return FileEventType.Changed;
            }
        }

        #endregion

        #region Chuyển đổi qua lại với một dòng văn bản

        /// <summary>
        /// Chuyển bản ghi thành một dòng để ghi vào tệp nhật ký.
        /// </summary>
        public string ToLogLine()
        {
            return string.Join(Separator.ToString(), new string[]
            {
                Time.ToString(TimeFormat, CultureInfo.InvariantCulture),
                EventType.ToString(),
                Sanitize(FileName),
                Sanitize(FullPath),
                Sanitize(OldFullPath)
            });
        }

        /// <summary>
        /// Đọc một dòng trong tệp nhật ký thành bản ghi.
        /// </summary>
        /// <param name="line">Dòng văn bản cần đọc.</param>
        /// <param name="result">Bản ghi thu được, null nếu dòng không hợp lệ.</param>
        /// <returns>true nếu đọc thành công.</returns>
        public static bool TryParse(string line, out FileEventLog result)
        {
            result = null;

            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            string[] parts = line.Split(Separator);

            // Cột thứ 5 (đường dẫn cũ) là tùy chọn nên chấp nhận dòng chỉ có 4 cột.
            if (parts.Length < 4)
            {
                return false;
            }

            DateTime time;
            if (!DateTime.TryParseExact(parts[0], TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out time))
            {
                return false;
            }

            FileEventType eventType;
            if (!TryParseEventType(parts[1], out eventType))
            {
                return false;
            }

            result = new FileEventLog
            {
                Time = time,
                EventType = eventType,
                FileName = parts[2],
                FullPath = parts[3],
                OldFullPath = parts.Length > 4 ? parts[4] : string.Empty
            };

            return true;
        }

        /// <summary>
        /// Đọc tên loại sự kiện thành kiểu liệt kê.
        /// </summary>
        /// <remarks>
        /// Phải kiểm tra thêm bằng Enum.IsDefined vì Enum.TryParse chấp nhận cả chuỗi số:
        /// giá trị "99" sẽ lọt qua và tạo ra một loại sự kiện không tồn tại.
        /// </remarks>
        private static bool TryParseEventType(string value, out FileEventType eventType)
        {
            eventType = FileEventType.Changed;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            FileEventType parsed;
            if (!Enum.TryParse(value.Trim(), true, out parsed))
            {
                return false;
            }

            if (!Enum.IsDefined(typeof(FileEventType), parsed))
            {
                return false;
            }

            eventType = parsed;
            return true;
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

        #endregion

        /// <summary>
        /// Mô tả ngắn gọn dùng khi gỡ lỗi.
        /// </summary>
        public override string ToString()
        {
            return Time.ToString(TimeFormat, CultureInfo.InvariantCulture) + " " + EventType + " " + FullPath;
        }
    }
}
