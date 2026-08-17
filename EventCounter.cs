using System;
using System.Collections.Generic;
using System.Text;

namespace FileMonitorApps
{
    /// <summary>
    /// Đếm số sự kiện đã ghi nhận trong một phiên giám sát, tách theo từng loại.
    /// </summary>
    /// <remarks>
    /// Tách thành lớp riêng thay vì nuôi bốn biến đếm rời trong MainForm: thêm một loại
    /// sự kiện mới thì không phải sửa gì ở đây, vì danh sách loại được lấy từ
    /// FileEventTypeHelper.GetAll().
    ///
    /// Lớp tự bảo vệ bằng khóa nội bộ. Hiện tại chỉ luồng giao diện gọi tới, nhưng khóa
    /// lại là để nếu về sau có chỗ đếm ngay trên luồng nền thì vẫn đúng.
    /// Lớp không tham chiếu Windows Forms nên kiểm thử được độc lập.
    /// </remarks>
    internal class EventCounter
    {
        private readonly Dictionary<FileEventType, int> counts = new Dictionary<FileEventType, int>();
        private readonly object syncLock = new object();
        private int total;

        /// <summary>Tổng số sự kiện đã đếm.</summary>
        public int Total
        {
            get
            {
                lock (syncLock)
                {
                    return total;
                }
            }
        }

        /// <summary>Số sự kiện của một loại.</summary>
        public int this[FileEventType eventType]
        {
            get { return GetCount(eventType); }
        }

        /// <summary>
        /// Lấy số sự kiện của một loại. Loại chưa xuất hiện lần nào thì trả về 0.
        /// </summary>
        public int GetCount(FileEventType eventType)
        {
            lock (syncLock)
            {
                int value;
                return counts.TryGetValue(eventType, out value) ? value : 0;
            }
        }

        /// <summary>
        /// Ghi nhận thêm một sự kiện.
        /// </summary>
        public void Increment(FileEventType eventType)
        {
            lock (syncLock)
            {
                int value;
                counts[eventType] = counts.TryGetValue(eventType, out value) ? value + 1 : 1;
                total++;
            }
        }

        /// <summary>
        /// Đưa mọi bộ đếm về 0, dùng khi bắt đầu một phiên giám sát mới.
        /// </summary>
        public void Reset()
        {
            lock (syncLock)
            {
                counts.Clear();
                total = 0;
            }
        }

        /// <summary>
        /// Dựng dòng mô tả ngắn để hiển thị trên giao diện, dạng
        /// "Tổng 16 · Tạo mới 8 · Sửa đổi 5 · Xóa 2 · Đổi tên 1".
        /// </summary>
        /// <remarks>
        /// Liệt kê đủ cả bốn loại kể cả loại đang bằng 0, để vị trí các con số không
        /// nhảy qua nhảy lại mỗi khi có loại sự kiện mới xuất hiện.
        /// </remarks>
        public string ToSummary()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Tổng ");
            builder.Append(Total.ToString("N0"));

            foreach (FileEventType eventType in FileEventTypeHelper.GetAll())
            {
                builder.Append("   ·   ");
                builder.Append(FileEventTypeHelper.GetDisplayName(eventType));
                builder.Append(' ');
                builder.Append(GetCount(eventType).ToString("N0"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Dựng mô tả nhiều dòng dùng cho chú thích khi đưa chuột vào.
        /// </summary>
        public string ToDetailedSummary()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Thống kê phiên giám sát hiện tại:");

            foreach (FileEventType eventType in FileEventTypeHelper.GetAll())
            {
                builder.Append(Environment.NewLine);
                builder.Append("  • ");
                builder.Append(eventType);
                builder.Append(" (");
                builder.Append(FileEventTypeHelper.GetDisplayName(eventType));
                builder.Append("): ");
                builder.Append(GetCount(eventType).ToString("N0"));
            }

            builder.Append(Environment.NewLine);
            builder.Append("  ─────");
            builder.Append(Environment.NewLine);
            builder.Append("  Tổng cộng: ");
            builder.Append(Total.ToString("N0"));

            return builder.ToString();
        }
    }
}
