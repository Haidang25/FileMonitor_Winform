using System;

namespace FileMonitorApps
{
    /// <summary>
    /// Kiểu delegate cho sự kiện FileEventDetected.
    /// </summary>
    /// <param name="sender">Đối tượng phát sự kiện, ở đây là FileMonitorService.</param>
    /// <param name="e">Dữ liệu kèm theo sự kiện.</param>
    /// <remarks>
    /// Khai báo delegate riêng thay vì dùng EventHandler&lt;T&gt; có sẵn để thể hiện rõ
    /// cơ chế của lập trình hướng sự kiện: delegate định nghĩa "khuôn" của phương thức
    /// xử lý, event là chỗ để các phương thức đúng khuôn đó đăng ký vào.
    ///
    /// Chữ ký vẫn giữ đúng quy ước của .NET là (object sender, EventArgs e), nhờ vậy
    /// mọi phương thức xử lý viết theo mẫu thông thường đều gắn vào được.
    /// </remarks>
    internal delegate void FileEventDetectedEventHandler(object sender, FileEventDetectedEventArgs e);

    /// <summary>
    /// Dữ liệu kèm theo khi phát hiện một thay đổi trong thư mục đang theo dõi.
    /// </summary>
    /// <remarks>
    /// Kế thừa EventArgs theo đúng quy ước của .NET. Các thuộc tính chỉ đọc để bên nhận
    /// không sửa được nội dung sự kiện: cùng một đối tượng này được truyền cho mọi
    /// phương thức đã đăng ký, nếu một bên sửa thì các bên sau sẽ nhận dữ liệu đã bị đổi.
    /// </remarks>
    internal class FileEventDetectedEventArgs : EventArgs
    {
        /// <summary>Bản ghi mô tả thay đổi vừa phát hiện.</summary>
        public FileEventLog Entry { get; private set; }

        /// <summary>Thời điểm phát hiện, lấy từ bản ghi cho tiện dùng.</summary>
        public DateTime Time
        {
            get { return Entry != null ? Entry.Time : DateTime.MinValue; }
        }

        /// <summary>Loại thay đổi, lấy từ bản ghi cho tiện dùng.</summary>
        public FileEventType EventType
        {
            get { return Entry != null ? Entry.EventType : FileEventType.Changed; }
        }

        /// <param name="entry">Bản ghi mô tả thay đổi. Không được null.</param>
        /// <exception cref="ArgumentNullException">entry là null.</exception>
        public FileEventDetectedEventArgs(FileEventLog entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException("entry");
            }

            Entry = entry;
        }
    }
}
