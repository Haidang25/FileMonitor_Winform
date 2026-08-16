using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

namespace FileMonitorApps
{
    /// <summary>
    /// Cửa sổ chính của chương trình FileMonitor.
    /// Tab "Giám sát" cho phép người dùng chọn thư mục cần theo dõi.
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>
        /// Đối tượng theo dõi thư mục. Chỉ khác null khi đang giám sát.
        /// </summary>
        private FileSystemWatcher watcher;

        /// <summary>
        /// Danh sách nhật ký đang hiển thị ở tab Nhật ký.
        /// Giữ lại để xuất ra CSV đúng những gì người dùng đang thấy.
        /// </summary>
        private List<LogEntry> loadedLogEntries = new List<LogEntry>();

        /// <summary>
        /// Toàn bộ nhật ký đọc từ tệp, giữ nguyên chưa lọc.
        /// Nhờ vậy khi người dùng gõ tìm kiếm hoặc đổi bộ lọc thì chỉ cần lọc lại
        /// trên bộ nhớ, không phải đọc lại tệp mỗi lần nhấn phím.
        /// </summary>
        private List<LogEntry> allLogEntries = new List<LogEntry>();

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            SetCueBanner(txtFolderPath, "Ví dụ: D:\\MonitorTest");
            LoadFileFilters();
            UpdateEventCount();
            SetMonitoringState(false);
            InitDateFilter();
            LoadEventTypeFilters();
            SetCueBanner(txtSearch, "Tìm theo tên tệp hoặc đường dẫn...");
        }

        #region Chọn thư mục giám sát

        /// <summary>
        /// Xử lý sự kiện bấm nút "Chọn thư mục": mở hộp thoại duyệt thư mục,
        /// kiểm tra thư mục vừa chọn rồi điền vào ô txtFolderPath.
        /// </summary>
        private void btnBrowse_Click(object sender, EventArgs e)
        {
            try
            {
                // Nếu ô nhập đang chứa một thư mục hợp lệ thì mở hộp thoại ngay tại đó,
                // giúp người dùng không phải duyệt lại từ đầu.
                string currentPath = txtFolderPath.Text.Trim();
                if (currentPath.Length > 0 && Directory.Exists(currentPath))
                {
                    folderBrowserDialog.SelectedPath = currentPath;
                }

                if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                {
                    string normalizedPath;

                    // Người dùng vẫn có thể chọn thư mục mà tài khoản hiện tại không đọc được
                    // (ví dụ C:\\System Volume Information), nên phải kiểm tra trước khi nhận.
                    if (TryValidateFolder(folderBrowserDialog.SelectedPath, out normalizedPath))
                    {
                        txtFolderPath.Text = normalizedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                // Bắt ngoại lệ để chương trình không bị đóng đột ngột
                // (ví dụ: thư mục nằm trên ổ đĩa mạng đã ngắt kết nối).
                MessageBox.Show(this,
                    "Không thể mở hộp thoại chọn thư mục." + Environment.NewLine +
                    Environment.NewLine + "Chi tiết: " + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Kiểm tra đường dẫn thư mục và thông báo cho người dùng nếu không hợp lệ.
        /// </summary>
        /// <param name="rawPath">Đường dẫn người dùng nhập hoặc chọn.</param>
        /// <param name="normalizedPath">Đường dẫn đã chuẩn hóa, chỉ có giá trị khi hàm trả về true.</param>
        /// <returns>true nếu thư mục tồn tại và đọc được.</returns>
        private bool TryValidateFolder(string rawPath, out string normalizedPath)
        {
            string errorMessage;

            if (CheckFolder(rawPath, out normalizedPath, out errorMessage))
            {
                return true;
            }

            MessageBox.Show(this,
                errorMessage,
                "Đường dẫn không hợp lệ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        /// <summary>
        /// Chuẩn hóa và kiểm tra một đường dẫn thư mục.
        /// Hàm này không đụng tới giao diện để có thể kiểm thử độc lập.
        /// </summary>
        /// <param name="rawPath">Đường dẫn cần kiểm tra.</param>
        /// <param name="normalizedPath">Đường dẫn tuyệt đối đã chuẩn hóa (rỗng nếu không hợp lệ).</param>
        /// <param name="errorMessage">Mô tả lỗi để hiển thị (rỗng nếu hợp lệ).</param>
        /// <returns>true nếu thư mục tồn tại và tài khoản hiện tại đọc được.</returns>
        private static bool CheckFolder(string rawPath, out string normalizedPath, out string errorMessage)
        {
            normalizedPath = string.Empty;
            errorMessage = string.Empty;

            // Bỏ khoảng trắng và dấu nháy kép khi người dùng dán đường dẫn từ File Explorer.
            string path = (rawPath ?? string.Empty).Trim().Trim('"');

            if (path.Length == 0)
            {
                errorMessage = "Vui lòng chọn thư mục cần giám sát.";
                return false;
            }

            // Chuẩn hóa: chuyển đường dẫn tương đối thành tuyệt đối, gộp dấu gạch chéo thừa.
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                errorMessage = "Đường dẫn chứa ký tự không hợp lệ:" + Environment.NewLine + rawPath;
                return false;
            }
            catch (NotSupportedException)
            {
                errorMessage = "Định dạng đường dẫn không được hỗ trợ:" + Environment.NewLine + rawPath;
                return false;
            }
            catch (PathTooLongException)
            {
                errorMessage = "Đường dẫn quá dài so với giới hạn của hệ điều hành.";
                return false;
            }
            catch (SecurityException)
            {
                errorMessage = "Không đủ quyền để xử lý đường dẫn này:" + Environment.NewLine + rawPath;
                return false;
            }

            if (!Directory.Exists(path))
            {
                errorMessage = "Thư mục không tồn tại:" + Environment.NewLine + path;
                return false;
            }

            // Thư mục tồn tại chưa chắc đã đọc được. Thử liệt kê một phần tử đầu tiên
            // để phát hiện sớm lỗi phân quyền, thay vì để FileSystemWatcher báo lỗi khó hiểu về sau.
            try
            {
                using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
                {
                    entries.MoveNext();
                }
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = "Tài khoản hiện tại không có quyền đọc thư mục:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine +
                    "Hãy chọn thư mục khác, hoặc chạy chương trình với quyền Administrator.";
                return false;
            }
            catch (IOException ex)
            {
                errorMessage = "Không đọc được thư mục:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message;
                return false;
            }

            normalizedPath = path;
            return true;
        }

        /// <summary>
        /// Trả về đường dẫn thư mục đang được chọn sau khi đã kiểm tra hợp lệ,
        /// đồng thời hiển thị lại dạng đã chuẩn hóa trong ô nhập.
        /// Nếu không hợp lệ, hiển thị thông báo và trả về chuỗi rỗng.
        /// Bước bắt đầu giám sát ở phần sau sẽ dùng lại phương thức này.
        /// </summary>
        private string GetValidatedFolderPath()
        {
            string normalizedPath;

            if (!TryValidateFolder(txtFolderPath.Text, out normalizedPath))
            {
                txtFolderPath.Focus();
                txtFolderPath.SelectAll();
                return string.Empty;
            }

            txtFolderPath.Text = normalizedPath;
            return normalizedPath;
        }

        #endregion

        #region Tab Nhật ký

        /// <summary>Định dạng thời gian hiển thị trong bảng nhật ký.</summary>
        private const string DisplayTimeFormat = "dd/MM/yyyy HH:mm:ss";

        /// <summary>
        /// Bấm "Tải log": đọc tệp nhật ký trên đĩa và đổ vào bảng, mới nhất lên đầu.
        /// </summary>
        private void btnLoadLog_Click(object sender, EventArgs e)
        {
            try
            {
                allLogEntries = LogStorage.ReadAll();
                ApplyLogFilters();

                if (allLogEntries.Count == 0)
                {
                    MessageBox.Show(this,
                        "Chưa có nhật ký nào được ghi." + Environment.NewLine +
                        Environment.NewLine + "Tệp nhật ký: " + LogStorage.LogFilePath,
                        "Nhật ký trống",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else if (loadedLogEntries.Count == 0)
                {
                    // Phân biệt rõ "chưa ghi gì" với "có dữ liệu nhưng bị bộ lọc loại hết",
                    // nếu không người dùng sẽ tưởng chương trình không ghi được nhật ký.
                    MessageBox.Show(this,
                        "Không có bản ghi nào khớp với bộ lọc hiện tại." + Environment.NewLine +
                        Environment.NewLine + "Toàn bộ nhật ký có " +
                        allLogEntries.Count.ToString("N0") + " bản ghi. " +
                        "Hãy thử nới rộng khoảng ngày, xóa từ khóa tìm kiếm " +
                        "hoặc chọn lại \"Tất cả loại\".",
                        "Không có dữ liệu phù hợp",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Không đọc được tệp nhật ký:" + Environment.NewLine + LogStorage.LogFilePath +
                    Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Bấm "Xuất log": lưu danh sách đang hiển thị ra tệp CSV để mở bằng Excel.
        /// </summary>
        private void btnExportLog_Click(object sender, EventArgs e)
        {
            if (loadedLogEntries.Count == 0)
            {
                MessageBox.Show(this,
                    "Chưa có dữ liệu để xuất. Hãy bấm \"Tải log\" trước.",
                    "Không có dữ liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            saveFileDialog.FileName = "nhatky-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";

            if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                LogStorage.ExportCsv(saveFileDialog.FileName, loadedLogEntries);

                MessageBox.Show(this,
                    "Đã xuất " + loadedLogEntries.Count.ToString("N0") + " bản ghi ra tệp:" +
                    Environment.NewLine + saveFileDialog.FileName,
                    "Xuất thành công",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Không ghi được tệp CSV." + Environment.NewLine +
                    Environment.NewLine + "Chi tiết: " + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Bấm "Xóa log": xóa toàn bộ nội dung tệp nhật ký sau khi người dùng xác nhận.
        /// </summary>
        private void btnClearLog_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(this,
                "Xóa toàn bộ nhật ký đã ghi?" + Environment.NewLine +
                Environment.NewLine + "Thao tác này không thể hoàn tác.",
                "Xác nhận xóa",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                LogStorage.Clear();
                allLogEntries.Clear();
                ApplyLogFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Không xóa được tệp nhật ký:" + Environment.NewLine + LogStorage.LogFilePath +
                    Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Nạp danh sách loại sự kiện vào ComboBox lọc.
        /// Dùng lại lớp FilterItem của tab Giám sát: nhãn hiển thị tách khỏi giá trị thật,
        /// giá trị rỗng nghĩa là không lọc theo loại.
        /// </summary>
        private void LoadEventTypeFilters()
        {
            cboEventTypeFilter.Items.Clear();
            cboEventTypeFilter.Items.AddRange(new object[]
            {
                new FilterItem("Tất cả loại",         string.Empty),
                new FilterItem("Created — Tạo mới",   "Created"),
                new FilterItem("Changed — Sửa đổi",   "Changed"),
                new FilterItem("Deleted — Xóa",       "Deleted"),
                new FilterItem("Renamed — Đổi tên",   "Renamed")
            });

            cboEventTypeFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// Lọc lại danh sách theo cả ba tiêu chí (ngày, từ khóa, loại sự kiện)
        /// rồi hiển thị kết quả. Hàm này không hiện thông báo nào để người dùng
        /// gõ tìm kiếm mà không bị hộp thoại làm phiền.
        /// </summary>
        private void ApplyLogFilters()
        {
            List<LogEntry> result = FilterByDate(allLogEntries);
            result = FilterByEventType(result);
            result = FilterByKeyword(result);

            // Tệp được ghi nối nên thứ tự trong tệp là cũ trước, mới sau.
            // Đảo lại để bản ghi mới nhất nằm trên đầu bảng.
            result.Reverse();

            loadedLogEntries = result;
            ShowLogEntries(result);
        }

        /// <summary>
        /// Lọc theo loại sự kiện đang chọn. Mục "Tất cả loại" giữ nguyên danh sách.
        /// </summary>
        private List<LogEntry> FilterByEventType(List<LogEntry> entries)
        {
            List<LogEntry> result = new List<LogEntry>();

            if (entries == null)
            {
                return result;
            }

            FilterItem selected = cboEventTypeFilter.SelectedItem as FilterItem;
            string eventType = selected != null ? selected.Pattern : string.Empty;

            if (eventType.Length == 0)
            {
                result.AddRange(entries);
                return result;
            }

            foreach (LogEntry entry in entries)
            {
                if (string.Equals(entry.EventType, eventType, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Lọc theo từ khóa, so khớp với tên tệp hoặc đường dẫn.
        /// </summary>
        /// <remarks>
        /// Dùng CurrentCultureIgnoreCase thay vì OrdinalIgnoreCase để so sánh
        /// chữ hoa/chữ thường đúng với tiếng Việt có dấu.
        /// </remarks>
        private List<LogEntry> FilterByKeyword(List<LogEntry> entries)
        {
            List<LogEntry> result = new List<LogEntry>();

            if (entries == null)
            {
                return result;
            }

            string keyword = txtSearch.Text.Trim();

            if (keyword.Length == 0)
            {
                result.AddRange(entries);
                return result;
            }

            foreach (LogEntry entry in entries)
            {
                if (Contains(entry.FileName, keyword) || Contains(entry.FullPath, keyword))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Kiểm tra chuỗi có chứa từ khóa hay không, bỏ qua phân biệt hoa/thường.
        /// </summary>
        private static bool Contains(string value, string keyword)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// Gõ vào ô tìm kiếm thì lọc lại ngay, không cần bấm nút.
        /// </summary>
        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            ApplyLogFilters();
        }

        /// <summary>
        /// Đổi loại sự kiện thì lọc lại ngay.
        /// </summary>
        private void cboEventTypeFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyLogFilters();
        }

        /// <summary>
        /// Đặt khoảng ngày mặc định khi mở chương trình: 7 ngày gần nhất.
        /// </summary>
        private void InitDateFilter()
        {
            dtpFrom.Value = DateTime.Today.AddDays(-7);
            dtpTo.Value = DateTime.Today;
        }

        /// <summary>
        /// Lọc danh sách nhật ký theo khoảng ngày đang chọn.
        /// </summary>
        /// <remarks>
        /// Ngày kết thúc được lấy tới hết ngày (23:59:59) chứ không phải 00:00:00,
        /// nếu không thì chọn "đến hôm nay" sẽ bỏ sót toàn bộ sự kiện của chính hôm nay.
        /// </remarks>
        private List<LogEntry> FilterByDate(List<LogEntry> entries)
        {
            List<LogEntry> result = new List<LogEntry>();

            if (entries == null)
            {
                return result;
            }

            DateTime from = dtpFrom.Value.Date;
            DateTime to = dtpTo.Value.Date.AddDays(1).AddTicks(-1);

            foreach (LogEntry entry in entries)
            {
                if (entry.Time >= from && entry.Time <= to)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Không cho phép ngày bắt đầu vượt quá ngày kết thúc.
        /// Tự chỉnh lại thay vì hiện thông báo lỗi, để người dùng đỡ bị làm phiền.
        /// </summary>
        private void dtpFrom_ValueChanged(object sender, EventArgs e)
        {
            if (dtpFrom.Value.Date > dtpTo.Value.Date)
            {
                dtpTo.Value = dtpFrom.Value.Date;
            }

            ApplyLogFilters();
        }

        /// <summary>
        /// Không cho phép ngày kết thúc lùi trước ngày bắt đầu.
        /// </summary>
        private void dtpTo_ValueChanged(object sender, EventArgs e)
        {
            if (dtpTo.Value.Date < dtpFrom.Value.Date)
            {
                dtpFrom.Value = dtpTo.Value.Date;
            }

            ApplyLogFilters();
        }

        /// <summary>
        /// Đổ danh sách nhật ký lên bảng dgvLogHistory.
        /// </summary>
        private void ShowLogEntries(List<LogEntry> entries)
        {
            dgvLogHistory.Rows.Clear();

            if (entries == null || entries.Count == 0)
            {
                return;
            }

            // Tắt vẽ lại trong lúc thêm hàng loạt để bảng không bị nháy.
            dgvLogHistory.SuspendLayout();
            try
            {
                foreach (LogEntry entry in entries)
                {
                    dgvLogHistory.Rows.Add(
                        entry.Time.ToString(DisplayTimeFormat),
                        entry.EventType,
                        entry.FileName,
                        entry.FullPath);
                }
            }
            finally
            {
                dgvLogHistory.ResumeLayout();
            }
        }

        #endregion

        #region Bắt đầu / dừng giám sát

        /// <summary>
        /// Bấm "Bắt đầu giám sát": kiểm tra thư mục rồi khởi động FileSystemWatcher.
        /// </summary>
        private void btnStart_Click(object sender, EventArgs e)
        {
            string folderPath = GetValidatedFolderPath();
            if (folderPath.Length == 0)
            {
                return;
            }

            try
            {
                StartWatching(folderPath);
                SetMonitoringState(true);
            }
            catch (Exception ex)
            {
                // Nếu khởi động thất bại thì phải dọn sạch, không để lại watcher dở dang.
                DisposeWatcher();
                SetMonitoringState(false);

                MessageBox.Show(this,
                    "Không thể bắt đầu giám sát thư mục:" + Environment.NewLine + folderPath +
                    Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Bấm "Dừng giám sát": giải phóng watcher và trả giao diện về trạng thái nghỉ.
        /// </summary>
        private void btnStop_Click(object sender, EventArgs e)
        {
            DisposeWatcher();
            SetMonitoringState(false);
        }

        /// <summary>
        /// Tạo và khởi động FileSystemWatcher theo cấu hình đang chọn trên giao diện.
        /// </summary>
        /// <param name="folderPath">Thư mục cần theo dõi, đã được kiểm tra hợp lệ.</param>
        private void StartWatching(string folderPath)
        {
            // Dọn watcher của lần chạy trước (nếu có) để không bị hai bộ theo dõi cùng chạy.
            DisposeWatcher();

            watcher = new FileSystemWatcher();
            watcher.Path = folderPath;
            watcher.Filter = GetSelectedFilter();
            watcher.IncludeSubdirectories = chkIncludeSubdirs.Checked;

            // NotifyFilter quyết định thay đổi nào được coi là đáng báo.
            // Chỉ đăng ký những loại cần thiết để giảm bớt sự kiện nhiễu.
            watcher.NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size;

            // Hệ điều hành lưu tạm các thay đổi vào một bộ đệm trước khi báo cho chương trình.
            // Khi thư mục thay đổi dồn dập, bộ đệm mặc định (8 KB) có thể bị tràn và
            // một số sự kiện sẽ bị mất. Đặt lên mức tối đa 64 KB để hạn chế tình huống đó.
            watcher.InternalBufferSize = 65536;

            // Sự kiện Error báo khi bản thân việc theo dõi gặp sự cố,
            // ví dụ tràn bộ đệm hoặc thư mục đang theo dõi bị xóa.
            watcher.Error += Watcher_Error;

            // (Bước sau sẽ đăng ký thêm Created / Changed / Deleted / Renamed
            //  để ghi từng thay đổi vào bảng dgvEvents.)

            watcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Dừng và giải phóng watcher hiện tại. Gọi được nhiều lần mà không gây lỗi.
        /// </summary>
        private void DisposeWatcher()
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Error -= Watcher_Error;
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
        /// Xử lý sự cố của chính bộ theo dõi (tràn bộ đệm, mất thư mục đang theo dõi...).
        /// </summary>
        /// <remarks>
        /// FileSystemWatcher phát sự kiện trên LUỒNG NỀN, không phải luồng giao diện.
        /// Windows Forms chỉ cho phép đụng tới control từ đúng luồng đã tạo ra nó,
        /// nên phải chuyển lời gọi về luồng giao diện bằng BeginInvoke trước khi cập nhật.
        /// </remarks>
        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            // Form có thể đã đóng trong lúc sự kiện đang trên đường tới.
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new ErrorEventHandler(Watcher_Error), new object[] { sender, e });
                return;
            }

            Exception error = e.GetException();

            DisposeWatcher();
            SetMonitoringState(false);

            MessageBox.Show(this,
                "Quá trình giám sát đã dừng do gặp sự cố." + Environment.NewLine +
                Environment.NewLine +
                "Nguyên nhân thường gặp: thư mục đang theo dõi bị xóa hoặc bị ngắt kết nối, " +
                "hoặc có quá nhiều thay đổi cùng lúc làm tràn bộ đệm." + Environment.NewLine +
                Environment.NewLine + "Chi tiết: " + (error != null ? error.Message : "không rõ"),
                "Lỗi giám sát",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Cập nhật giao diện theo trạng thái đang giám sát hay đang nghỉ.
        /// </summary>
        /// <param name="isMonitoring">true khi bộ theo dõi đang chạy.</param>
        private void SetMonitoringState(bool isMonitoring)
        {
            btnStart.Enabled = !isMonitoring;
            btnStop.Enabled = isMonitoring;

            // Khóa phần cấu hình trong lúc đang chạy, nếu không cấu hình hiển thị
            // sẽ không còn khớp với cấu hình mà watcher đang thực sự dùng.
            txtFolderPath.Enabled = !isMonitoring;
            btnBrowse.Enabled = !isMonitoring;
            chkIncludeSubdirs.Enabled = !isMonitoring;
            cboFileFilter.Enabled = !isMonitoring;

            if (isMonitoring)
            {
                lblStatus.Text = "● Đang giám sát";
                lblStatus.ForeColor = Color.FromArgb(16, 124, 16);
            }
            else
            {
                lblStatus.Text = "● Chưa giám sát";
                lblStatus.ForeColor = Color.Gray;
            }
        }

        /// <summary>
        /// Giải phóng bộ theo dõi khi đóng chương trình để không bỏ sót tài nguyên.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            DisposeWatcher();
        }

        #endregion

        #region Danh sách sự kiện

        /// <summary>
        /// Cập nhật nhãn tổng số sự kiện theo số dòng hiện có trong bảng.
        /// Lấy trực tiếp từ dgvEvents.Rows.Count để chỉ có một nguồn dữ liệu duy nhất,
        /// tránh tình trạng biến đếm bị lệch so với nội dung đang hiển thị.
        /// </summary>
        private void UpdateEventCount()
        {
            lblEventCount.Text = "Tổng số sự kiện: " + dgvEvents.Rows.Count.ToString("N0");
        }

        #endregion

        #region Lọc theo phần mở rộng tệp

        /// <summary>
        /// Một mục trong danh sách lọc: gồm nhãn hiển thị cho người dùng
        /// và mẫu lọc thực sự sẽ gán cho FileSystemWatcher.Filter.
        /// </summary>
        private class FilterItem
        {
            public string Display { get; private set; }
            public string Pattern { get; private set; }

            public FilterItem(string display, string pattern)
            {
                Display = display;
                Pattern = pattern;
            }

            // ComboBox dùng ToString() để hiển thị nên chỉ cần trả về nhãn.
            public override string ToString()
            {
                return Display;
            }
        }

        /// <summary>
        /// Nạp danh sách phần mở rộng vào ComboBox và chọn sẵn mục "Tất cả".
        /// </summary>
        /// <remarks>
        /// Lưu ý: trên .NET Framework, thuộc tính FileSystemWatcher.Filter chỉ nhận
        /// MỘT mẫu lọc duy nhất (không hỗ trợ nhiều mẫu ngăn cách bởi dấu chấm phẩy),
        /// nên mỗi mục ở đây chỉ chứa một phần mở rộng.
        /// </remarks>
        private void LoadFileFilters()
        {
            cboFileFilter.Items.Clear();
            cboFileFilter.Items.AddRange(new object[]
            {
                new FilterItem("*.* (Tất cả)",      "*.*"),
                new FilterItem("*.txt (Văn bản)",   "*.txt"),
                new FilterItem("*.docx (Word)",     "*.docx"),
                new FilterItem("*.xlsx (Excel)",    "*.xlsx"),
                new FilterItem("*.pdf (PDF)",       "*.pdf"),
                new FilterItem("*.png (Hình ảnh)",  "*.png"),
                new FilterItem("*.cs (Mã nguồn C#)", "*.cs"),
                new FilterItem("*.log (Nhật ký)",   "*.log")
            });

            cboFileFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// Trả về mẫu lọc đang được chọn để gán cho FileSystemWatcher.Filter.
        /// Nếu vì lý do nào đó chưa có mục nào được chọn thì mặc định lấy tất cả tệp.
        /// </summary>
        private string GetSelectedFilter()
        {
            FilterItem selected = cboFileFilter.SelectedItem as FilterItem;
            return selected != null ? selected.Pattern : "*.*";
        }

        #endregion

        #region Gợi ý trong ô nhập (placeholder)

        // .NET Framework chưa có thuộc tính PlaceholderText cho TextBox,
        // nên dùng thông điệp EM_SETCUEBANNER của Windows để hiển thị dòng gợi ý mờ.
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>
        /// Hiển thị dòng gợi ý mờ bên trong ô nhập khi ô đang trống.
        /// </summary>
        /// <param name="textBox">Ô nhập cần đặt gợi ý.</param>
        /// <param name="hint">Nội dung gợi ý.</param>
        private static void SetCueBanner(TextBox textBox, string hint)
        {
            try
            {
                // Tham số wParam = 1: vẫn giữ gợi ý khi ô nhập đang được chọn.
                SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, hint);
            }
            catch (Exception)
            {
                // Dòng gợi ý chỉ là chi tiết trang trí. Nếu hệ điều hành không hỗ trợ
                // thông điệp này thì bỏ qua, không được để ảnh hưởng tới việc mở chương trình.
            }
        }

        #endregion
    }
}
