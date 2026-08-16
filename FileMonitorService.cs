using System;
using System.IO;

namespace FileMonitorApps
{
    /// <summary>
    /// Dữ liệu kèm theo khi phát hiện một thay đổi.
    /// </summary>
    internal class FileEventOccurredEventArgs : EventArgs
    {
        /// <summary>Bản ghi mô tả thay đổi vừa phát hiện.</summary>
        public FileEventLog Entry { get; private set; }

        public FileEventOccurredEventArgs(FileEventLog entry)
        {
            Entry = entry;
        }
    }

    /// <summary>
    /// Dữ liệu kèm theo khi bản thân việc theo dõi gặp sự cố.
    /// </summary>
    internal class MonitorErrorEventArgs : EventArgs
    {
        /// <summary>Ngoại lệ gây ra sự cố, có thể null.</summary>
        public Exception Error { get; private set; }

        public MonitorErrorEventArgs(Exception error)
        {
            Error = error;
        }
    }

    /// <summary>
    /// Bao bọc FileSystemWatcher: chịu trách nhiệm bật, tắt và cấu hình việc theo dõi
    /// một thư mục, rồi phát sự kiện mỗi khi có thay đổi.
    /// </summary>
    /// <remarks>
    /// Lớp này KHÔNG tham chiếu tới Windows Forms và không đụng tới control nào.
    /// Nhờ vậy phần lõi của chương trình kiểm thử được độc lập, và nếu sau này
    /// muốn làm thêm bản chạy nền (Windows Service) thì dùng lại được nguyên vẹn.
    ///
    /// QUAN TRỌNG: các sự kiện EventOccurred và ErrorOccurred được phát trên
    /// LUỒNG NỀN của FileSystemWatcher, không phải luồng giao diện. Bên sử dụng
    /// phải tự chuyển về luồng giao diện (Invoke/BeginInvoke) trước khi cập nhật control.
    /// Việc chuyển luồng cố tình để ở phía giao diện, vì lớp này không được biết
    /// nó đang phục vụ WinForms hay một môi trường nào khác.
    /// </remarks>
    internal class FileMonitorService : IDisposable
    {
        /// <summary>Kích thước bộ đệm khi chỉ theo dõi một thư mục (16 KB).</summary>
        private const int BufferSizeSingleFolder = 16 * 1024;

        /// <summary>Kích thước bộ đệm khi theo dõi cả cây thư mục con (64 KB - mức tối đa).</summary>
        private const int BufferSizeRecursive = 64 * 1024;

        /// <summary>Mẫu lọc mặc định khi bên gọi không chỉ định.</summary>
        private const string DefaultFilter = "*.*";

        private FileSystemWatcher watcher;

        /// <summary>
        /// Phát mỗi khi phát hiện một thay đổi. Chạy trên luồng nền.
        /// </summary>
        public event EventHandler<FileEventOccurredEventArgs> EventOccurred;

        /// <summary>
        /// Phát khi việc theo dõi gặp sự cố (tràn bộ đệm, mất thư mục...). Chạy trên luồng nền.
        /// </summary>
        public event EventHandler<MonitorErrorEventArgs> ErrorOccurred;

        /// <summary>Đang theo dõi hay không.</summary>
        public bool IsRunning
        {
            get { return watcher != null && watcher.EnableRaisingEvents; }
        }

        /// <summary>Thư mục đang theo dõi, chuỗi rỗng nếu chưa chạy.</summary>
        public string FolderPath
        {
            get { return watcher != null ? watcher.Path : string.Empty; }
        }

        /// <summary>Mẫu lọc đang áp dụng, chuỗi rỗng nếu chưa chạy.</summary>
        public string Filter
        {
            get { return watcher != null ? watcher.Filter : string.Empty; }
        }

        /// <summary>Có theo dõi cả thư mục con hay không.</summary>
        public bool IncludeSubdirectories
        {
            get { return watcher != null && watcher.IncludeSubdirectories; }
        }

        /// <summary>Kích thước bộ đệm đang dùng, 0 nếu chưa chạy.</summary>
        public int BufferSize
        {
            get { return watcher != null ? watcher.InternalBufferSize : 0; }
        }

        #region Bật / tắt theo dõi

        /// <summary>
        /// Bắt đầu theo dõi một thư mục. Nếu đang chạy thì phiên cũ được dừng trước.
        /// </summary>
        /// <param name="folderPath">Thư mục cần theo dõi.</param>
        /// <param name="filter">Mẫu lọc phần mở rộng, ví dụ "*.txt". Để trống nghĩa là mọi tệp.</param>
        /// <param name="includeSubdirectories">Có theo dõi cả thư mục con hay không.</param>
        /// <exception cref="ArgumentException">Đường dẫn rỗng.</exception>
        /// <exception cref="DirectoryNotFoundException">Thư mục không tồn tại.</exception>
        public void Start(string folderPath, string filter, bool includeSubdirectories)
        {
            if (string.IsNullOrEmpty(folderPath) || folderPath.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định thư mục cần theo dõi.", "folderPath");
            }

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException("Thư mục không tồn tại: " + folderPath);
            }

            // Dọn phiên trước (nếu có) để không bao giờ có hai bộ theo dõi cùng chạy.
            Stop();

            watcher = new FileSystemWatcher();
            watcher.Path = folderPath;
            watcher.Filter = string.IsNullOrEmpty(filter) ? DefaultFilter : filter;

            // Bật tùy chọn này làm số sự kiện tăng theo cấp số nhân với độ sâu cây thư mục.
            watcher.IncludeSubdirectories = includeSubdirectories;

            // NotifyFilter quyết định thay đổi nào được coi là đáng báo.
            // Chỉ đăng ký những loại cần thiết để giảm bớt sự kiện nhiễu.
            // Đây là cách giảm tải rẻ nhất, nên làm trước khi nghĩ tới việc nới bộ đệm.
            watcher.NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size;

            // Hệ điều hành lưu tạm các thay đổi vào một bộ đệm trước khi báo cho chương trình.
            // Khi thư mục thay đổi dồn dập, bộ đệm mặc định (8 KB) có thể bị tràn; khi đó
            // sự kiện Error được phát và MỘT SỐ THAY ĐỔI BỊ MẤT HẲN, không lấy lại được.
            //
            // Bộ đệm nằm trong vùng nhớ non-paged của hệ điều hành nên đặt càng lớn càng tốn,
            // vì vậy chỉ nới lên mức tối đa khi thực sự cần: lúc theo dõi cả cây thư mục con.
            watcher.InternalBufferSize = includeSubdirectories
                ? BufferSizeRecursive
                : BufferSizeSingleFolder;

            watcher.Error += Watcher_Error;
            watcher.Renamed += Watcher_Renamed;

            // (Bước sau sẽ đăng ký thêm Created / Changed / Deleted.)

            watcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Dừng theo dõi và giải phóng tài nguyên. Gọi được nhiều lần mà không gây lỗi.
        /// </summary>
        public void Stop()
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Error -= Watcher_Error;
                watcher.Renamed -= Watcher_Renamed;
                watcher.Dispose();
            }
            catch (Exception)
            {
                // Đang trong quá trình dọn dẹp nên bỏ qua lỗi phát sinh,
                // điều quan trọng là tham chiếu được đặt lại về null ở dưới.
            }
            finally
            {
                watcher = null;
            }
        }

        /// <summary>
        /// Giải phóng tài nguyên khi không dùng nữa.
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        #endregion

        #region Phát sự kiện

        /// <summary>
        /// Phát sự kiện báo có thay đổi. Bỏ qua nếu chưa ai đăng ký nghe.
        /// </summary>
        protected void RaiseEventOccurred(FileEventLog entry)
        {
            if (entry == null)
            {
                return;
            }

            // Chụp lại tham chiếu trước khi kiểm tra null: người nghe có thể hủy đăng ký
            // từ một luồng khác ngay giữa lúc kiểm tra và lúc gọi.
            EventHandler<FileEventOccurredEventArgs> handler = EventOccurred;
            if (handler != null)
            {
                handler(this, new FileEventOccurredEventArgs(entry));
            }
        }

        /// <summary>
        /// Phát sự kiện báo sự cố.
        /// </summary>
        protected void RaiseErrorOccurred(Exception error)
        {
            EventHandler<MonitorErrorEventArgs> handler = ErrorOccurred;
            if (handler != null)
            {
                handler(this, new MonitorErrorEventArgs(error));
            }
        }

        /// <summary>
        /// Xử lý sự kiện đổi tên tệp hoặc thư mục.
        /// </summary>
        /// <remarks>
        /// Đây là sự kiện duy nhất mang theo hai đường dẫn: FullPath là tên mới,
        /// OldFullPath là tên trước khi đổi. RenamedEventArgs kế thừa từ
        /// FileSystemEventArgs, nên nếu vô ý dùng chung một handler cho mọi loại
        /// sự kiện thì phần đường dẫn cũ sẽ bị bỏ mất — vì vậy sự kiện này
        /// có handler riêng.
        ///
        /// Windows chỉ báo Renamed khi tệp được đổi tên trong cùng một thư mục.
        /// Di chuyển tệp sang thư mục khác sẽ thành một cặp Deleted + Created,
        /// kể cả khi tên tệp không đổi.
        /// </remarks>
        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            RaiseEventOccurred(FileEventLog.FromRenamedEvent(e));
        }

        /// <summary>
        /// Xử lý sự cố do chính FileSystemWatcher báo lên.
        /// </summary>
        /// <remarks>
        /// Chỉ phát sự kiện chứ không tự gọi Stop() ở đây, vì hàm này đang chạy trên
        /// luồng nền của watcher — giải phóng đối tượng ngay bên trong lời gọi lại
        /// của chính nó là việc nên tránh. Bên sử dụng gọi Stop() sau khi đã
        /// chuyển về luồng của mình.
        /// </remarks>
        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            RaiseErrorOccurred(e != null ? e.GetException() : null);
        }

        #endregion
    }
}
