using System;
using System.Collections.Generic;
using System.IO;

namespace FileMonitorApps
{
    /// <summary>
    /// Dữ liệu kèm theo khi bản thân việc theo dõi gặp sự cố.
    /// </summary>
    internal class MonitorErrorEventArgs : EventArgs
    {
        /// <summary>Ngoại lệ gây ra sự cố, có thể null.</summary>
        public Exception Error { get; private set; }

        /// <summary>
        /// true nếu sự cố là tràn bộ đệm nội bộ của FileSystemWatcher.
        /// </summary>
        /// <remarks>
        /// Phải phân biệt rõ hai nhóm sự cố vì cách xử lý ngược nhau:
        ///
        /// - Tràn bộ đệm (InternalBufferOverflowException): bộ theo dõi VẪN CÒN SỐNG,
        ///   chỉ là một số thay đổi đã bị mất trong lúc bộ đệm đầy. Dừng giám sát lúc này
        ///   là phản tác dụng, vì sẽ mất luôn cả những thay đổi sau đó.
        ///
        /// - Các sự cố khác (thư mục bị xóa, ổ đĩa mạng ngắt kết nối...): bộ theo dõi
        ///   không còn hoạt động được nữa, phải dừng hẳn.
        ///
        /// Nhờ thuộc tính này mà bên sử dụng không phải tự kiểm tra kiểu ngoại lệ.
        /// </remarks>
        public bool IsBufferOverflow
        {
            get { return Error is InternalBufferOverflowException; }
        }

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
    /// QUAN TRỌNG: các sự kiện FileEventDetected và ErrorOccurred được phát trên
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

        /// <summary>
        /// Khoảng thời gian gộp các sự kiện Changed trùng nhau trên cùng một tệp (mili giây).
        /// </summary>
        /// <remarks>
        /// Một lần lưu tệp thường làm hệ điều hành phát nhiều sự kiện Changed liên tiếp:
        /// phần mềm ghi nội dung, cập nhật kích thước, rồi cập nhật thời gian sửa đổi.
        /// Word và Excel còn ghi qua tệp tạm nên số sự kiện càng nhiều.
        /// Nếu ghi hết thì một thao tác lưu duy nhất tạo ra 3-5 dòng nhật ký giống hệt nhau.
        ///
        /// Đánh đổi: hai lần sửa thật sự cách nhau dưới ngưỡng này sẽ bị tính là một.
        /// 500 ms đủ ngắn để không bỏ sót thao tác của người dùng, đủ dài để gộp một lần lưu.
        /// </remarks>
        private const int ChangeDebounceMilliseconds = 500;

        /// <summary>
        /// Số đường dẫn tối đa được nhớ để so trùng, tránh phình bộ nhớ khi theo dõi lâu.
        /// </summary>
        private const int MaxTrackedPaths = 1000;

        private FileSystemWatcher watcher;

        /// <summary>
        /// Thời điểm gần nhất mỗi tệp phát sinh sự kiện Changed đã được ghi nhận.
        /// </summary>
        private readonly Dictionary<string, DateTime> recentChanges =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Khóa bảo vệ recentChanges: sự kiện đến từ nhiều luồng khác nhau của thread pool.
        /// </summary>
        private readonly object recentChangesLock = new object();

        /// <summary>
        /// Phát mỗi khi phát hiện một thay đổi trong thư mục đang theo dõi.
        /// </summary>
        /// <remarks>
        /// Chạy trên LUỒNG NỀN của FileSystemWatcher. Xem chú thích của lớp.
        /// Sự kiện dùng delegate riêng FileEventDetectedEventHandler.
        /// </remarks>
        public event FileEventDetectedEventHandler FileEventDetected;

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

            ClearRecentChanges();

            watcher.Error += Watcher_Error;
            watcher.Renamed += Watcher_Renamed;
            watcher.Changed += Watcher_Changed;
            watcher.Deleted += Watcher_Deleted;
            watcher.Created += Watcher_Created;

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
                watcher.Changed -= Watcher_Changed;
                watcher.Deleted -= Watcher_Deleted;
                watcher.Created -= Watcher_Created;
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
                ClearRecentChanges();
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
        /// Phát sự kiện FileEventDetected. Bỏ qua nếu chưa ai đăng ký nghe.
        /// </summary>
        /// <remarks>
        /// Đặt tên theo quy ước On + tên sự kiện, và để protected virtual để lớp con
        /// có thể chặn hoặc bổ sung xử lý trước khi sự kiện được phát ra.
        /// </remarks>
        protected virtual void OnFileEventDetected(FileEventLog entry)
        {
            if (entry == null)
            {
                return;
            }

            // Chụp lại tham chiếu trước khi kiểm tra null: người nghe có thể hủy đăng ký
            // từ một luồng khác ngay giữa lúc kiểm tra và lúc gọi.
            FileEventDetectedEventHandler handler = FileEventDetected;
            if (handler != null)
            {
                handler(this, new FileEventDetectedEventArgs(entry));
            }
        }

        /// <summary>
        /// Phát sự kiện báo sự cố.
        /// </summary>
        protected virtual void OnErrorOccurred(Exception error)
        {
            EventHandler<MonitorErrorEventArgs> handler = ErrorOccurred;
            if (handler != null)
            {
                handler(this, new MonitorErrorEventArgs(error));
            }
        }

        /// <summary>
        /// Xử lý sự kiện tệp bị sửa đổi.
        /// </summary>
        /// <remarks>
        /// Sự kiện Changed là loại phát ra dày đặc nhất, nên phải lọc trùng trước khi báo lên.
        /// Xem chú thích của ChangeDebounceMilliseconds để biết lý do.
        /// </remarks>
        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (e == null || !ShouldReportChange(e.FullPath))
            {
                return;
            }

            OnFileEventDetected(FileEventLog.FromFileSystemEvent(e));
        }

        /// <summary>
        /// Quyết định một sự kiện Changed có đáng báo lên hay chỉ là bản trùng
        /// của lần thay đổi vừa xảy ra trên cùng tệp đó.
        /// </summary>
        /// <returns>true nếu nên báo lên.</returns>
        private bool ShouldReportChange(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return false;
            }

            DateTime now = DateTime.Now;

            lock (recentChangesLock)
            {
                DateTime lastTime;
                if (recentChanges.TryGetValue(fullPath, out lastTime))
                {
                    double elapsed = (now - lastTime).TotalMilliseconds;

                    // elapsed < 0 nghĩa là đồng hồ hệ thống vừa bị chỉnh lùi;
                    // khi đó cứ báo lên còn hơn im lặng bỏ qua.
                    if (elapsed >= 0 && elapsed < ChangeDebounceMilliseconds)
                    {
                        return false;
                    }
                }

                recentChanges[fullPath] = now;

                if (recentChanges.Count > MaxTrackedPaths)
                {
                    PruneRecentChanges(now);
                }

                return true;
            }
        }

        /// <summary>
        /// Bỏ bớt các đường dẫn đã quá cũ, không còn tác dụng lọc trùng nữa.
        /// Phải được gọi khi đang giữ recentChangesLock.
        /// </summary>
        private void PruneRecentChanges(DateTime now)
        {
            List<string> expired = new List<string>();

            foreach (KeyValuePair<string, DateTime> item in recentChanges)
            {
                if ((now - item.Value).TotalMilliseconds >= ChangeDebounceMilliseconds)
                {
                    expired.Add(item.Key);
                }
            }

            foreach (string key in expired)
            {
                recentChanges.Remove(key);
            }
        }

        /// <summary>
        /// Ghi nhận thời điểm hiện tại cho một đường dẫn vào lịch sử lọc trùng,
        /// để sự kiện Changed xảy ra ngay sau đó được coi là trùng.
        /// </summary>
        private void RememberPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return;
            }

            DateTime now = DateTime.Now;

            lock (recentChangesLock)
            {
                recentChanges[fullPath] = now;

                if (recentChanges.Count > MaxTrackedPaths)
                {
                    PruneRecentChanges(now);
                }
            }
        }

        /// <summary>
        /// Quên một đường dẫn khỏi lịch sử lọc trùng.
        /// </summary>
        /// <remarks>
        /// Cần gọi khi tệp bị xóa hoặc đổi tên. Nếu không, đường dẫn cũ vẫn còn trong
        /// lịch sử: một tệp bị xóa rồi được tạo lại và sửa ngay trong vòng nửa giây
        /// sẽ bị hiểu nhầm là sự kiện trùng và bị bỏ qua.
        /// </remarks>
        private void ForgetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return;
            }

            lock (recentChangesLock)
            {
                recentChanges.Remove(fullPath);
            }
        }

        /// <summary>
        /// Xóa toàn bộ lịch sử lọc trùng khi bắt đầu hoặc kết thúc một phiên.
        /// </summary>
        private void ClearRecentChanges()
        {
            lock (recentChangesLock)
            {
                recentChanges.Clear();
            }
        }

        /// <summary>
        /// Xử lý sự kiện tệp hoặc thư mục được tạo mới.
        /// </summary>
        /// <remarks>
        /// Tạo một tệp gần như luôn kéo theo một sự kiện Changed ngay sau đó, vì phần mềm
        /// tạo tệp rỗng trước rồi mới ghi nội dung vào. Nếu ghi cả hai thì một thao tác
        /// duy nhất của người dùng sinh ra hai dòng nhật ký, trong đó dòng thứ hai
        /// không cho biết thêm điều gì.
        ///
        /// Vì vậy sau khi báo Created, đường dẫn được ghi vào lịch sử lọc trùng để
        /// sự kiện Changed đi liền ngay sau đó bị gộp vào.
        /// Đánh đổi: nếu người dùng sửa tệp thật sự trong vòng nửa giây kể từ lúc tạo
        /// thì lần sửa đó không được ghi riêng.
        /// </remarks>
        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            RememberPath(e.FullPath);

            OnFileEventDetected(FileEventLog.FromFileSystemEvent(e));
        }

        /// <summary>
        /// Xử lý sự kiện tệp hoặc thư mục bị xóa.
        /// </summary>
        /// <remarks>
        /// Không lọc trùng cho loại này: một tệp chỉ bị xóa được một lần, nên sự kiện
        /// Deleted không phát ra dồn dập như Changed.
        ///
        /// Lưu ý về phạm vi: khi xóa cả một thư mục, số sự kiện nhận được không cố định.
        /// Tùy cách xóa (bỏ vào Thùng rác hay xóa hẳn) mà hệ điều hành có thể chỉ báo một
        /// sự kiện cho chính thư mục đó, hoặc báo thêm cho từng tệp bên trong.
        /// Vì vậy không nên dựa vào giả định "mỗi tệp bị xóa là một dòng nhật ký".
        /// </remarks>
        private void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            // Tệp không còn nữa thì lịch sử lọc trùng của nó cũng hết ý nghĩa.
            ForgetPath(e.FullPath);

            OnFileEventDetected(FileEventLog.FromFileSystemEvent(e));
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
            if (e == null)
            {
                return;
            }

            // Đường dẫn cũ không còn tồn tại nên bỏ khỏi lịch sử lọc trùng.
            ForgetPath(e.OldFullPath);

            OnFileEventDetected(FileEventLog.FromRenamedEvent(e));
        }

        /// <summary>
        /// Xử lý sự cố do chính FileSystemWatcher báo lên.
        /// </summary>
        /// <remarks>
        /// Chỉ phát sự kiện chứ không tự gọi Stop() ở đây, vì hai lý do:
        /// - Hàm này đang chạy trên luồng nền của watcher, giải phóng đối tượng ngay bên
        ///   trong lời gọi lại của chính nó là việc nên tránh.
        /// - Không phải sự cố nào cũng cần dừng: tràn bộ đệm thì bộ theo dõi vẫn chạy được.
        ///   Quyết định dừng hay không thuộc về bên sử dụng, dựa vào IsBufferOverflow.
        /// </remarks>
        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            OnErrorOccurred(e != null ? e.GetException() : null);
        }

        #endregion
    }
}
