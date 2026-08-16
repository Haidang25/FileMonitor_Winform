using System;
using System.Collections.Generic;

namespace FileMonitorApps
{
    /// <summary>
    /// Các loại thay đổi mà chương trình ghi nhận.
    /// </summary>
    /// <remarks>
    /// Dùng kiểu liệt kê thay vì chuỗi để trình biên dịch bắt lỗi gõ sai ngay lúc dịch,
    /// thay vì để sai sót lọt tới lúc chạy.
    /// Tên các thành phần trùng với tên trong WatcherChangeTypes của .NET và cũng chính là
    /// chuỗi được ghi xuống tệp nhật ký, nên KHÔNG đổi tên nếu không muốn làm hỏng
    /// những tệp nhật ký đã ghi trước đó.
    /// </remarks>
    internal enum FileEventType
    {
        /// <summary>Tệp hoặc thư mục được tạo mới.</summary>
        Created,

        /// <summary>Nội dung tệp bị sửa đổi.</summary>
        Changed,

        /// <summary>Tệp hoặc thư mục bị xóa.</summary>
        Deleted,

        /// <summary>Tệp hoặc thư mục bị đổi tên.</summary>
        Renamed
    }

    /// <summary>
    /// Các tiện ích đi kèm kiểu liệt kê FileEventType.
    /// </summary>
    /// <remarks>
    /// Gom phần mô tả tiếng Việt về một chỗ: khi cần thêm một loại sự kiện mới
    /// thì chỉ sửa ở tệp này, giao diện tự cập nhật theo.
    /// </remarks>
    internal static class FileEventTypeHelper
    {
        /// <summary>
        /// Trả về mô tả tiếng Việt của một loại sự kiện.
        /// </summary>
        public static string GetDisplayName(FileEventType eventType)
        {
            switch (eventType)
            {
                case FileEventType.Created:
                    return "Tạo mới";
                case FileEventType.Changed:
                    return "Sửa đổi";
                case FileEventType.Deleted:
                    return "Xóa";
                case FileEventType.Renamed:
                    return "Đổi tên";
                default:
                    return eventType.ToString();
            }
        }

        /// <summary>
        /// Trả về nhãn đầy đủ dùng cho ComboBox, dạng "Created — Tạo mới".
        /// </summary>
        public static string GetFullLabel(FileEventType eventType)
        {
            return eventType + " — " + GetDisplayName(eventType);
        }

        /// <summary>
        /// Liệt kê toàn bộ loại sự kiện theo đúng thứ tự khai báo.
        /// Dùng để dựng danh sách trên giao diện mà không phải viết tay từng mục.
        /// </summary>
        public static IEnumerable<FileEventType> GetAll()
        {
            return (FileEventType[])Enum.GetValues(typeof(FileEventType));
        }
    }
}
