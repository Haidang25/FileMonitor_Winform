using System;
using System.Collections.Generic;

namespace FileMonitorApps
{
    /// <summary>
    /// Chống trùng sự kiện: quyết định một sự kiện vừa đến là thay đổi thật
    /// hay chỉ là bản trùng của lần vừa xảy ra trên cùng đối tượng đó.
    /// </summary>
    /// <remarks>
    /// Vì sao cần lớp này: một lần lưu tệp thường làm hệ điều hành phát nhiều sự kiện
    /// Changed liên tiếp — phần mềm ghi nội dung, cập nhật kích thước, rồi cập nhật thời
    /// gian sửa đổi. Word và Excel còn ghi qua tệp tạm nên số sự kiện càng nhiều.
    /// Ghi hết thì một thao tác Ctrl+S duy nhất tạo ra 3-5 dòng nhật ký giống hệt nhau.
    ///
    /// Cách làm: nhớ thời điểm gần nhất của từng khóa (ở đây khóa là đường dẫn tệp),
    /// và bỏ qua sự kiện đến lại trong vòng IntervalMilliseconds.
    /// Lọc theo TỪNG khóa chứ không theo thời gian toàn cục, nên hai tệp bị sửa cùng lúc
    /// vẫn được báo riêng.
    ///
    /// Đánh đổi: hai lần thay đổi thật sự cách nhau dưới ngưỡng sẽ bị tính là một.
    ///
    /// Lớp tự bảo vệ bằng khóa nội bộ nên gọi được từ nhiều luồng cùng lúc —
    /// điều kiện bắt buộc vì FileSystemWatcher phát sự kiện trên các luồng khác nhau
    /// của thread pool. Lớp không tham chiếu Windows Forms nên kiểm thử được độc lập.
    /// </remarks>
    internal class EventDebouncer
    {
        /// <summary>
        /// Ngưỡng gộp mặc định: 500 ms.
        /// Đủ ngắn để không bỏ sót thao tác của người dùng, đủ dài để gộp một lần lưu tệp.
        /// </summary>
        public const int DefaultIntervalMilliseconds = 500;

        /// <summary>
        /// Số khóa tối đa được nhớ. Vượt quá thì dọn bớt các khóa đã hết hiệu lực,
        /// tránh phình bộ nhớ khi giám sát dài ngày.
        /// </summary>
        public const int DefaultMaxTrackedKeys = 1000;

        private readonly Dictionary<string, DateTime> lastSeen =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly object syncLock = new object();

        /// <summary>Nguồn thời gian. Tách ra để kiểm thử không phải chờ thật.</summary>
        private readonly Func<DateTime> clock;

        private readonly int intervalMilliseconds;
        private readonly int maxTrackedKeys;

        /// <summary>Ngưỡng gộp đang dùng, tính bằng mili giây.</summary>
        public int IntervalMilliseconds
        {
            get { return intervalMilliseconds; }
        }

        /// <summary>Số khóa đang được nhớ. Dùng để theo dõi và kiểm thử.</summary>
        public int TrackedCount
        {
            get
            {
                lock (syncLock)
                {
                    return lastSeen.Count;
                }
            }
        }

        public EventDebouncer()
            : this(DefaultIntervalMilliseconds, DefaultMaxTrackedKeys, null)
        {
        }

        public EventDebouncer(int intervalMilliseconds)
            : this(intervalMilliseconds, DefaultMaxTrackedKeys, null)
        {
        }

        /// <param name="intervalMilliseconds">Ngưỡng gộp. Giá trị âm được đưa về 0.</param>
        /// <param name="maxTrackedKeys">Số khóa tối đa được nhớ.</param>
        /// <param name="clock">Nguồn thời gian, để null nghĩa là dùng DateTime.Now.</param>
        public EventDebouncer(int intervalMilliseconds, int maxTrackedKeys, Func<DateTime> clock)
        {
            this.intervalMilliseconds = intervalMilliseconds > 0 ? intervalMilliseconds : 0;
            this.maxTrackedKeys = maxTrackedKeys > 0 ? maxTrackedKeys : DefaultMaxTrackedKeys;
            this.clock = clock != null ? clock : DefaultClock;
        }

        private static DateTime DefaultClock()
        {
            return DateTime.Now;
        }

        /// <summary>
        /// Hỏi xem một sự kiện có đáng báo lên hay không, đồng thời ghi nhận thời điểm
        /// của lần này để so với các lần sau.
        /// </summary>
        /// <param name="key">Khóa nhận dạng đối tượng, thường là đường dẫn đầy đủ.</param>
        /// <returns>true nếu nên báo lên; false nếu là bản trùng.</returns>
        public bool ShouldReport(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            lock (syncLock)
            {
                // Lấy thời điểm BÊN TRONG khóa. Nếu lấy ở ngoài, hai luồng có thể chụp
                // được hai mốc thời gian rồi mới lần lượt vào khóa theo thứ tự ngược lại;
                // luồng vào sau sẽ tính ra elapsed âm, rơi vào nhánh "đồng hồ bị chỉnh lùi"
                // và được báo lên — thành ra một thay đổi bị ghi hai lần.
                DateTime now = clock();

                DateTime last;
                if (lastSeen.TryGetValue(key, out last))
                {
                    double elapsed = (now - last).TotalMilliseconds;

                    // elapsed < 0 nghĩa là đồng hồ hệ thống vừa bị chỉnh lùi;
                    // khi đó cứ báo lên còn hơn im lặng bỏ qua.
                    if (elapsed >= 0 && elapsed < intervalMilliseconds)
                    {
                        return false;
                    }
                }

                Touch(key, now);
                return true;
            }
        }

        /// <summary>
        /// Ghi nhận thời điểm hiện tại cho một khóa mà không hỏi gì, để sự kiện
        /// đến ngay sau đó bị coi là trùng.
        /// </summary>
        /// <remarks>
        /// Dùng cho sự kiện Created: tạo một tệp gần như luôn kéo theo một sự kiện Changed
        /// ngay sau đó, vì phần mềm tạo tệp rỗng trước rồi mới ghi nội dung vào.
        /// </remarks>
        public void Remember(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (syncLock)
            {
                Touch(key, clock());
            }
        }

        /// <summary>
        /// Quên một khóa.
        /// </summary>
        /// <remarks>
        /// Dùng khi tệp bị xóa hoặc đổi tên. Nếu không quên đi, đường dẫn cũ vẫn còn
        /// trong lịch sử: một tệp bị xóa rồi được tạo lại và sửa ngay trong vòng nửa giây
        /// sẽ bị hiểu nhầm là sự kiện trùng và bị bỏ qua.
        /// </remarks>
        public void Forget(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (syncLock)
            {
                lastSeen.Remove(key);
            }
        }

        /// <summary>
        /// Xóa toàn bộ lịch sử, dùng khi bắt đầu hoặc kết thúc một phiên giám sát.
        /// </summary>
        public void Clear()
        {
            lock (syncLock)
            {
                lastSeen.Clear();
            }
        }

        /// <summary>
        /// Ghi thời điểm cho một khóa và dọn bớt nếu từ điển quá lớn.
        /// Phải được gọi khi đang giữ syncLock.
        /// </summary>
        private void Touch(string key, DateTime now)
        {
            lastSeen[key] = now;

            if (lastSeen.Count > maxTrackedKeys)
            {
                Prune(now);
            }
        }

        /// <summary>
        /// Bỏ các khóa đã quá cũ, không còn tác dụng lọc trùng nữa.
        /// Phải được gọi khi đang giữ syncLock.
        /// </summary>
        private void Prune(DateTime now)
        {
            List<string> expired = new List<string>();

            foreach (KeyValuePair<string, DateTime> item in lastSeen)
            {
                if ((now - item.Value).TotalMilliseconds >= intervalMilliseconds)
                {
                    expired.Add(item.Key);
                }
            }

            foreach (string key in expired)
            {
                lastSeen.Remove(key);
            }
        }
    }
}
