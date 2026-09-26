using System;
using System.IO;
using System.Threading;

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

        /// <summary>
        /// true nếu thư mục đang giám sát không còn tồn tại (bị xóa, đổi tên, di chuyển,
        /// bỏ vào Thùng rác, hoặc ổ USB/ổ mạng chứa nó bị ngắt).
        /// </summary>
        public bool IsFolderLost
        {
            get { return Error is DirectoryNotFoundException; }
        }

        /// <param name="error">Ngoại lệ gây ra sự cố, có thể null.</param>
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
        /// Bộ theo dõi của .NET, bọc cơ chế ReadDirectoryChangesW của Windows.
        /// null khi chưa chạy; mỗi lần Start tạo một đối tượng mới, Stop thì giải phóng.
        /// </summary>
        private FileSystemWatcher watcher;

        /// <summary>
        /// Bộ chống trùng sự kiện. Xem lớp EventDebouncer để biết vì sao cần lọc trùng
        /// và ngưỡng gộp được chọn như thế nào.
        /// </summary>
        private readonly EventDebouncer debouncer = new EventDebouncer();

        /// <summary>Đã giải phóng hay chưa.</summary>
        private bool disposed;

        /// <summary>
        /// Có nhận sự kiện nữa hay không.
        /// </summary>
        /// <remarks>
        /// Đặt EnableRaisingEvents = false KHÔNG có nghĩa là mọi sự kiện đã dừng ngay:
        /// những sự kiện đã được xếp vào hàng đợi thread pool trước đó vẫn sẽ gọi tới
        /// các phương thức xử lý, kể cả sau khi Stop() đã trả về. Nếu không chặn lại,
        /// bên sử dụng sẽ nhận thêm vài sự kiện trong lúc giao diện đã hiện "Chưa giám sát".
        ///
        /// Đánh dấu volatile để mọi luồng đều thấy giá trị mới nhất mà không cần khóa.
        /// </remarks>
        private volatile bool acceptingEvents;

        /// <summary>
        /// Chu kỳ kiểm tra thư mục đang giám sát còn tồn tại hay không.
        /// </summary>
        private const int HealthCheckIntervalMilliseconds = 2000;

        /// <summary>
        /// Bộ hẹn giờ kiểm tra định kỳ thư mục còn tồn tại (xem CheckFolderStillExists).
        /// </summary>
        /// <remarks>
        /// Dùng System.Threading.Timer chứ không dùng Timer của WinForms: lớp này không được
        /// phụ thuộc giao diện. Hàm gọi lại chạy trên thread pool, giống sự kiện của watcher.
        /// </remarks>
        private Timer healthTimer;

        /// <summary>Đường dẫn đang giám sát, lưu riêng để luồng hẹn giờ đọc mà không đụng tới watcher.</summary>
        private string monitoredPath = string.Empty;

        /// <summary>
        /// Bằng 1 khi đã báo "mất thư mục" cho phiên hiện tại. Sự kiện Error của watcher và bộ
        /// hẹn giờ có thể cùng phát hiện một lúc; Interlocked đảm bảo chỉ báo đúng một lần.
        /// </summary>
        private int folderLossReported;

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
        /// <exception cref="ObjectDisposedException">Đối tượng đã bị giải phóng.</exception>
        /// <exception cref="ArgumentException">Đường dẫn rỗng.</exception>
        /// <exception cref="DirectoryNotFoundException">Thư mục không tồn tại.</exception>
        public void Start(string folderPath, string filter, bool includeSubdirectories)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("FileMonitorService");
            }

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
            // ĐIỂM KỸ THUẬT ③ (tràn bộ đệm): nới bộ đệm để giảm khả năng mất sự kiện;
            // khi vẫn tràn thì sự kiện Error được xử lý ở Watcher_Error → MonitoringSession.
            watcher.InternalBufferSize = includeSubdirectories
                ? BufferSizeRecursive
                : BufferSizeSingleFolder;

            debouncer.Clear();

            watcher.Error += Watcher_Error;
            watcher.Renamed += Watcher_Renamed;
            watcher.Changed += Watcher_Changed;
            watcher.Deleted += Watcher_Deleted;
            watcher.Created += Watcher_Created;

            monitoredPath = folderPath;
            Interlocked.Exchange(ref folderLossReported, 0);

            acceptingEvents = true;
            watcher.EnableRaisingEvents = true;

            healthTimer = new Timer(CheckFolderStillExists, null,
                HealthCheckIntervalMilliseconds, HealthCheckIntervalMilliseconds);
        }

        /// <summary>
        /// Dừng theo dõi và giải phóng tài nguyên. Gọi được nhiều lần mà không gây lỗi.
        /// </summary>
        public void Stop()
        {
            // Đóng cổng nhận sự kiện trước tiên, để những sự kiện đang trên đường tới
            // không lọt qua trong lúc đang dọn dẹp.
            acceptingEvents = false;

            if (healthTimer != null)
            {
                healthTimer.Dispose();
                healthTimer = null;
            }

            if (watcher == null)
            {
                debouncer.Clear();
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
                debouncer.Clear();
            }
        }

        /// <summary>
        /// Giải phóng tài nguyên khi không dùng nữa. Gọi nhiều lần không gây lỗi.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);

            // Lớp này không có finalizer, nhưng vẫn gọi theo đúng mẫu Dispose chuẩn
            // để nếu sau này lớp con thêm finalizer thì hành vi vẫn đúng.
            GC.SuppressFinalize(this);
        }

        /// <param name="disposing">
        /// true khi được gọi từ Dispose(); false khi được gọi từ finalizer.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                Stop();

                // Bỏ mọi phương thức đã đăng ký. Không bỏ thì đối tượng này còn giữ
                // tham chiếu tới Form; Form không được thu hồi, và một sự kiện đến muộn
                // có thể gọi vào Form đã đóng.
                FileEventDetected = null;
                ErrorOccurred = null;
            }

            disposed = true;
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
        /// Bổ sung kích thước tệp cho bản ghi trước khi phát sự kiện.
        /// </summary>
        /// <remarks>
        /// FileSizeProbe tự bắt mọi ngoại lệ (tệp bị khóa, đã bị xóa, thiếu quyền...), nên
        /// gọi ở đây không có nguy cơ làm chết luồng của FileSystemWatcher. Không đọc được
        /// thì bản ghi vẫn được phát, chỉ thiếu kích thước kèm ghi chú lý do.
        ///
        /// Kích thước là giá trị TẠI THỜI ĐIỂM phát hiện: với sự kiện Created, phần mềm thường
        /// tạo tệp rỗng trước rồi mới ghi nội dung, nên con số hay là 0 byte.
        /// </remarks>
        private static FileEventLog CreateEntry(FileEventLog entry)
        {
            FileSizeProbe.Fill(entry);
            return entry;
        }

        /// <summary>
        /// Xử lý sự kiện tệp bị sửa đổi.
        /// </summary>
        /// <remarks>
        /// Sự kiện Changed là loại phát ra dày đặc nhất, nên phải lọc trùng trước khi báo lên.
        /// Xem lớp EventDebouncer để biết lý do và ngưỡng gộp.
        /// </remarks>
        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!acceptingEvents)
            {
                return;
            }

            // ĐIỂM KỸ THUẬT ② (sự kiện trùng lặp): chỉ báo lên nếu không phải bản trùng.
            if (e == null || !debouncer.ShouldReport(e.FullPath))
            {
                return;
            }

            OnFileEventDetected(CreateEntry(FileEventLog.FromFileSystemEvent(e)));
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
            if (!acceptingEvents)
            {
                return;
            }

            if (e == null)
            {
                return;
            }

            debouncer.Remember(e.FullPath);

            OnFileEventDetected(CreateEntry(FileEventLog.FromFileSystemEvent(e)));
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
            if (!acceptingEvents)
            {
                return;
            }

            if (e == null)
            {
                return;
            }

            // Tệp không còn nữa thì lịch sử lọc trùng của nó cũng hết ý nghĩa.
            debouncer.Forget(e.FullPath);

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
            if (!acceptingEvents)
            {
                return;
            }

            if (e == null)
            {
                return;
            }

            // Đường dẫn cũ không còn tồn tại nên bỏ khỏi lịch sử lọc trùng.
            debouncer.Forget(e.OldFullPath);

            OnFileEventDetected(CreateEntry(FileEventLog.FromRenamedEvent(e)));
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
            if (!acceptingEvents)
            {
                return;
            }

            Exception error = e != null ? e.GetException() : null;

            // Khi thư mục đang giám sát bị xóa, Windows báo lỗi "Access is denied"
            // (Win32Exception mã 5) — rất dễ bị hiểu nhầm là thiếu quyền. Nếu thư mục thực sự
            // không còn thì đổi thành DirectoryNotFoundException cho đúng bản chất, giữ lỗi gốc
            // ở InnerException để tra cứu.
            if (!(error is InternalBufferOverflowException) && !FolderExists())
            {
                ReportFolderLost(error);
                return;
            }

            OnErrorOccurred(error);
        }

        /// <summary>
        /// Hàm gọi lại của bộ hẹn giờ: báo sự cố nếu thư mục đang giám sát đã biến mất.
        /// </summary>
        /// <remarks>
        /// Vì sao cần kiểm tra định kỳ, không chờ sự kiện Error của watcher:
        /// - Xóa hẳn thư mục (Shift+Delete): Windows thường phát sự kiện Error, nhưng không
        ///   phải phiên bản nào cũng vậy.
        /// - Bỏ vào Thùng rác hoặc di chuyển: về bản chất là ĐỔI TÊN thư mục. Watcher vẫn bám
        ///   theo thư mục ở vị trí mới, KHÔNG báo lỗi gì, và tiếp tục báo sự kiện với đường dẫn
        ///   cũ đã sai. Người dùng tưởng vẫn đang giám sát đúng chỗ.
        /// - Rút ổ USB hoặc mất mạng: có lúc không có sự kiện nào cả.
        /// Kiểm tra Directory.Exists 2 giây một lần rất rẻ và bắt được cả ba trường hợp.
        ///
        /// Hạn chế: nếu thư mục bị chuyển đi rồi một thư mục MỚI cùng tên được tạo lại ngay
        /// trong vòng 2 giây, lần kiểm tra vẫn thấy "còn tồn tại" và không phát hiện được.
        /// </remarks>
        private void CheckFolderStillExists(object state)
        {
            if (!acceptingEvents || FolderExists())
            {
                return;
            }

            ReportFolderLost(null);
        }

        /// <summary>Thư mục đang giám sát còn tồn tại hay không. Không bao giờ ném ngoại lệ.</summary>
        private bool FolderExists()
        {
            // Directory.Exists trả về false thay vì ném ngoại lệ khi có lỗi (kể cả thiếu quyền),
            // nên không cần try/catch.
            return Directory.Exists(monitoredPath);
        }

        /// <summary>
        /// Báo mất thư mục, bảo đảm chỉ báo MỘT lần cho mỗi phiên giám sát.
        /// </summary>
        private void ReportFolderLost(Exception original)
        {
            if (Interlocked.Exchange(ref folderLossReported, 1) != 0)
            {
                return;
            }

            OnErrorOccurred(new DirectoryNotFoundException(
                "Thư mục đang giám sát không còn tồn tại: " + monitoredPath, original));
        }

        #endregion
    }
}
