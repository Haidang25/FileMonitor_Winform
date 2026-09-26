using System;
using System.Threading;

namespace FileMonitorApps
{
    /// <summary>
    /// Dữ liệu kèm theo khi phiên giám sát bị dừng do sự cố.
    /// </summary>
    internal class MonitorFaultEventArgs : EventArgs
    {
        /// <summary>Nhóm sự cố.</summary>
        public MonitorErrorKind Kind { get; private set; }

        /// <summary>Ngoại lệ gốc, có thể null.</summary>
        public Exception Error { get; private set; }

        /// <summary>Thư mục đang giám sát lúc xảy ra sự cố.</summary>
        public string FolderPath { get; private set; }

        /// <summary>Lời giải thích đầy đủ, viết sẵn cho người dùng.</summary>
        public string Description
        {
            get { return MonitorErrorClassifier.DescribeFault(Kind); }
        }

        /// <param name="kind">Nhóm sự cố.</param>
        /// <param name="error">Ngoại lệ gốc, có thể null.</param>
        /// <param name="folderPath">Thư mục đang giám sát.</param>
        public MonitorFaultEventArgs(MonitorErrorKind kind, Exception error, string folderPath)
        {
            Kind = kind;
            Error = error;
            FolderPath = folderPath ?? string.Empty;
        }
    }

    /// <summary>
    /// Một phiên giám sát: nối bộ theo dõi (FileMonitorService) với nhật ký (LogService)
    /// và bộ đếm (EventCounter), đồng thời giữ tình trạng của phiên.
    /// </summary>
    /// <remarks>
    /// Đây là phần "nghiệp vụ" trước đây nằm rải rác trong MainForm:
    /// - Phát hiện thay đổi thì GHI NHẬT KÝ và ĐẾM (chức năng D1, C2).
    /// - Tràn bộ đệm thì đếm số lần bỏ sót và giám sát tiếp.
    /// - Sự cố nghiêm trọng thì TỰ DỪNG, phân loại nguyên nhân, và chỉ báo một lần.
    /// - Tính số lần ghi nhật ký thất bại của riêng phiên này.
    ///
    /// MainForm giờ chỉ còn: ra lệnh Start/Stop, nghe sự kiện, chuyển về luồng giao diện
    /// và hiển thị. Lớp này KHÔNG tham chiếu Windows Forms; thay WinForms bằng WPF hay
    /// bản chạy nền thì dùng lại nguyên vẹn.
    ///
    /// QUAN TRỌNG: mọi sự kiện của lớp này được phát trên LUỒNG NỀN. Bên giao diện phải tự
    /// chuyển về luồng giao diện (Invoke/BeginInvoke) trước khi đụng tới control.
    /// </remarks>
    internal class MonitoringSession : IDisposable
    {
        /// <summary>Bộ theo dõi thư mục (bọc FileSystemWatcher).</summary>
        private readonly FileMonitorService monitor;
        /// <summary>Nơi ghi nhật ký. Dùng chung với tab Nhật ký của giao diện.</summary>
        private readonly LogService logService;
        /// <summary>Bộ đếm sự kiện của phiên hiện tại.</summary>
        private readonly EventCounter counter = new EventCounter();

        /// <summary>
        /// Khóa cho Start/Stop. FileMonitorService không an toàn đa luồng, trong khi phiên có
        /// thể bị dừng từ hai phía cùng lúc: người dùng bấm Dừng (luồng giao diện) và sự cố
        /// tự dừng (luồng thread pool).
        /// </summary>
        private readonly object sync = new object();

        /// <summary>
        /// Phiên đang chạy hay không. volatile để luồng nền luôn đọc được giá trị mới nhất
        /// do luồng giao diện ghi, không cần khóa.
        /// </summary>
        private volatile bool running;
        /// <summary>Số lần tràn bộ đệm; tăng bằng Interlocked vì được ghi từ luồng nền.</summary>
        private int overflowCount;
        /// <summary>
        /// Số lần ghi lỗi của LogService lúc bắt đầu phiên. LogService đếm dồn từ khi chương trình
        /// mở; lấy hiệu với mốc này để ra số lỗi của riêng phiên hiện tại.
        /// </summary>
        private int writeFailuresAtStart;

        /// <summary>
        /// Bằng 1 khi sự cố của phiên hiện tại đã được xử lý; chặn xử lý (và báo) hai lần.
        /// </summary>
        private int faultHandled;

        /// <summary>
        /// Số thứ tự của phiên, tăng mỗi lần Start. Việc tự dừng do sự cố được đẩy sang luồng
        /// khác nên chạy trễ một chút; nếu trong lúc đó người dùng đã bắt đầu phiên MỚI thì
        /// không được dừng nhầm phiên mới ấy.
        /// </summary>
        private int generation;

        /// <summary>Sự cố đã làm phiên gần nhất tự dừng; None nếu không có.</summary>
        private volatile MonitorErrorKind lastFault = MonitorErrorKind.None;
        /// <summary>Đã giải phóng hay chưa; chỉ đọc/ghi khi đang giữ sync.</summary>
        private bool disposed;

        /// <summary>
        /// Phát sau khi một thay đổi đã được ghi nhật ký và đếm. Chạy trên LUỒNG NỀN.
        /// </summary>
        public event FileEventDetectedEventHandler EventRecorded;

        /// <summary>
        /// Phát khi bộ đệm của hệ điều hành bị tràn (một số thay đổi bị mất), phiên vẫn chạy tiếp.
        /// Chạy trên LUỒNG NỀN.
        /// </summary>
        public event EventHandler EventsMissed;

        /// <summary>
        /// Phát SAU KHI phiên đã tự dừng do sự cố. Mỗi phiên phát tối đa một lần.
        /// Chạy trên LUỒNG NỀN.
        /// </summary>
        public event EventHandler<MonitorFaultEventArgs> Faulted;

        /// <summary>Tạo phiên giám sát ghi nhật ký vào logService.</summary>
        /// <param name="logService">Nơi ghi nhật ký.</param>
        public MonitoringSession(LogService logService)
            : this(logService, new FileMonitorService())
        {
        }

        /// <param name="logService">Nơi ghi nhật ký.</param>
        /// <param name="monitor">Bộ theo dõi; tách ra để kiểm thử có thể truyền đối tượng riêng.</param>
        internal MonitoringSession(LogService logService, FileMonitorService monitor)
        {
            if (logService == null)
            {
                throw new ArgumentNullException("logService");
            }

            if (monitor == null)
            {
                throw new ArgumentNullException("monitor");
            }

            this.logService = logService;
            this.monitor = monitor;
            FolderPath = string.Empty;

            monitor.FileEventDetected += Monitor_FileEventDetected;
            monitor.ErrorOccurred += Monitor_ErrorOccurred;
        }

        #region Tình trạng của phiên

        /// <summary>Phiên đang chạy hay không.</summary>
        public bool IsRunning
        {
            get { return running; }
        }

        /// <summary>Thư mục của phiên gần nhất.</summary>
        public string FolderPath { get; private set; }

        /// <summary>Số sự kiện đã phát hiện trong phiên, tách theo loại.</summary>
        public EventCounter Counter
        {
            get { return counter; }
        }

        /// <summary>Số lần tràn bộ đệm (mỗi lần là một khoảng bị thiếu dữ liệu).</summary>
        public int OverflowCount
        {
            get { return Volatile.Read(ref overflowCount); }
        }

        /// <summary>Số lần ghi nhật ký thất bại của riêng phiên này.</summary>
        public int WriteFailureCount
        {
            get { return logService.WriteFailureCount - writeFailuresAtStart; }
        }

        /// <summary>Lỗi ghi nhật ký gần nhất.</summary>
        public string LastWriteError
        {
            get { return logService.LastWriteError; }
        }

        /// <summary>Thư mục chứa nhật ký.</summary>
        public string LogFolder
        {
            get { return logService.LogFolder; }
        }

        /// <summary>
        /// Lý do ngắn gọn khiến phiên gần nhất tự dừng; rỗng nếu phiên đang chạy
        /// hoặc được dừng bình thường bằng nút Dừng.
        /// </summary>
        public string LastFaultReason
        {
            get
            {
                return lastFault == MonitorErrorKind.None
                    ? string.Empty
                    : MonitorErrorClassifier.ShortReason(lastFault);
            }
        }

        #endregion

        #region Bắt đầu / dừng

        /// <summary>
        /// Bắt đầu một phiên mới: đặt lại mọi bộ đếm rồi khởi động bộ theo dõi.
        /// </summary>
        /// <remarks>
        /// Đặt lại bộ đếm TRƯỚC khi khởi động, vì sự kiện có thể đến ngay khi watcher bật.
        /// Khởi động thất bại thì dọn sạch rồi ném lại ngoại lệ cho giao diện báo lỗi;
        /// dùng MonitorErrorClassifier.DescribeStartError để giải thích.
        /// </remarks>
        public void Start(string folderPath, string filter, bool includeSubdirectories)
        {
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException("MonitoringSession");
                }

                Interlocked.Increment(ref generation);
                counter.Reset();
                Interlocked.Exchange(ref overflowCount, 0);
                Interlocked.Exchange(ref faultHandled, 0);
                writeFailuresAtStart = logService.WriteFailureCount;
                lastFault = MonitorErrorKind.None;
                FolderPath = folderPath ?? string.Empty;

                running = true;
                try
                {
                    monitor.Start(folderPath, filter, includeSubdirectories);
                }
                catch
                {
                    running = false;
                    monitor.Stop();
                    throw;
                }
            }
        }

        /// <summary>
        /// Dừng phiên theo yêu cầu của người dùng. Gọi nhiều lần không gây lỗi.
        /// </summary>
        public void Stop()
        {
            lock (sync)
            {
                running = false;
                lastFault = MonitorErrorKind.None;
                monitor.Stop();
            }
        }

        /// <summary>
        /// Dừng phiên, hủy đăng ký sự kiện và giải phóng bộ theo dõi. Gọi nhiều lần không gây lỗi.
        /// </summary>
        /// <remarks>
        /// Bỏ hết các phương thức đã đăng ký vào sự kiện: không bỏ thì phiên còn giữ tham chiếu
        /// tới Form, Form không được thu hồi, và một sự kiện đến muộn có thể gọi vào Form đã đóng.
        /// </remarks>
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                running = false;
                monitor.FileEventDetected -= Monitor_FileEventDetected;
                monitor.ErrorOccurred -= Monitor_ErrorOccurred;
                monitor.Dispose();

                EventRecorded = null;
                EventsMissed = null;
                Faulted = null;
                disposed = true;
            }
        }

        #endregion

        #region Xử lý sự kiện của bộ theo dõi (chạy trên luồng nền)

        /// <summary>
        /// Có thay đổi: ghi nhật ký, đếm, rồi báo cho giao diện.
        /// </summary>
        /// <remarks>
        /// Ghi nhật ký ngay trên luồng nền của watcher, trước khi chuyển sang giao diện:
        /// thao tác đĩa mà đẩy sang luồng giao diện thì mỗi thay đổi làm cửa sổ khựng một nhịp.
        /// Và nhờ ghi ở đây, nhật ký vẫn đầy đủ kể cả khi giao diện đang bận hoặc đã đóng.
        /// </remarks>
        private void Monitor_FileEventDetected(object sender, FileEventDetectedEventArgs e)
        {
            if (!running || e == null || e.Entry == null)
            {
                return;
            }

            try
            {
                // TryAppend không ném ngoại lệ với các lỗi đĩa thường gặp; lỗi được ghi nhận
                // vào WriteFailureCount / LastWriteError để giao diện báo cho người dùng.
                logService.TryAppend(e.Entry);
            }
            catch (Exception)
            {
                // Lưới an toàn cho lỗi không lường trước: đang ở luồng nền của watcher,
                // ngoại lệ lọt ra ngoài sẽ làm sập cả chương trình.
            }

            counter.Increment(e.EventType);

            FileEventDetectedEventHandler handler = EventRecorded;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Sự cố của bộ theo dõi: tràn bộ đệm thì chạy tiếp, còn lại thì tự dừng phiên.
        /// </summary>
        /// <remarks>
        /// Không dừng ngay trong hàm này: nó đang chạy bên trong lời gọi lại của chính
        /// watcher, giải phóng watcher ở đây là việc nên tránh. Việc dừng được đẩy sang
        /// một luồng khác của thread pool.
        /// </remarks>
        private void Monitor_ErrorOccurred(object sender, MonitorErrorEventArgs e)
        {
            if (!running)
            {
                return;
            }

            Exception error = e != null ? e.Error : null;

            // ĐIỂM KỸ THUẬT ③ (tràn bộ đệm): watcher VẪN CÒN SỐNG, chỉ một số sự kiện đã mất
            // vĩnh viễn. Không dừng (dừng thì mất luôn các sự kiện sau), chỉ đếm và báo.
            if (e != null && e.IsBufferOverflow)
            {
                Interlocked.Increment(ref overflowCount);

                EventHandler missed = EventsMissed;
                if (missed != null)
                {
                    missed(this, EventArgs.Empty);
                }

                return;
            }

            if (Interlocked.Exchange(ref faultHandled, 1) != 0)
            {
                return;
            }

            string folder = FolderPath;
            int faultGeneration = Volatile.Read(ref generation);
            MonitorErrorKind kind = MonitorErrorClassifier.Classify(error, folder);

            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (sync)
                {
                    // Người dùng đã bấm Dừng (hoặc đã bắt đầu phiên khác) trong lúc chờ.
                    if (!running || disposed || faultGeneration != Volatile.Read(ref generation))
                    {
                        return;
                    }

                    running = false;
                    monitor.Stop();
                    lastFault = kind;
                }

                EventHandler<MonitorFaultEventArgs> handler = Faulted;
                if (handler != null)
                {
                    handler(this, new MonitorFaultEventArgs(kind, error, folder));
                }
            });
        }

        #endregion
    }
}
