using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FileMonitorApps
{
    /// <summary>
    /// Ghi, đọc, tìm kiếm, lọc và xuất nhật ký giám sát.
    /// </summary>
    /// <remarks>
    /// Đã hoàn thiện đủ 9 bước của khung Tuần 5.
    ///
    /// Thay đổi so với bản LogService hiện tại:
    ///   1. Mỗi ngày một tệp: Logs\filemonitor-yyyyMMdd.log (checklist F).
    ///      Đọc theo khoảng ngày thì chỉ mở đúng những tệp cần, không đọc cả lịch sử.
    ///   2. Tìm kiếm / lọc chuyển từ MainForm sang đây (checklist G:
    ///      "MainForm.cs không chứa logic nghiệp vụ").
    ///   3. Ghi lỗi không còn bị nuốt im lặng: TryAppend trả về false và
    ///      LastWriteError cho biết lý do, để MainForm báo cho người dùng.
    ///
    /// Không đổi:
    ///   - Chuyển bản ghi ↔ dòng văn bản vẫn do FileEventLog lo (ToLogLine / TryParse).
    ///   - Khóa ghi tệp là khóa của từng đối tượng → cả chương trình chỉ dùng MỘT đối tượng.
    ///   - Không tham chiếu Windows Forms.
    /// </remarks>
    internal class LogService
    {
        #region Hằng số

        /// <summary>Tên thư mục con chứa nhật ký, nằm cạnh tệp chương trình.</summary>
        public const string DefaultFolderName = "Logs";

        /// <summary>Tiền tố tên tệp nhật ký.</summary>
        public const string FilePrefix = "filemonitor-";

        /// <summary>Phần mở rộng tệp nhật ký.</summary>
        public const string FileExtension = ".log";

        /// <summary>
        /// Định dạng ngày trong tên tệp. Dạng năm-tháng-ngày để sắp xếp theo tên
        /// cũng chính là sắp xếp theo thời gian.
        /// </summary>
        public const string FileDateFormat = "yyyyMMdd";

        /// <summary>
        /// Tên tệp nhật ký của phiên bản cũ (một tệp duy nhất, trước khi tách theo ngày).
        /// </summary>
        public const string LegacyFileName = "filemonitor.log";

        /// <summary>Đuôi thêm vào tệp cũ sau khi đã chuyển xong dữ liệu.</summary>
        public const string LegacyImportedSuffix = ".imported";

        #endregion

        #region Trường và thuộc tính

        /// <summary>
        /// Khóa dùng chung cho mọi thao tác đọc/ghi tệp của đối tượng này.
        /// FileSystemWatcher gọi Append từ nhiều luồng thread pool cùng lúc.
        /// </summary>
        private readonly object fileLock = new object();

        /// <summary>Thư mục chứa các tệp nhật ký.</summary>
        public string LogFolder { get; private set; }

        /// <summary>
        /// Mô tả lỗi của lần ghi thất bại gần nhất, rỗng nếu chưa từng lỗi.
        /// </summary>
        public string LastWriteError { get; private set; }

        /// <summary>Số lần ghi thất bại kể từ khi tạo đối tượng.</summary>
        public int WriteFailureCount { get; private set; }

        #endregion

        #region Khởi tạo

        /// <summary>
        /// Dùng thư mục mặc định: &lt;thư mục chương trình&gt;\Logs
        /// </summary>
        public LogService()
            : this(GetDefaultLogFolder())
        {
        }

        /// <param name="logFolder">Thư mục chứa tệp nhật ký. Kiểm thử truyền thư mục tạm vào đây.</param>
        /// <exception cref="ArgumentException">Đường dẫn rỗng.</exception>
        public LogService(string logFolder)
        {
            if (string.IsNullOrEmpty(logFolder) || logFolder.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định thư mục chứa nhật ký.", "logFolder");
            }

            LogFolder = logFolder;
            LastWriteError = string.Empty;
            WriteFailureCount = 0;
            LastReadError = string.Empty;
            LastReadSkippedLines = 0;

            // KHÔNG tạo thư mục ở đây. Thư mục chỉ được tạo lúc ghi bản ghi đầu tiên
            // (xem EnsureFolderExists), vì hai lẽ:
            // - Chỉ mở chương trình rồi tắt thì không để lại thư mục rỗng nào.
            // - Constructor chạy ngay khi tạo MainForm; nếu tạo thư mục ở đây mà thiếu
            //   quyền ghi thì chương trình không mở lên được, dù người dùng chỉ muốn xem.
        }

        /// <summary>
        /// Thư mục mặc định. Dùng AppDomain.CurrentDomain.BaseDirectory,
        /// KHÔNG dùng Application.StartupPath (để không phụ thuộc WinForms).
        /// </summary>
        public static string GetDefaultLogFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFolderName);
        }

        #endregion

        #region Tên tệp theo ngày

        /// <summary>
        /// Đường dẫn tệp nhật ký của một ngày, ví dụ Logs\filemonitor-20260926.log
        /// </summary>
        public string GetLogFilePath(DateTime day)
        {
            // InvariantCulture: tên tệp không phụ thuộc cài đặt ngôn ngữ của máy
            // (có lịch như lịch Phật giáo Thái sẽ ra năm 2569 thay vì 2026).
            string fileName = FilePrefix
                + day.ToString(FileDateFormat, CultureInfo.InvariantCulture)
                + FileExtension;

            return Path.Combine(LogFolder, fileName);
        }

        /// <summary>
        /// Đọc ngày từ tên tệp. Trả về false nếu tên tệp không đúng mẫu
        /// (ví dụ tệp người dùng tự bỏ vào thư mục Logs).
        /// </summary>
        private static bool TryParseDayFromFileName(string filePath, out DateTime day)
        {
            day = DateTime.MinValue;

            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            string name = Path.GetFileNameWithoutExtension(filePath);

            if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string datePart = name.Substring(FilePrefix.Length);

            // TryParseExact với đúng định dạng: "filemonitor-backup.log" hay
            // "filemonitor-2026.log" đều bị loại, không bị hiểu nhầm thành một ngày.
            return DateTime.TryParseExact(datePart, FileDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out day);
        }

        /// <summary>
        /// Liệt kê các ngày đang có tệp nhật ký, tăng dần.
        /// </summary>
        /// <remarks>
        /// Dùng để biết có dữ liệu từ ngày nào tới ngày nào, và cho ClearAll.
        /// </remarks>
        public List<DateTime> GetAvailableDays()
        {
            List<DateTime> days = new List<DateTime>();

            if (!Directory.Exists(LogFolder))
            {
                return days;
            }

            string[] files = Directory.GetFiles(LogFolder, FilePrefix + "*" + FileExtension);

            foreach (string file in files)
            {
                DateTime day;
                if (TryParseDayFromFileName(file, out day))
                {
                    days.Add(day);
                }
            }

            // Directory.GetFiles không cam kết thứ tự trả về nên phải tự sắp xếp.
            days.Sort();
            return days;
        }

        #endregion

        #region Chuyển dữ liệu từ phiên bản cũ

        /// <summary>
        /// Chuyển nhật ký của phiên bản cũ (Logs\filemonitor.log) sang các tệp theo ngày.
        /// </summary>
        /// <returns>Số bản ghi đã chuyển; 0 nếu không có tệp cũ.</returns>
        /// <remarks>
        /// Chỉ chạy một lần: chuyển xong thì tệp cũ được ĐỔI TÊN thành
        /// filemonitor.log.imported chứ không xóa. Nếu có gì sai, dữ liệu gốc vẫn còn nguyên
        /// để kiểm tra lại; đổi tên cũng đảm bảo lần mở chương trình sau không chuyển lặp.
        ///
        /// Mỗi bản ghi được ghi vào đúng tệp của ngày trong bản ghi (qua TryAppendRange),
        /// nối vào cuối tệp ngày nếu tệp đó đã có. Tệp ngày do bản mới ghi luôn có thời gian
        /// muộn hơn tệp cũ, nên thứ tự trong tệp có thể không tăng dần ở chỗ nối — không
        /// ảnh hưởng gì vì bảng hiển thị không dựa vào thứ tự đó để lọc.
        ///
        /// Nếu ghi không đủ (đĩa đầy, thiếu quyền) thì KHÔNG đổi tên tệp cũ, để lần sau thử lại.
        /// Chấp nhận khả năng lần thử lại sinh bản ghi trùng: trùng còn hơn mất.
        /// </remarks>
        public int ImportLegacyLog()
        {
            string legacyPath = Path.Combine(LogFolder, LegacyFileName);

            if (!File.Exists(legacyPath))
            {
                return 0;
            }

            List<FileEventLog> entries = new List<FileEventLog>();
            foreach (string line in ReadLines(legacyPath))
            {
                FileEventLog entry;
                if (FileEventLog.TryParse(line, out entry))
                {
                    entries.Add(entry);
                }
            }

            int written = TryAppendRange(entries);
            if (written != entries.Count)
            {
                return written;
            }

            lock (fileLock)
            {
                string target = legacyPath + LegacyImportedSuffix;

                // Đã có bản .imported từ trước (hiếm) thì thêm mốc thời gian cho khỏi trùng tên.
                if (File.Exists(target))
                {
                    target = legacyPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture) + LegacyImportedSuffix;
                }

                File.Move(legacyPath, target);
            }

            return written;
        }

        #endregion

        #region Ghi

        /// <summary>
        /// Số lần thử mở tệp khi tệp đang bị chương trình khác giữ tạm thời.
        /// </summary>
        private const int MaxOpenAttempts = 3;

        /// <summary>Thời gian chờ giữa hai lần thử mở tệp, tính bằng mili giây.</summary>
        private const int RetryDelayMilliseconds = 50;

        /// <summary>
        /// Ghi thêm một bản ghi vào cuối tệp của NGÀY TRONG BẢN GHI (entry.Time),
        /// không phải ngày hiện tại.
        /// </summary>
        /// <remarks>
        /// Lấy theo entry.Time để sự kiện xảy ra lúc 23:59:59.9 nhưng tới lúc 00:00:00.1
        /// mới được ghi vẫn nằm đúng tệp của ngày hôm trước.
        ///
        /// Gọi được đồng thời từ nhiều luồng: FileSystemWatcher phát sự kiện trên
        /// nhiều luồng của thread pool, và mỗi luồng đều gọi vào đây.
        /// </remarks>
        /// <returns>true nếu ghi thành công; false nếu lỗi, xem LastWriteError.</returns>
        public bool TryAppend(FileEventLog entry)
        {
            if (entry == null)
            {
                return false;
            }

            return TryAppendRange(new FileEventLog[] { entry }) == 1;
        }

        /// <summary>
        /// Ghi thêm nhiều bản ghi. Mỗi tệp ngày chỉ mở MỘT lần cho cả lô.
        /// </summary>
        /// <remarks>
        /// Dùng khi có sẵn cả lô, ví dụ khi thư mục thay đổi dồn dập: mở/đóng tệp là
        /// thao tác tốn kém nhất của việc ghi, gộp lại thì đỡ hẳn.
        /// Lô vắt qua nửa đêm sẽ được tách ra hai tệp, mỗi tệp giữ đúng thứ tự ban đầu.
        /// </remarks>
        /// <returns>Số bản ghi đã ghi thành công.</returns>
        public int TryAppendRange(IEnumerable<FileEventLog> entries)
        {
            if (entries == null)
            {
                return 0;
            }

            // Bước 1 — chuẩn bị NGOÀI khóa: chuyển bản ghi thành dòng và nhóm theo tệp.
            // Việc này chỉ dùng CPU, không đụng đĩa, nên không có lý do bắt luồng khác chờ.
            // Dùng List các nhóm (thay vì chỉ Dictionary) để giữ thứ tự tệp như thứ tự
            // bản ghi đầu vào — Dictionary không cam kết thứ tự duyệt.
            Dictionary<string, List<string>> linesByFile =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            List<string> fileOrder = new List<string>();

            foreach (FileEventLog entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                string path = GetLogFilePath(entry.Time);

                List<string> lines;
                if (!linesByFile.TryGetValue(path, out lines))
                {
                    lines = new List<string>();
                    linesByFile.Add(path, lines);
                    fileOrder.Add(path);
                }

                lines.Add(entry.ToLogLine());
            }

            if (fileOrder.Count == 0)
            {
                return 0;
            }

            // Bước 2 — ghi TRONG khóa. Khóa bao trọn từ lúc mở tới lúc đóng tệp, nên
            // tại một thời điểm chỉ một luồng được ghi: các dòng không bao giờ chen vào
            // giữa nhau, và không có hai luồng cùng mở tệp để ghi (sẽ ném IOException).
            int written = 0;

            lock (fileLock)
            {
                try
                {
                    EnsureFolderExists();
                }
                catch (IOException ex)
                {
                    RecordWriteFailure(ex);
                    return 0;
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Chương trình đặt trong thư mục không có quyền ghi, ví dụ Program Files.
                    RecordWriteFailure(ex);
                    return 0;
                }

                foreach (string path in fileOrder)
                {
                    List<string> lines = linesByFile[path];

                    if (AppendLines(path, lines))
                    {
                        written += lines.Count;
                    }
                }
            }

            return written;
        }

        /// <summary>
        /// Ghi nối các dòng vào cuối một tệp. Phải gọi khi đang giữ fileLock.
        /// </summary>
        /// <returns>true nếu ghi đủ; false nếu lỗi (đã ghi nhận vào LastWriteError).</returns>
        private bool AppendLines(string path, List<string> lines)
        {
            FileStream stream = OpenForAppend(path);
            if (stream == null)
            {
                return false;
            }

            try
            {
                // UTF8Encoding(false): không ghi BOM. Nếu có BOM, mỗi lần mở tệp đã có
                // dữ liệu lại chèn thêm BOM vào giữa tệp.
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    foreach (string line in lines)
                    {
                        writer.WriteLine(line);
                    }
                }

                return true;
            }
            catch (IOException ex)
            {
                // Lỗi giữa chừng lúc đang ghi (thường là đĩa đầy). KHÔNG thử lại ở đây:
                // một phần các dòng có thể đã xuống đĩa, ghi lại sẽ sinh dòng trùng.
                RecordWriteFailure(ex);
                return false;
            }
        }

        /// <summary>
        /// Mở tệp ở chế độ ghi nối, thử lại vài lần nếu tệp đang bị giữ tạm thời.
        /// Phải gọi khi đang giữ fileLock.
        /// </summary>
        /// <returns>Luồng tệp đã mở, hoặc null nếu không mở được.</returns>
        /// <remarks>
        /// FileMode.Append: tệp chưa có thì tự tạo, có rồi thì con trỏ ghi đặt sẵn ở cuối.
        /// Vì vậy sang ngày mới là tự sinh tệp mới, không cần code riêng.
        ///
        /// FileShare.Read: trong lúc đang ghi, chương trình khác (Notepad, Excel) vẫn
        /// ĐỌC được tệp, nhưng không ai ghi chen vào được.
        ///
        /// Vì sao thử lại: phần mềm diệt virus hoặc công cụ sao lưu hay mở tệp vừa thay đổi
        /// trong chốc lát. Chỉ thử lại lúc MỞ tệp — khi đó chưa có dòng nào được ghi, nên
        /// thử lại không bao giờ sinh dòng trùng.
        ///
        /// lock chỉ chặn được các luồng trong CÙNG chương trình. Nếu mở hai cửa sổ
        /// FileMonitor cùng ghi một thư mục Logs, FileShare.Read là thứ chặn chúng ghi
        /// đè lên nhau: bên đến sau không mở được tệp và nhận IOException.
        /// </remarks>
        private FileStream OpenForAppend(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                }
                catch (IOException ex)
                {
                    if (attempt >= MaxOpenAttempts)
                    {
                        RecordWriteFailure(ex);
                        return null;
                    }

                    // Đang giữ khóa nên các luồng khác phải chờ theo. Tổng thời gian chờ
                    // tối đa (MaxOpenAttempts - 1) * RetryDelayMilliseconds = 100 ms,
                    // đủ ngắn để không làm tràn bộ đệm của FileSystemWatcher.
                    Thread.Sleep(RetryDelayMilliseconds);
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Thiếu quyền thì chờ bao lâu cũng vậy, không thử lại.
                    RecordWriteFailure(ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// Ghi nhận một lần ghi thất bại. Phải gọi khi đang giữ fileLock.
        /// </summary>
        private void RecordWriteFailure(Exception ex)
        {
            WriteFailureCount++;
            LastWriteError = ex != null ? ex.Message : string.Empty;
        }

        /// <summary>
        /// Tạo thư mục Logs nếu chưa có. Phải gọi khi đang giữ fileLock.
        /// </summary>
        private void EnsureFolderExists()
        {
            // Kiểm tra lại ở MỖI lần ghi chứ không chỉ lần đầu: người dùng có thể xóa
            // thư mục Logs trong lúc chương trình đang chạy.
            // CreateDirectory tạo luôn các thư mục cha còn thiếu, và không báo lỗi
            // nếu thư mục đã tồn tại; câu if chỉ để tránh một lời gọi hệ thống thừa.
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }
        }

        #endregion

        #region Đọc

        /// <summary>
        /// Số dòng bị bỏ qua ở lần đọc gần nhất vì không đúng định dạng.
        /// </summary>
        /// <remarks>
        /// Khác 0 nghĩa là tệp nhật ký có dòng hỏng (bị sửa tay, hoặc chương trình tắt
        /// đột ngột giữa lúc ghi). Giao diện có thể dùng con số này để báo cho người dùng
        /// thay vì im lặng làm như không có gì.
        /// </remarks>
        public int LastReadSkippedLines { get; private set; }

        /// <summary>
        /// Mô tả các tệp không đọc được ở lần đọc gần nhất, rỗng nếu đọc được hết.
        /// </summary>
        public string LastReadError { get; private set; }

        /// <summary>
        /// Đọc toàn bộ nhật ký của mọi ngày, cũ trước mới sau.
        /// </summary>
        public List<FileEventLog> ReadAll()
        {
            return ReadRange(DateTime.MinValue, DateTime.MaxValue);
        }

        /// <summary>
        /// Đọc toàn bộ bản ghi trong khoảng ngày [from, to], cũ trước mới sau.
        /// Chỉ mở những tệp thuộc khoảng ngày đó.
        /// </summary>
        /// <remarks>
        /// Duyệt theo danh sách tệp ĐANG CÓ (GetAvailableDays) chứ không duyệt từng ngày
        /// từ from tới to:
        /// - Khoảng 7 ngày mà chỉ 2 ngày có giám sát thì chỉ đụng tới 2 tệp.
        /// - ReadAll truyền vào MinValue..MaxValue; duyệt từng ngày sẽ là ~3,6 triệu vòng lặp.
        ///
        /// Tệp nào không đọc được thì bỏ qua tệp đó và ghi lý do vào LastReadError,
        /// các ngày còn lại vẫn được trả về. Một tệp hỏng không được làm mất cả lần tải.
        ///
        /// Thứ tự: GetAvailableDays đã sắp xếp tăng dần, trong mỗi tệp các dòng nằm theo
        /// thứ tự ghi, nên KHÔNG cần sắp xếp lại sau khi gộp.
        /// </remarks>
        public List<FileEventLog> ReadRange(DateTime from, DateTime to)
        {
            if (from > to)
            {
                DateTime swap = from;
                from = to;
                to = swap;
            }

            DateTime firstDay = from.Date;
            DateTime lastDay = to.Date;

            List<FileEventLog> entries = new List<FileEventLog>();
            List<string> errors = new List<string>();
            int skippedLines = 0;

            foreach (DateTime day in GetAvailableDays())
            {
                if (day < firstDay || day > lastDay)
                {
                    continue;
                }

                string path = GetLogFilePath(day);

                string[] lines;
                try
                {
                    lines = ReadLines(path);
                }
                catch (IOException ex)
                {
                    errors.Add(Path.GetFileName(path) + ": " + ex.Message);
                    continue;
                }
                catch (UnauthorizedAccessException ex)
                {
                    errors.Add(Path.GetFileName(path) + ": " + ex.Message);
                    continue;
                }

                // Phân tích dòng NGOÀI khóa: ReadLines đã trả khóa ngay khi đọc xong,
                // nên luồng watcher không phải chờ trong lúc đang phân tích hàng nghìn dòng.
                foreach (string line in lines)
                {
                    // Dòng trống (thường là dòng cuối tệp) không tính là dòng hỏng.
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    FileEventLog entry;
                    if (FileEventLog.TryParse(line, out entry))
                    {
                        entries.Add(entry);
                    }
                    else
                    {
                        skippedLines++;
                    }
                }
            }

            LastReadSkippedLines = skippedLines;
            LastReadError = string.Join(Environment.NewLine, errors.ToArray());

            return entries;
        }

        /// <summary>
        /// Đọc mọi dòng của một tệp. Tệp không tồn tại → mảng rỗng.
        /// </summary>
        /// <remarks>
        /// Giữ fileLock trong lúc đọc: nếu đúng lúc đó luồng watcher đang ghi dở một dòng,
        /// ta sẽ phải chờ nó ghi xong, không bao giờ đọc được nửa dòng do chính chương trình ghi.
        ///
        /// Không dùng File.ReadAllLines: hàm đó mở tệp với FileShare.Read, nghĩa là
        /// "không cho ai khác đang ghi". Nếu có một cửa sổ FileMonitor thứ hai đang mở tệp
        /// để ghi, việc đọc sẽ ném IOException. Mở với FileShare.ReadWrite thì đọc được
        /// bình thường; cùng lắm dòng cuối bị ghi dở, và dòng đó bị TryParse loại ra.
        ///
        /// Encoding.UTF8 tự nhận và bỏ qua BOM nếu có, nên đọc được cả tệp do
        /// công cụ khác (ví dụ Notepad cũ) lưu lại kèm BOM.
        /// </remarks>
        private string[] ReadLines(string path)
        {
            List<string> lines = new List<string>();

            lock (fileLock)
            {
                if (!File.Exists(path))
                {
                    return lines.ToArray();
                }

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }
            }

            return lines.ToArray();
        }

        #endregion

        #region Tìm kiếm và lọc (chuyển từ MainForm sang)

        /// <summary>
        /// Đọc và lọc trong một bước. Kết quả sắp xếp mới nhất lên đầu, sẵn để hiển thị.
        /// </summary>
        /// <remarks>
        /// Khoảng ngày được dùng HAI lần, mỗi lần một việc:
        /// - ReadRange dùng nó để chọn TỆP cần mở: 7 ngày thì mở tối đa 7 tệp, không đọc
        ///   cả lịch sử nhiều tháng rồi mới lọc. Đây là lợi ích chính của việc tách tệp theo ngày.
        /// - Filter dùng nó để lọc từng BẢN GHI, để kết quả vẫn đúng kể cả khi một tệp chứa
        ///   bản ghi lệch ngày (ví dụ người dùng tự chép nối hai tệp vào nhau).
        /// </remarks>
        public List<FileEventLog> Query(LogFilter filter)
        {
            if (filter == null)
            {
                filter = new LogFilter();
            }

            List<FileEventLog> entries = ReadRange(filter.RangeStart, filter.RangeEnd);
            List<FileEventLog> result = Filter(entries, filter);

            // Tệp ghi nối nên thứ tự đọc ra là cũ trước, mới sau. Đảo lại cho bảng hiển thị.
            result.Reverse();
            return result;
        }

        /// <summary>
        /// Lọc một danh sách có sẵn trong bộ nhớ theo mọi điều kiện của filter,
        /// không đụng tới đĩa.
        /// </summary>
        /// <remarks>
        /// Tách riêng để MainForm dùng khi người dùng gõ tìm kiếm hoặc đổi ComboBox:
        /// lọc lại trên danh sách đã tải, không đọc lại tệp mỗi lần thay đổi.
        /// Hàm tĩnh, kiểm thử được độc lập.
        ///
        /// </remarks>
        public static List<FileEventLog> Filter(IEnumerable<FileEventLog> entries, LogFilter filter)
        {
            List<FileEventLog> result = new List<FileEventLog>();

            if (entries == null)
            {
                return result;
            }

            foreach (FileEventLog entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                // filter == null nghĩa là không lọc gì: trả về bản sao đầy đủ.
                if (filter == null || filter.Matches(entry))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Lọc theo loại sự kiện. eventType = null nghĩa là lấy tất cả các loại.
        /// </summary>
        /// <param name="entries">Danh sách cần lọc. Không bị sửa.</param>
        /// <param name="eventType">Loại cần lấy, hoặc null cho "Tất cả loại".</param>
        /// <returns>Danh sách MỚI, giữ nguyên thứ tự của danh sách đầu vào.</returns>
        /// <remarks>
        /// Dùng lại đúng quy tắc của LogFilter.MatchesEventType, để lọc riêng theo loại
        /// và lọc tổng hợp qua Filter() không bao giờ cho hai kết quả khác nhau.
        ///
        /// Luôn trả về danh sách mới, kể cả khi không lọc gì: bên gọi có thể Reverse()
        /// hay Clear() kết quả mà không làm hỏng danh sách gốc (allLogEntries trong MainForm).
        /// </remarks>
        public static List<FileEventLog> FilterByEventType(IEnumerable<FileEventLog> entries,
            FileEventType? eventType)
        {
            List<FileEventLog> result = new List<FileEventLog>();

            if (entries == null)
            {
                return result;
            }

            LogFilter filter = new LogFilter();
            filter.EventType = eventType;

            foreach (FileEventLog entry in entries)
            {
                if (filter.MatchesEventType(entry))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Lọc theo khoảng ngày [from, to], tính cả hai ngày đầu mút.
        /// </summary>
        /// <param name="entries">Danh sách cần lọc. Không bị sửa.</param>
        /// <param name="from">Ngày bắt đầu (phần giờ bị bỏ qua).</param>
        /// <param name="to">Ngày kết thúc (lấy tới hết ngày). Ngược với from thì tự đổi chỗ.</param>
        /// <returns>Danh sách MỚI, giữ nguyên thứ tự của danh sách đầu vào.</returns>
        /// <remarks>
        /// Dùng lại đúng quy tắc của LogFilter.MatchesDate, giống FilterByEventType và Search.
        /// </remarks>
        public static List<FileEventLog> FilterByDate(IEnumerable<FileEventLog> entries,
            DateTime from, DateTime to)
        {
            List<FileEventLog> result = new List<FileEventLog>();

            if (entries == null)
            {
                return result;
            }

            LogFilter filter = new LogFilter();
            filter.FromDate = from;
            filter.ToDate = to;

            foreach (FileEventLog entry in entries)
            {
                if (filter.MatchesDate(entry))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Tìm các bản ghi có chứa từ khóa trong tên tệp hoặc đường dẫn.
        /// Từ khóa rỗng thì trả về tất cả.
        /// </summary>
        /// <param name="entries">Danh sách cần tìm. Không bị sửa.</param>
        /// <param name="keyword">Từ khóa; nhiều từ cách nhau bởi khoảng trắng.</param>
        /// <returns>Danh sách MỚI, giữ nguyên thứ tự của danh sách đầu vào.</returns>
        /// <remarks>
        /// Quy tắc tìm (không phân biệt hoa/thường và dấu, nhiều từ là AND, tìm cả tên cũ
        /// của sự kiện Renamed) nằm ở LogFilter.MatchesKeyword; hàm này dùng lại đúng quy tắc
        /// đó để tìm riêng và lọc tổng hợp không bao giờ cho hai kết quả khác nhau.
        /// </remarks>
        public static List<FileEventLog> Search(IEnumerable<FileEventLog> entries, string keyword)
        {
            List<FileEventLog> result = new List<FileEventLog>();

            if (entries == null)
            {
                return result;
            }

            LogFilter filter = new LogFilter();
            filter.Keyword = keyword;

            foreach (FileEventLog entry in entries)
            {
                if (filter.MatchesKeyword(entry))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Đếm số bản ghi của từng loại trong một danh sách.
        /// </summary>
        /// <remarks>
        /// Dùng để hiện số lượng ngay trong ComboBox lọc, ví dụ "Deleted — Xóa (12)",
        /// giúp người dùng biết trước chọn loại nào thì ra bao nhiêu dòng.
        /// Mọi loại đều có mặt trong kết quả, kể cả loại bằng 0.
        /// </remarks>
        public static Dictionary<FileEventType, int> CountByEventType(IEnumerable<FileEventLog> entries)
        {
            Dictionary<FileEventType, int> counts = new Dictionary<FileEventType, int>();

            foreach (FileEventType eventType in FileEventTypeHelper.GetAll())
            {
                counts[eventType] = 0;
            }

            if (entries == null)
            {
                return counts;
            }

            foreach (FileEventLog entry in entries)
            {
                if (entry != null && counts.ContainsKey(entry.EventType))
                {
                    counts[entry.EventType]++;
                }
            }

            return counts;
        }

        #endregion

        #region Xuất và xóa

        /// <summary>Dấu phân cách cột mặc định của tệp CSV.</summary>
        public const char DefaultCsvSeparator = ',';

        /// <summary>
        /// Xuất danh sách ra tệp CSV, dùng dấu phẩy làm dấu phân cách.
        /// </summary>
        /// <returns>Số bản ghi đã xuất.</returns>
        /// <exception cref="ArgumentException">Đường dẫn đích rỗng.</exception>
        public int ExportCsv(string destinationPath, IList<FileEventLog> entries)
        {
            return ExportCsv(destinationPath, entries, DefaultCsvSeparator);
        }

        /// <summary>
        /// Xuất danh sách ra tệp CSV để mở bằng Excel, với dấu phân cách tùy chọn.
        /// </summary>
        /// <param name="destinationPath">Đường dẫn tệp CSV cần tạo (ghi đè nếu đã có).</param>
        /// <param name="entries">Danh sách bản ghi cần xuất.</param>
        /// <param name="separator">Dấu phân cách cột, thường là ',' hoặc ';'.</param>
        /// <returns>Số bản ghi đã xuất.</returns>
        /// <remarks>
        /// Vì sao cho chọn dấu phân cách: khi mở tệp CSV bằng cách nhấp đúp, Excel dùng
        /// "List separator" trong cài đặt vùng của Windows. Máy đặt vùng Việt Nam dùng dấu
        /// phẩy làm dấu thập phân nên List separator là dấu chấm phẩy — gặp tệp phân cách
        /// bằng dấu phẩy, Excel dồn cả dòng vào một cột. Giao diện có thể truyền vào
        /// CultureInfo.CurrentCulture.TextInfo.ListSeparator để khớp với máy người dùng.
        ///
        /// Không dùng dòng "sep=;" ở đầu tệp: Excel hiểu dòng đó nhưng khi gặp nó lại bỏ
        /// qua BOM, làm tiếng Việt bị lỗi font.
        ///
        /// Tệp được ghi kèm BOM (UTF8Encoding(true)): thiếu BOM, Excel đọc tệp theo bảng mã
        /// ANSI và tiếng Việt hiện thành ký tự lạ. Dòng kết thúc bằng CRLF theo RFC 4180.
        ///
        /// Không cần giữ fileLock: hàm chỉ làm việc với danh sách trong bộ nhớ và một
        /// tệp đích do người dùng chọn, không đụng tới tệp nhật ký.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Đường dẫn đích rỗng, hoặc dấu phân cách là nháy kép / ký tự xuống dòng.
        /// </exception>
        public int ExportCsv(string destinationPath, IList<FileEventLog> entries, char separator)
        {
            if (string.IsNullOrEmpty(destinationPath) || destinationPath.Trim().Length == 0)
            {
                throw new ArgumentException("Chưa chỉ định tệp CSV cần tạo.", "destinationPath");
            }

            if (separator == '"' || separator == '\r' || separator == '\n')
            {
                throw new ArgumentException("Dấu phân cách không hợp lệ.", "separator");
            }

            int count = 0;

            using (StreamWriter writer = new StreamWriter(destinationPath, false, new UTF8Encoding(true)))
            {
                // Tự đặt ký tự xuống dòng, không phụ thuộc Environment.NewLine của hệ điều hành.
                writer.NewLine = "\r\n";

                writer.WriteLine(CsvRow(separator,
                    "Thời gian", "Loại sự kiện", "Tên tệp", "Đường dẫn", "Đường dẫn cũ",
                    "Kích thước (byte)", "Ghi chú"));

                if (entries != null)
                {
                    foreach (FileEventLog entry in entries)
                    {
                        if (entry == null)
                        {
                            continue;
                        }

                        writer.WriteLine(CsvRow(separator,
                            entry.Time.ToString(FileEventLog.TimeFormat, CultureInfo.InvariantCulture),
                            entry.EventType.ToString(),
                            entry.FileName,
                            entry.FullPath,
                            entry.OldFullPath,
                            entry.FileSize.HasValue
                                ? entry.FileSize.Value.ToString(CultureInfo.InvariantCulture)
                                : string.Empty,
                            entry.Note));
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Xóa mọi tệp nhật ký. Trả về số tệp đã xóa.
        /// </summary>
        /// <remarks>
        /// Chỉ xóa tệp khớp mẫu tên filemonitor-yyyyMMdd.log (qua GetAvailableDays),
        /// không đụng tới tệp lạ người dùng tự bỏ vào thư mục Logs, cũng không xóa
        /// chính thư mục Logs.
        ///
        /// Giữ fileLock suốt quá trình xóa: nếu đang giám sát, luồng watcher phải chờ xóa
        /// xong mới ghi tiếp, và lần ghi đó tự tạo lại tệp của ngày hôm nay.
        ///
        /// Tệp nào không xóa được (thường vì đang mở trong Excel) thì bỏ qua, xóa tiếp các
        /// tệp còn lại, cuối cùng mới ném IOException liệt kê những tệp còn sót. Dừng ngay ở
        /// tệp lỗi đầu tiên sẽ để lại một nửa nhật ký đã xóa, một nửa chưa.
        /// </remarks>
        /// <exception cref="IOException">Có tệp không xóa được; thông báo liệt kê từng tệp.</exception>
        public int ClearAll()
        {
            int deleted = 0;
            List<string> failures = new List<string>();

            lock (fileLock)
            {
                foreach (DateTime day in GetAvailableDays())
                {
                    string path = GetLogFilePath(day);

                    try
                    {
                        File.Delete(path);
                        deleted++;
                    }
                    catch (IOException ex)
                    {
                        failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                    }
                }
            }

            if (failures.Count > 0)
            {
                throw new IOException(
                    "Đã xóa " + deleted + " tệp, còn " + failures.Count + " tệp không xóa được:" +
                    Environment.NewLine + string.Join(Environment.NewLine, failures.ToArray()));
            }

            return deleted;
        }

        /// <summary>
        /// Ghép các ô thành một dòng CSV, mỗi ô đã được xử lý qua CsvField.
        /// </summary>
        private static string CsvRow(char separator, params string[] values)
        {
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(CsvField(values[i], separator));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Xử lý một ô theo quy tắc CSV (RFC 4180) để ký tự đặc biệt không làm lệch cột.
        /// </summary>
        /// <remarks>
        /// Tên tệp và thư mục trên Windows được phép chứa dấu phẩy, dấu chấm phẩy và
        /// dấu nháy đơn, ví dụ D:\Báo cáo, quý 3\bản cuối.txt. Ghi thẳng ra thì dấu phẩy
        /// đó bị hiểu là ranh giới cột, cả dòng bị lệch sang phải.
        ///
        /// Quy tắc áp dụng:
        /// 1. Ô chứa dấu phân cách, nháy kép, CR hoặc LF → bọc trong nháy kép.
        /// 2. Nháy kép bên trong ô → nhân đôi ("" ). Windows cấm nháy kép trong tên tệp,
        ///    nhưng vẫn xử lý vì hàm này không nên phụ thuộc vào giả định đó.
        /// 3. Ô bắt đầu bằng = + - @ (hoặc TAB, CR) → thêm dấu nháy đơn ở đầu.
        ///    Đây là chống "CSV injection": Excel coi ô như vậy là CÔNG THỨC và thực thi nó.
        ///    Một tệp tên "=HYPERLINK(...).txt" là tên hợp lệ trên Windows; không chặn thì chỉ
        ///    cần mở tệp CSV xuất ra là Excel chạy công thức do người khác đặt vào tên tệp.
        ///    Với công cụ giám sát thì tên tệp là dữ liệu do BẤT KỲ AI tạo ra, nên phải coi
        ///    là không tin cậy.
        ///    Đánh đổi: tệp tên "-nhap.txt" sẽ hiện là "'-nhap.txt" trong Excel.
        ///
        /// Ô không có gì đặc biệt thì giữ nguyên, không bọc nháy kép, cho tệp gọn và dễ đọc
        /// bằng Notepad.
        /// </remarks>
        internal static string CsvField(string value, char separator)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (IsFormulaTrigger(value[0]))
            {
                value = "'" + value;
            }

            bool mustQuote = value.IndexOf(separator) >= 0
                          || value.IndexOf('"') >= 0
                          || value.IndexOf('\r') >= 0
                          || value.IndexOf('\n') >= 0;

            if (!mustQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Ký tự đầu ô khiến Excel / LibreOffice hiểu ô là công thức.
        /// </summary>
        private static bool IsFormulaTrigger(char c)
        {
            return c == '=' || c == '+' || c == '-' || c == '@' || c == '\t' || c == '\r';
        }

        #endregion
    }
}
