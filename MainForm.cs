using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
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
        /// Phần lõi lo việc theo dõi thư mục. Form chỉ ra lệnh bật/tắt và nghe sự kiện.
        /// </summary>
        private readonly FileMonitorService monitorService = new FileMonitorService();

        /// <summary>
        /// Số sự kiện đã ghi nhận trong phiên giám sát hiện tại.
        /// </summary>
        /// <remarks>
        /// Không lấy từ dgvEvents.Rows.Count vì bảng chỉ giữ lại một số dòng gần nhất,
        /// còn con số này phải phản ánh tổng số thay đổi thực sự đã bắt được.
        /// </remarks>
        private int sessionEventCount;

        /// <summary>
        /// Số lần tràn bộ đệm trong phiên hiện tại. Mỗi lần tương ứng với một khoảng
        /// thời gian mà nhật ký bị thiếu dữ liệu.
        /// </summary>
        private int overflowCount;

        /// <summary>
        /// Các bản ghi đã nhận nhưng chưa kịp đưa lên bảng.
        /// </summary>
        private readonly List<FileEventLog> pendingEvents = new List<FileEventLog>();

        /// <summary>
        /// Khóa bảo vệ pendingEvents: luồng nền ghi vào, luồng giao diện đọc ra.
        /// </summary>
        private readonly object pendingLock = new object();

        /// <summary>
        /// Bằng 1 khi đã xếp hàng một lượt cập nhật giao diện nhưng lượt đó chưa chạy.
        /// Dùng Interlocked nên đọc/ghi an toàn giữa các luồng mà không cần khóa.
        /// </summary>
        private int flushScheduled;

        /// <summary>
        /// Số lượt cập nhật giao diện đã thực hiện. Chỉ dùng để kiểm chứng hiệu quả gộp.
        /// </summary>
        private int flushCount;

        /// <summary>
        /// Số dòng tối đa giữ lại trên bảng sự kiện. Toàn bộ vẫn nằm trong tệp nhật ký.
        /// Không giới hạn thì một thư mục hoạt động mạnh sẽ làm bảng phình ra vô hạn.
        /// </summary>
        private const int MaxDisplayedEvents = 5000;

        /// <summary>
        /// Đang trong phiên giám sát hay không. Giữ thành một trường riêng để
        /// mọi nơi cần bật/tắt nút đều đọc từ cùng một nguồn trạng thái.
        /// </summary>
        private bool isMonitoring;

        /// <summary>
        /// Danh sách nhật ký đang hiển thị ở tab Nhật ký.
        /// Giữ lại để xuất ra CSV đúng những gì người dùng đang thấy.
        /// </summary>
        private List<FileEventLog> loadedLogEntries = new List<FileEventLog>();

        /// <summary>
        /// Toàn bộ nhật ký đọc từ tệp, giữ nguyên chưa lọc.
        /// Nhờ vậy khi người dùng gõ tìm kiếm hoặc đổi bộ lọc thì chỉ cần lọc lại
        /// trên bộ nhớ, không phải đọc lại tệp mỗi lần nhấn phím.
        /// </summary>
        private List<FileEventLog> allLogEntries = new List<FileEventLog>();

        public MainForm()
        {
            InitializeComponent();

            monitorService.FileEventDetected += MonitorService_FileEventDetected;
            monitorService.ErrorOccurred += MonitorService_ErrorOccurred;
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
            // Nút đã bị làm mờ khi không có dữ liệu, đây chỉ là chốt chặn phòng xa.
            if (loadedLogEntries.Count == 0)
            {
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
            cboEventTypeFilter.Items.Add(new FilterItem("Tất cả loại", string.Empty));

            // Dựng danh sách từ chính kiểu liệt kê: thêm một loại sự kiện mới
            // chỉ cần khai báo trong FileEventType, giao diện tự có thêm mục.
            foreach (FileEventType eventType in FileEventTypeHelper.GetAll())
            {
                cboEventTypeFilter.Items.Add(
                    new FilterItem(FileEventTypeHelper.GetFullLabel(eventType), eventType.ToString()));
            }

            cboEventTypeFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// Lọc lại danh sách theo cả ba tiêu chí (ngày, từ khóa, loại sự kiện)
        /// rồi hiển thị kết quả. Hàm này không hiện thông báo nào để người dùng
        /// gõ tìm kiếm mà không bị hộp thoại làm phiền.
        /// </summary>
        private void ApplyLogFilters()
        {
            List<FileEventLog> result = FilterByDate(allLogEntries);
            result = FilterByEventType(result);
            result = FilterByKeyword(result);

            // Tệp được ghi nối nên thứ tự trong tệp là cũ trước, mới sau.
            // Đảo lại để bản ghi mới nhất nằm trên đầu bảng.
            result.Reverse();

            loadedLogEntries = result;
            ShowLogEntries(result);
            UpdateButtonStates();
        }

        /// <summary>
        /// Lọc theo loại sự kiện đang chọn. Mục "Tất cả loại" giữ nguyên danh sách.
        /// </summary>
        private List<FileEventLog> FilterByEventType(List<FileEventLog> entries)
        {
            List<FileEventLog> result = new List<FileEventLog>();

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

            foreach (FileEventLog entry in entries)
            {
                if (string.Equals(entry.EventType.ToString(), eventType, StringComparison.OrdinalIgnoreCase))
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
        private List<FileEventLog> FilterByKeyword(List<FileEventLog> entries)
        {
            List<FileEventLog> result = new List<FileEventLog>();

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

            foreach (FileEventLog entry in entries)
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
        private List<FileEventLog> FilterByDate(List<FileEventLog> entries)
        {
            List<FileEventLog> result = new List<FileEventLog>();

            if (entries == null)
            {
                return result;
            }

            DateTime from = dtpFrom.Value.Date;
            DateTime to = dtpTo.Value.Date.AddDays(1).AddTicks(-1);

            foreach (FileEventLog entry in entries)
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
        private void ShowLogEntries(List<FileEventLog> entries)
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
                foreach (FileEventLog entry in entries)
                {
                    dgvLogHistory.Rows.Add(
                        entry.Time.ToString(DisplayTimeFormat),
                        entry.EventType.ToString(),
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

            if (!ConfirmHighVolumeScope(folderPath))
            {
                return;
            }

            try
            {
                dgvEvents.Rows.Clear();
                lock (pendingLock)
                {
                    pendingEvents.Clear();
                }
                sessionEventCount = 0;
                overflowCount = 0;
                flushCount = 0;
                UpdateEventCount();

                monitorService.Start(folderPath, GetSelectedFilter(), chkIncludeSubdirs.Checked);
                SetMonitoringState(true);
            }
            catch (Exception ex)
            {
                // Nếu khởi động thất bại thì phải dọn sạch, không để lại phiên dở dang.
                monitorService.Stop();
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
        /// Bấm "Dừng giám sát": yêu cầu phần lõi dừng và trả giao diện về trạng thái nghỉ.
        /// </summary>
        private void btnStop_Click(object sender, EventArgs e)
        {
            monitorService.Stop();

            // Đẩy nốt những bản ghi vừa nhận nhưng chưa lên bảng, nếu không
            // các thay đổi cuối cùng trước khi dừng sẽ không bao giờ hiện ra.
            FlushPendingEvents();

            SetMonitoringState(false);
        }

        /// <summary>
        /// Hỏi lại người dùng khi phạm vi theo dõi quá rộng.
        /// </summary>
        /// <returns>true nếu được phép tiếp tục.</returns>
        private bool ConfirmHighVolumeScope(string folderPath)
        {
            if (!chkIncludeSubdirs.Checked || !IsDriveRoot(folderPath))
            {
                return true;
            }

            DialogResult answer = MessageBox.Show(this,
                "Bạn đang chọn thư mục gốc của ổ đĩa kèm toàn bộ thư mục con:" +
                Environment.NewLine + folderPath + Environment.NewLine +
                Environment.NewLine +
                "Phạm vi này sinh ra rất nhiều sự kiện (tệp tạm của hệ điều hành, bộ nhớ đệm " +
                "của trình duyệt, tiến trình đồng bộ ngầm...) và dễ làm tràn bộ đệm, " +
                "khiến một số thay đổi bị bỏ sót." + Environment.NewLine +
                Environment.NewLine + "Vẫn tiếp tục?",
                "Phạm vi theo dõi quá rộng",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            return answer == DialogResult.Yes;
        }

        /// <summary>
        /// Kiểm tra một đường dẫn có phải thư mục gốc của ổ đĩa hay không (ví dụ C:\).
        /// Hàm tĩnh, không đụng tới giao diện để kiểm thử được độc lập.
        /// </summary>
        private static bool IsDriveRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                {
                    return false;
                }

                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Đường dẫn không hợp lệ thì coi như không phải thư mục gốc;
                // phần kiểm tra đường dẫn đã được làm ở CheckFolder trước đó.
                return false;
            }
        }

        /// <summary>
        /// Phương thức xử lý sự kiện FileEventDetected: ghi xuống tệp rồi hiển thị lên bảng.
        /// </summary>
        /// <remarks>
        /// Hàm này chạy trên LUỒNG NỀN của FileSystemWatcher.
        /// Việc ghi tệp cố tình làm ngay tại đây, trước khi chuyển luồng: thao tác đĩa
        /// mà đẩy sang luồng giao diện thì mỗi thay đổi sẽ làm giao diện khựng một nhịp.
        /// Chỉ phần cập nhật control mới được chuyển về luồng giao diện.
        /// </remarks>
        private void MonitorService_FileEventDetected(object sender, FileEventDetectedEventArgs e)
        {
            if (e == null || e.Entry == null)
            {
                return;
            }

            try
            {
                LogStorage.Append(e.Entry);
            }
            catch (Exception)
            {
                // Không ghi được nhật ký (đĩa đầy, tệp đang bị khóa...) thì vẫn phải
                // hiển thị sự kiện lên bảng, không được để luồng sự kiện chết theo.
            }

            // Form có thể đã đóng trong lúc sự kiện đang trên đường tới.
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            lock (pendingLock)
            {
                pendingEvents.Add(e.Entry);
            }

            // Chỉ xếp hàng MỘT lượt cập nhật giao diện cho cả loạt sự kiện đang dồn về.
            // Nếu gọi BeginInvoke cho từng sự kiện thì khi thư mục thay đổi dồn dập,
            // hàng đợi thông điệp của luồng giao diện bị ngập và cửa sổ đứng hẳn.
            // Interlocked.Exchange đặt cờ và trả về giá trị cũ trong một bước không thể
            // bị chen ngang, nên dù nhiều luồng cùng vào đây cũng chỉ một luồng xếp hàng.
            if (Interlocked.Exchange(ref flushScheduled, 1) == 0)
            {
                try
                {
                    // Dùng BeginInvoke (không chờ) chứ không dùng Invoke (chờ cho tới khi
                    // luồng giao diện xử lý xong). Invoke sẽ khóa luồng của FileSystemWatcher
                    // trong lúc chờ, làm bộ đệm của hệ điều hành đầy nhanh hơn, và có nguy cơ
                    // bế tắc nếu luồng giao diện lại đang chờ một khóa mà luồng này đang giữ.
                    BeginInvoke(new Action(FlushPendingEvents));
                }
                catch (InvalidOperationException)
                {
                    // Form bị đóng ngay giữa lúc xếp hàng lời gọi.
                    Interlocked.Exchange(ref flushScheduled, 0);
                }
            }
        }

        /// <summary>
        /// Đưa toàn bộ bản ghi đang chờ lên bảng. Luôn chạy trên luồng giao diện.
        /// </summary>
        private void FlushPendingEvents()
        {
            // Hạ cờ TRƯỚC khi lấy dữ liệu ra: sự kiện đến trong lúc đang cập nhật sẽ
            // xếp hàng được một lượt mới, không bị bỏ sót.
            Interlocked.Exchange(ref flushScheduled, 0);

            List<FileEventLog> batch;
            lock (pendingLock)
            {
                if (pendingEvents.Count == 0)
                {
                    return;
                }

                batch = new List<FileEventLog>(pendingEvents);
                pendingEvents.Clear();
            }

            if (IsDisposed || dgvEvents.IsDisposed)
            {
                return;
            }

            flushCount++;

            // Tắt vẽ lại trong lúc thêm cả lô để bảng không nháy và không vẽ lại từng dòng.
            dgvEvents.SuspendLayout();
            try
            {
                foreach (FileEventLog entry in batch)
                {
                    AddEventRow(entry);
                }
            }
            finally
            {
                dgvEvents.ResumeLayout();
            }

            UpdateEventCount();
        }

        /// <summary>
        /// Thêm một dòng lên đầu bảng sự kiện. Luôn chạy trên luồng giao diện.
        /// </summary>
        private void AddEventRow(FileEventLog entry)
        {
            if (entry == null || dgvEvents.IsDisposed)
            {
                return;
            }

            // Chèn lên đầu để thay đổi mới nhất luôn nhìn thấy ngay, không phải cuộn xuống.
            dgvEvents.Rows.Insert(0, new object[]
            {
                entry.Time.ToString("HH:mm:ss"),
                entry.EventType.ToString(),
                entry.FileName,
                entry.FullPath
            });

            // Với sự kiện đổi tên, đưa tên cũ vào chú thích của ô đường dẫn:
            // bảng chỉ có 4 cột theo thiết kế, nhưng thông tin này không được để mất.
            if (entry.EventType == FileEventType.Renamed && !string.IsNullOrEmpty(entry.OldFullPath))
            {
                dgvEvents.Rows[0].Cells[3].ToolTipText = "Tên cũ: " + entry.OldFullPath;
            }

            // Cắt bớt phần cũ nhất khi bảng quá dài. Dữ liệu đầy đủ vẫn nằm trong tệp nhật ký.
            while (dgvEvents.Rows.Count > MaxDisplayedEvents)
            {
                dgvEvents.Rows.RemoveAt(dgvEvents.Rows.Count - 1);
            }

            sessionEventCount++;
        }

        /// <summary>
        /// Xử lý sự cố do phần lõi báo lên (tràn bộ đệm, mất thư mục đang theo dõi...).
        /// </summary>
        /// <remarks>
        /// FileMonitorService phát sự kiện trên LUỒNG NỀN của FileSystemWatcher.
        /// Windows Forms chỉ cho phép đụng tới control từ đúng luồng đã tạo ra nó,
        /// nên phải chuyển lời gọi về luồng giao diện bằng BeginInvoke trước khi cập nhật.
        /// </remarks>
        private void MonitorService_ErrorOccurred(object sender, MonitorErrorEventArgs e)
        {
            // Form có thể đã đóng trong lúc sự kiện đang trên đường tới.
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler<MonitorErrorEventArgs>(MonitorService_ErrorOccurred),
                    new object[] { sender, e });
                return;
            }

            if (e != null && e.IsBufferOverflow)
            {
                HandleBufferOverflow();
                return;
            }

            // Sự cố khiến bộ theo dõi không chạy được nữa: dừng ở đây, tức là sau khi đã
            // về luồng giao diện, chứ không dừng ngay bên trong lời gọi lại của watcher.
            monitorService.Stop();
            SetMonitoringState(false);

            Exception error = e != null ? e.Error : null;

            MessageBox.Show(this,
                "Quá trình giám sát đã dừng do gặp sự cố." + Environment.NewLine +
                Environment.NewLine +
                "Nguyên nhân thường gặp: thư mục đang theo dõi bị xóa, bị đổi tên, " +
                "hoặc nằm trên ổ đĩa mạng đã ngắt kết nối." + Environment.NewLine +
                Environment.NewLine + "Chi tiết: " + (error != null ? error.Message : "không rõ"),
                "Lỗi giám sát",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Xử lý tình huống tràn bộ đệm: vẫn tiếp tục giám sát, chỉ báo cho người dùng biết
        /// rằng nhật ký đã bị thiếu một khoảng.
        /// </summary>
        /// <remarks>
        /// Cố tình KHÔNG hiện hộp thoại ở đây, vì hai lẽ:
        /// - Tràn bộ đệm thường xảy ra thành chuỗi khi thư mục đang bị thay đổi dồn dập;
        ///   mỗi lần một hộp thoại thì người dùng không thể làm gì khác.
        /// - Hộp thoại là loại chặn (modal), trong lúc nó mở thì các sự kiện tiếp theo
        ///   chỉ xếp hàng chờ, càng làm tình hình tệ hơn.
        ///
        /// Thay vào đó dùng nhãn trạng thái đổi màu kèm số lần bỏ sót, và chú thích
        /// giải thích nguyên nhân khi người dùng đưa chuột vào.
        /// </remarks>
        private void HandleBufferOverflow()
        {
            overflowCount++;
            UpdateStatusLabel();
        }

        /// <summary>
        /// Cập nhật giao diện theo trạng thái đang giám sát hay đang nghỉ.
        /// </summary>
        /// <param name="isMonitoring">true khi bộ theo dõi đang chạy.</param>
        private void SetMonitoringState(bool monitoring)
        {
            isMonitoring = monitoring;

            // Khóa phần cấu hình trong lúc đang chạy, nếu không cấu hình hiển thị
            // sẽ không còn khớp với cấu hình mà phần lõi đang thực sự dùng.
            txtFolderPath.Enabled = !isMonitoring;
            btnBrowse.Enabled = !isMonitoring;
            chkIncludeSubdirs.Enabled = !isMonitoring;
            cboFileFilter.Enabled = !isMonitoring;

            UpdateStatusLabel();
            UpdateButtonStates();
        }

        /// <summary>
        /// Cập nhật nhãn trạng thái theo tình hình hiện tại, kể cả khi đã có lần bỏ sót.
        /// </summary>
        private void UpdateStatusLabel()
        {
            if (!isMonitoring)
            {
                lblStatus.Text = "● Chưa giám sát";
                lblStatus.ForeColor = Color.Gray;
                toolTipStatus.SetToolTip(lblStatus, string.Empty);
                return;
            }

            if (overflowCount > 0)
            {
                // Màu cam: vẫn đang chạy nhưng dữ liệu không còn đầy đủ.
                lblStatus.Text = "● Đang giám sát — bỏ sót " + overflowCount.ToString("N0") + " lần";
                lblStatus.ForeColor = Color.FromArgb(200, 100, 0);
                toolTipStatus.SetToolTip(lblStatus,
                    "Bộ đệm của hệ điều hành đã bị tràn " + overflowCount.ToString("N0") + " lần." +
                    Environment.NewLine +
                    "Một số thay đổi trong những khoảng đó không được ghi nhận." +
                    Environment.NewLine + Environment.NewLine +
                    "Cách giảm bớt: thu hẹp phạm vi theo dõi (bỏ chọn thư mục con) " +
                    "hoặc chọn bộ lọc phần mở rộng cụ thể thay vì tất cả tệp.");
                return;
            }

            lblStatus.Text = "● Đang giám sát";
            lblStatus.ForeColor = Color.FromArgb(16, 124, 16);
            toolTipStatus.SetToolTip(lblStatus, "Đang theo dõi bình thường, chưa bỏ sót thay đổi nào.");
        }

        /// <summary>
        /// Bật/tắt các nút theo dữ liệu và trạng thái hiện có.
        /// Gom về một chỗ để không có nút nào bị bỏ sót khi trạng thái thay đổi.
        /// </summary>
        /// <remarks>
        /// Nguyên tắc: nút nào không dùng được thì làm mờ, thay vì để người dùng
        /// bấm rồi mới hiện hộp thoại báo không làm được.
        /// </remarks>
        private void UpdateButtonStates()
        {
            // Chỉ bắt đầu được khi đang rảnh và đã có đường dẫn.
            btnStart.Enabled = !isMonitoring && txtFolderPath.Text.Trim().Length > 0;
            btnStop.Enabled = isMonitoring;

            // Chỉ xuất được thứ đang hiển thị trên bảng.
            btnExportLog.Enabled = loadedLogEntries.Count > 0;

            // Buộc phải bấm "Tải log" trước khi xóa, để người dùng nhìn thấy
            // mình sắp xóa cái gì. Xóa nhật ký là thao tác không hoàn tác được.
            btnClearLog.Enabled = allLogEntries.Count > 0;
        }

        /// <summary>
        /// Gõ hoặc xóa đường dẫn thì cập nhật lại nút "Bắt đầu giám sát" ngay.
        /// </summary>
        private void txtFolderPath_TextChanged(object sender, EventArgs e)
        {
            UpdateButtonStates();
        }

        /// <summary>
        /// Giải phóng bộ theo dõi khi đóng chương trình để không bỏ sót tài nguyên.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            monitorService.FileEventDetected -= MonitorService_FileEventDetected;
            monitorService.ErrorOccurred -= MonitorService_ErrorOccurred;
            monitorService.Dispose();
        }

        #endregion

        #region Danh sách sự kiện

        /// <summary>
        /// Cập nhật nhãn tổng số sự kiện của phiên giám sát hiện tại.
        /// </summary>
        private void UpdateEventCount()
        {
            lblEventCount.Text = "Tổng số sự kiện: " + sessionEventCount.ToString("N0");
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
