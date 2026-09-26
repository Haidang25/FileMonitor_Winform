using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        /// Bộ đếm sự kiện của phiên giám sát hiện tại, tách theo từng loại.
        /// </summary>
        /// <remarks>
        /// Không lấy số liệu từ dgvEvents.Rows.Count vì bảng chỉ giữ lại một số dòng gần
        /// nhất, còn con số này phải phản ánh tổng số thay đổi thực sự đã bắt được.
        /// </remarks>
        private readonly EventCounter eventCounter = new EventCounter();

        /// <summary>
        /// Đối tượng đọc/ghi tệp nhật ký. Cả chương trình dùng đúng một đối tượng,
        /// vì khóa ghi tệp là khóa của từng đối tượng.
        /// </summary>
        private readonly LogService logService = new LogService();

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
        /// Nhật ký của khoảng ngày đang chọn, đọc từ tệp, chưa lọc theo loại và từ khóa.
        /// Nhờ vậy khi người dùng gõ tìm kiếm hoặc đổi bộ lọc thì chỉ cần lọc lại
        /// trên bộ nhớ, không phải đọc lại tệp mỗi lần nhấn phím.
        /// </summary>
        private List<FileEventLog> allLogEntries = new List<FileEventLog>();

        /// <summary>
        /// Người dùng đã bấm "Tải log" ít nhất một lần chưa. Từ lúc đó, đổi khoảng ngày
        /// sẽ đọc lại đúng các tệp của khoảng ngày mới thay vì chỉ lọc trên bộ nhớ.
        /// </summary>
        private bool logLoaded;

        /// <summary>
        /// Số lần ghi nhật ký thất bại của LogService tại thời điểm bắt đầu phiên giám sát.
        /// Lấy hiệu với con số hiện tại để ra số lần lỗi của riêng phiên này.
        /// </summary>
        private int writeFailuresAtSessionStart;

        public MainForm()
        {
            InitializeComponent();

            monitorService.FileEventDetected += MonitorService_FileEventDetected;
            monitorService.ErrorOccurred += MonitorService_ErrorOccurred;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            SetCueBanner(txtFolderPath, "Ví dụ: D:\\MonitorTest");

            // Số canh phải để các kích thước thẳng hàng theo hàng đơn vị, dễ so sánh.
            colSize.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colLogSize.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            LoadFileFilters();
            UpdateEventCount();
            SetMonitoringState(false);
            InitDateFilter();
            LoadEventTypeFilters();
            SetCueBanner(txtSearch, "Tìm theo tên tệp hoặc đường dẫn (không cần dấu)...");
            ImportLegacyLog();
        }

        /// <summary>
        /// Chuyển nhật ký của phiên bản cũ (một tệp duy nhất) sang các tệp theo ngày.
        /// Chỉ làm một lần; lỗi ở đây không được phép chặn chương trình mở lên.
        /// </summary>
        private void ImportLegacyLog()
        {
            try
            {
                logService.ImportLegacyLog();
            }
            catch (Exception)
            {
                // Tệp cũ vẫn còn nguyên và sẽ được thử lại ở lần mở sau.
            }
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
                    if (TryValidateFolder(folderBrowserDialog.SelectedPath, false, out normalizedPath))
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
        /// <param name="confirmWarnings">
        /// true khi sắp bắt đầu giám sát: hỏi lại người dùng nếu có cảnh báo (ví dụ ổ mạng).
        /// false khi chỉ vừa chọn thư mục: chưa cần hỏi, lúc bấm "Bắt đầu" sẽ hỏi.
        /// </param>
        /// <param name="normalizedPath">Đường dẫn đã chuẩn hóa, chỉ có giá trị khi hàm trả về true.</param>
        /// <returns>true nếu dùng được thư mục này.</returns>
        /// <remarks>
        /// Toàn bộ quy tắc kiểm tra nằm ở FolderValidator. Form chỉ truyền vào những gì
        /// đang chọn trên giao diện và hiển thị kết quả.
        /// </remarks>
        private bool TryValidateFolder(string rawPath, bool confirmWarnings, out string normalizedPath)
        {
            FolderValidationResult result = FolderValidator.Validate(
                rawPath, logService.LogFolder, GetSelectedFilter(), chkIncludeSubdirs.Checked);

            normalizedPath = result.NormalizedPath;

            if (!result.IsValid)
            {
                MessageBox.Show(this,
                    result.ErrorMessage,
                    "Thư mục không hợp lệ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            if (confirmWarnings && result.Warning.Length > 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    result.Warning,
                    "Lưu ý về thư mục giám sát",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                return answer == DialogResult.Yes;
            }

            return true;
        }

        /// <summary>
        /// Trả về đường dẫn thư mục đang được chọn sau khi đã kiểm tra hợp lệ,
        /// đồng thời hiển thị lại dạng đã chuẩn hóa trong ô nhập.
        /// Nếu không hợp lệ, hiển thị thông báo, đưa con trỏ về ô nhập và trả về chuỗi rỗng.
        /// </summary>
        private string GetValidatedFolderPath()
        {
            string normalizedPath;

            if (!TryValidateFolder(txtFolderPath.Text, true, out normalizedPath))
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
        /// Bấm "Tải log": đọc nhật ký của khoảng ngày đang chọn và đổ vào bảng.
        /// </summary>
        private void btnLoadLog_Click(object sender, EventArgs e)
        {
            LoadLogFromDisk(true);
        }

        /// <summary>
        /// Đọc lại nhật ký của khoảng ngày đang chọn từ đĩa, rồi áp bộ lọc loại và từ khóa.
        /// </summary>
        /// <param name="showMessages">
        /// true khi người dùng bấm "Tải log": báo rõ nếu không có dữ liệu hoặc có tệp lỗi.
        /// false khi tự đọc lại do đổi ngày: không hiện hộp thoại nào, tránh làm phiền
        /// người dùng đang bấm chọn ngày.
        /// </param>
        /// <remarks>
        /// Chỉ đọc tệp của khoảng ngày đang chọn (LogService.ReadRange), không đọc cả lịch sử.
        /// Lọc theo loại và từ khóa thì làm trên bộ nhớ ở ApplyLogFilters, nên gõ tìm kiếm
        /// không phải đọc lại đĩa.
        /// </remarks>
        private void LoadLogFromDisk(bool showMessages)
        {
            try
            {
                allLogEntries = logService.ReadRange(dtpFrom.Value, dtpTo.Value);
                logLoaded = true;
            }
            catch (Exception ex)
            {
                // ReadRange tự bỏ qua từng tệp lỗi; tới được đây là lỗi ở mức thư mục,
                // ví dụ không có quyền liệt kê thư mục Logs.
                if (showMessages)
                {
                    MessageBox.Show(this,
                        "Không đọc được thư mục nhật ký:" + Environment.NewLine + logService.LogFolder +
                        Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message,
                        "Lỗi",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return;
            }

            ApplyLogFilters();

            if (showMessages)
            {
                ReportLoadResult();
            }
        }

        /// <summary>
        /// Báo cho người dùng kết quả của lần "Tải log" vừa rồi, nếu có gì đáng chú ý.
        /// </summary>
        /// <remarks>
        /// Phân biệt rõ ba tình huống dễ nhầm với nhau:
        /// - Không có nhật ký nào trong khoảng ngày đang chọn.
        /// - Có nhật ký, nhưng bộ lọc loại / từ khóa đã loại hết.
        /// - Có tệp hoặc dòng không đọc được (bảng đang thiếu dữ liệu).
        /// Nếu không nói rõ, người dùng sẽ tưởng chương trình không ghi được nhật ký.
        /// </remarks>
        private void ReportLoadResult()
        {
            string range = dtpFrom.Value.ToString("dd/MM/yyyy") + " – " + dtpTo.Value.ToString("dd/MM/yyyy");

            if (logService.LastReadError.Length > 0 || logService.LastReadSkippedLines > 0)
            {
                string problem = string.Empty;

                if (logService.LastReadError.Length > 0)
                {
                    problem += "Một số tệp không đọc được (thường do đang mở trong chương trình khác):" +
                        Environment.NewLine + logService.LastReadError + Environment.NewLine + Environment.NewLine;
                }

                if (logService.LastReadSkippedLines > 0)
                {
                    problem += "Đã bỏ qua " + logService.LastReadSkippedLines.ToString("N0") +
                        " dòng sai định dạng (tệp bị sửa tay hoặc chương trình bị tắt giữa lúc ghi)." +
                        Environment.NewLine + Environment.NewLine;
                }

                MessageBox.Show(this,
                    problem + "Bảng đang hiển thị " + loadedLogEntries.Count.ToString("N0") +
                    " bản ghi đọc được.",
                    "Nhật ký chưa đầy đủ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (allLogEntries.Count == 0)
            {
                MessageBox.Show(this,
                    "Không có nhật ký nào trong khoảng " + range + "." + Environment.NewLine +
                    Environment.NewLine + "Thư mục nhật ký: " + logService.LogFolder,
                    "Nhật ký trống",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (loadedLogEntries.Count == 0)
            {
                MessageBox.Show(this,
                    "Không có bản ghi nào khớp với bộ lọc hiện tại." + Environment.NewLine +
                    Environment.NewLine + "Khoảng " + range + " có " +
                    allLogEntries.Count.ToString("N0") + " bản ghi. " +
                    "Hãy thử xóa từ khóa tìm kiếm hoặc chọn lại \"Tất cả loại\".",
                    "Không có dữ liệu phù hợp",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Thư mục người dùng đã chọn ở lần xuất gần nhất trong phiên làm việc này.
        /// Lần xuất sau mở hộp thoại ngay tại đó, không phải duyệt lại từ đầu.
        /// </summary>
        private string lastExportFolder = string.Empty;

        /// <summary>Vị trí lựa chọn "dấu chấm phẩy" trong bộ lọc của SaveFileDialog (đánh số từ 1).</summary>
        private const int ExportFormatSemicolon = 1;

        /// <summary>Vị trí lựa chọn "dấu phẩy" trong bộ lọc của SaveFileDialog.</summary>
        private const int ExportFormatComma = 2;

        /// <summary>
        /// Bấm "Xuất log": cho người dùng chọn nơi lưu, rồi ghi danh sách đang hiển thị ra tệp CSV.
        /// </summary>
        /// <remarks>
        /// Xuất đúng loadedLogEntries — những gì đang thấy trên bảng sau khi đã lọc —
        /// chứ không phải toàn bộ nhật ký. Người dùng lọc ra 12 dòng thì tệp có 12 dòng.
        /// </remarks>
        private void btnExportLog_Click(object sender, EventArgs e)
        {
            // Nút đã bị làm mờ khi không có dữ liệu, đây chỉ là chốt chặn phòng xa.
            if (loadedLogEntries.Count == 0)
            {
                return;
            }

            string destinationPath;
            char separator;
            if (!AskExportDestination(out destinationPath, out separator))
            {
                return;
            }

            int exported;
            Cursor previousCursor = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                exported = logService.ExportCsv(destinationPath, loadedLogEntries, separator);
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowExportError(destinationPath,
                    "Không có quyền ghi vào thư mục này. Hãy chọn thư mục khác, ví dụ Documents.",
                    ex);
                return;
            }
            catch (IOException ex)
            {
                // Trường hợp hay gặp nhất: tệp cùng tên đang mở trong Excel. Excel khóa
                // tệp đang mở, nên xuất đè lên nó sẽ thất bại cho tới khi đóng lại.
                ShowExportError(destinationPath,
                    "Tệp đang được mở bởi chương trình khác (thường là Excel), " +
                    "hoặc ổ đĩa không ghi được. Hãy đóng tệp rồi xuất lại, hoặc đặt tên khác.",
                    ex);
                return;
            }
            catch (Exception ex)
            {
                ShowExportError(destinationPath, "Không ghi được tệp CSV.", ex);
                return;
            }
            finally
            {
                Cursor.Current = previousCursor;
            }

            lastExportFolder = Path.GetDirectoryName(destinationPath);

            DialogResult answer = MessageBox.Show(this,
                "Đã xuất " + exported.ToString("N0") + " bản ghi ra tệp:" +
                Environment.NewLine + destinationPath +
                Environment.NewLine + Environment.NewLine + "Mở thư mục chứa tệp?",
                "Xuất thành công",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);

            if (answer == DialogResult.Yes)
            {
                ShowInExplorer(destinationPath);
            }
        }

        /// <summary>
        /// Mở SaveFileDialog để người dùng chọn nơi lưu và kiểu dấu phân cách.
        /// </summary>
        /// <param name="destinationPath">Đường dẫn tệp đã chọn, luôn có đuôi .csv.</param>
        /// <param name="separator">Dấu phân cách ứng với lựa chọn trong hộp thoại.</param>
        /// <returns>false nếu người dùng bấm Hủy.</returns>
        /// <remarks>
        /// Hai lựa chọn định dạng nằm ngay trong ô "Save as type" của hộp thoại, nên người dùng
        /// không cần thêm một hộp thoại hỏi riêng. Lựa chọn khớp với cài đặt vùng của máy được
        /// chọn sẵn: máy đặt vùng Việt Nam thì Excel tách cột bằng dấu chấm phẩy, gặp tệp
        /// phân cách bằng dấu phẩy sẽ dồn cả dòng vào một cột.
        /// </remarks>
        private bool AskExportDestination(out string destinationPath, out char separator)
        {
            destinationPath = string.Empty;
            separator = LogService.DefaultCsvSeparator;

            saveFileDialog.Title = "Xuất nhật ký ra tệp CSV";
            saveFileDialog.Filter =
                "CSV phân cách bằng dấu chấm phẩy — Excel đặt vùng Việt Nam (*.csv)|*.csv|" +
                "CSV phân cách bằng dấu phẩy — chuẩn quốc tế (*.csv)|*.csv";
            saveFileDialog.FilterIndex = GetSystemListSeparator() == ';'
                ? ExportFormatSemicolon
                : ExportFormatComma;
            saveFileDialog.DefaultExt = "csv";
            saveFileDialog.AddExtension = true;
            saveFileDialog.OverwritePrompt = true;
            saveFileDialog.CheckPathExists = true;

            // Không cho hộp thoại đổi thư mục làm việc hiện tại của chương trình.
            saveFileDialog.RestoreDirectory = true;

            saveFileDialog.InitialDirectory = Directory.Exists(lastExportFolder)
                ? lastExportFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            saveFileDialog.FileName = BuildExportFileName();

            if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return false;
            }

            string path = saveFileDialog.FileName;

            // Ép đuôi .csv dù người dùng gõ tên kèm đuôi khác. Nhờ vậy không bao giờ ghi đè
            // nhầm lên tệp nhật ký (.log) hay một tệp quan trọng nào khác của người dùng.
            if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                path += ".csv";
            }

            destinationPath = path;
            separator = saveFileDialog.FilterIndex == ExportFormatSemicolon ? ';' : ',';
            return true;
        }

        /// <summary>
        /// Gợi ý tên tệp theo bộ lọc đang áp dụng, ví dụ "nhatky_20260919-20260926_Deleted.csv".
        /// </summary>
        /// <remarks>
        /// Tên tệp mô tả luôn nội dung bên trong, để mấy hôm sau mở thư mục ra vẫn biết
        /// tệp nào là tệp nào mà không phải mở từng tệp.
        /// </remarks>
        private string BuildExportFileName()
        {
            string name = "nhatky_"
                + dtpFrom.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                + "-"
                + dtpTo.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            FilterItem selectedType = cboEventTypeFilter.SelectedItem as FilterItem;
            if (selectedType != null && selectedType.Pattern.Length > 0)
            {
                name += "_" + selectedType.Pattern;
            }

            return name + ".csv";
        }

        /// <summary>
        /// Dấu phân cách danh sách (List separator) trong cài đặt vùng của Windows —
        /// chính là dấu Excel dùng để tách cột khi mở tệp CSV.
        /// </summary>
        private static char GetSystemListSeparator()
        {
            string listSeparator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;

            if (string.IsNullOrEmpty(listSeparator))
            {
                return ',';
            }

            return listSeparator[0] == ';' ? ';' : ',';
        }

        /// <summary>
        /// Mở File Explorer và chọn sẵn tệp vừa xuất.
        /// </summary>
        private static void ShowInExplorer(string filePath)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
            }
            catch (Exception)
            {
                // Không mở được Explorer cũng không sao: tệp đã được lưu thành công,
                // và đường dẫn đã hiện trong hộp thoại ngay trước đó.
            }
        }

        /// <summary>
        /// Báo lỗi xuất tệp kèm lời khuyên cụ thể cho từng nguyên nhân.
        /// </summary>
        private void ShowExportError(string destinationPath, string advice, Exception ex)
        {
            MessageBox.Show(this,
                "Không xuất được nhật ký ra tệp:" + Environment.NewLine + destinationPath +
                Environment.NewLine + Environment.NewLine + advice +
                Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message,
                "Lỗi xuất nhật ký",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        /// <summary>
        /// Bấm "Xóa log": xóa toàn bộ tệp nhật ký sau khi người dùng xác nhận.
        /// </summary>
        /// <remarks>
        /// Xóa TẤT CẢ các ngày chứ không chỉ khoảng ngày đang xem, nên hộp thoại xác nhận
        /// nói rõ số ngày và khoảng thời gian sẽ mất, tránh người dùng tưởng chỉ xóa phần
        /// đang thấy trên bảng.
        /// </remarks>
        private void btnClearLog_Click(object sender, EventArgs e)
        {
            List<DateTime> days;
            try
            {
                days = logService.GetAvailableDays();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Không đọc được thư mục nhật ký:" + Environment.NewLine + logService.LogFolder +
                    Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (days.Count == 0)
            {
                allLogEntries.Clear();
                ApplyLogFilters();
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                "Xóa TOÀN BỘ nhật ký của " + days.Count.ToString("N0") + " ngày, từ " +
                days[0].ToString("dd/MM/yyyy") + " đến " + days[days.Count - 1].ToString("dd/MM/yyyy") + "?" +
                Environment.NewLine + Environment.NewLine +
                "Không chỉ khoảng ngày đang xem trên bảng. Thao tác này không thể hoàn tác." +
                Environment.NewLine + Environment.NewLine +
                "Nếu cần giữ lại, hãy bấm \"Xuất log\" trước.",
                "Xác nhận xóa nhật ký",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                logService.ClearAll();
            }
            catch (Exception ex)
            {
                // ClearAll đã xóa hết những tệp xóa được, chỉ báo những tệp còn sót.
                MessageBox.Show(this,
                    "Chưa xóa hết nhật ký." + Environment.NewLine + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Hãy đóng các tệp đó (thường đang mở trong Excel hoặc Notepad) rồi xóa lại.",
                    "Xóa chưa trọn vẹn",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            // Đọc lại để bảng phản ánh đúng những gì còn trên đĩa, kể cả khi xóa không hết.
            LoadLogFromDisk(false);
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
        /// Lọc lại danh sách theo cả ba tiêu chí (ngày, loại sự kiện, từ khóa)
        /// rồi hiển thị kết quả. Hàm này không hiện thông báo nào để người dùng
        /// gõ tìm kiếm mà không bị hộp thoại làm phiền.
        /// </summary>
        /// <remarks>
        /// Toàn bộ quy tắc lọc nằm ở LogFilter / LogService. Form chỉ đọc giá trị trên các
        /// control, gói vào LogFilter và hiển thị kết quả.
        /// </remarks>
        private void ApplyLogFilters()
        {
            List<FileEventLog> result = LogService.Filter(allLogEntries, BuildLogFilter());

            // Tệp được ghi nối nên thứ tự trong tệp là cũ trước, mới sau.
            // Đảo lại để bản ghi mới nhất nằm trên đầu bảng.
            result.Reverse();

            loadedLogEntries = result;
            ShowLogEntries(result);
            UpdateButtonStates();
        }

        /// <summary>
        /// Gói các điều kiện lọc đang chọn trên giao diện vào một đối tượng LogFilter.
        /// </summary>
        private LogFilter BuildLogFilter()
        {
            LogFilter filter = new LogFilter();
            filter.FromDate = dtpFrom.Value;
            filter.ToDate = dtpTo.Value;
            filter.EventType = GetSelectedEventType();
            filter.Keyword = txtSearch.Text;
            return filter;
        }

        /// <summary>
        /// Loại sự kiện đang chọn trong ComboBox, hoặc null nếu chọn "Tất cả loại".
        /// </summary>
        private FileEventType? GetSelectedEventType()
        {
            FilterItem selected = cboEventTypeFilter.SelectedItem as FilterItem;
            if (selected == null || selected.Pattern.Length == 0)
            {
                return null;
            }

            FileEventType eventType;
            if (Enum.TryParse(selected.Pattern, out eventType))
            {
                return eventType;
            }

            return null;
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
        /// Không cho phép ngày bắt đầu vượt quá ngày kết thúc.
        /// Tự chỉnh lại thay vì hiện thông báo lỗi, để người dùng đỡ bị làm phiền.
        /// </summary>
        private void dtpFrom_ValueChanged(object sender, EventArgs e)
        {
            if (dtpFrom.Value.Date > dtpTo.Value.Date)
            {
                dtpTo.Value = dtpFrom.Value.Date;
            }

            OnDateRangeChanged();
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

            OnDateRangeChanged();
        }

        /// <summary>
        /// Đổi khoảng ngày: nếu đã tải nhật ký thì đọc lại đúng các tệp của khoảng mới,
        /// vì allLogEntries chỉ chứa dữ liệu của khoảng ngày cũ.
        /// </summary>
        private void OnDateRangeChanged()
        {
            if (logLoaded)
            {
                LoadLogFromDisk(false);
            }
            else
            {
                ApplyLogFilters();
            }
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
                    int index = dgvLogHistory.Rows.Add(
                        entry.Time.ToString(DisplayTimeFormat),
                        entry.EventType.ToString(),
                        entry.FileName,
                        GetSizeText(entry),
                        entry.FullPath);

                    DataGridViewRow row = dgvLogHistory.Rows[index];

                    // Cùng cách tô màu với bảng ở tab Giám sát để hai bảng đọc giống nhau.
                    Color color = GetEventTypeColor(entry.EventType);
                    row.Cells[1].Style.BackColor = color;
                    row.Cells[1].Style.SelectionBackColor = color;
                    row.Cells[1].Style.SelectionForeColor = SystemColors.ControlText;

                    // Bảng chỉ có 4 cột; tên cũ của sự kiện đổi tên đưa vào chú thích.
                    if (entry.EventType == FileEventType.Renamed && !string.IsNullOrEmpty(entry.OldFullPath))
                    {
                        row.Cells[colLogFullPath.Index].ToolTipText = "Tên cũ: " + entry.OldFullPath;
                    }

                    row.Cells[colLogSize.Index].ToolTipText = GetSizeToolTip(entry);
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
                eventCounter.Reset();
                overflowCount = 0;
                writeFailuresAtSessionStart = logService.WriteFailureCount;
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
            if (!chkIncludeSubdirs.Checked || !FolderValidator.IsDriveRoot(folderPath))
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
                // TryAppend không ném ngoại lệ với các lỗi đĩa thường gặp (đĩa đầy, thiếu quyền,
                // tệp bị khóa): nó trả về false và ghi nhận lỗi vào WriteFailureCount /
                // LastWriteError. Nhãn trạng thái đọc hai giá trị đó để báo cho người dùng.
                logService.TryAppend(e.Entry);
            }
            catch (Exception)
            {
                // Lưới an toàn cuối cùng cho lỗi không lường trước: đang ở luồng nền của
                // FileSystemWatcher, một ngoại lệ lọt ra ngoài sẽ làm sập cả chương trình.
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

            // Ghi lại chỗ người dùng đang xem TRƯỚC khi chèn thêm dòng.
            // Dòng mới được chèn lên đầu bảng nên mọi dòng cũ bị đẩy xuống; nếu không
            // bù lại thì người đang đọc một dòng ở giữa sẽ thấy nội dung tự trượt đi.
            int firstVisibleBefore = GetFirstVisibleRowIndex();

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

            RestoreViewPosition(firstVisibleBefore, batch.Count);

            UpdateEventCount();
            UpdateButtonStates();

            // Có thể vừa có lần ghi nhật ký thất bại, cập nhật nhãn trạng thái cho kịp.
            UpdateStatusLabel();
        }

        /// <summary>
        /// Chỉ số dòng đầu tiên đang nhìn thấy, -1 nếu bảng trống.
        /// </summary>
        private int GetFirstVisibleRowIndex()
        {
            if (dgvEvents.Rows.Count == 0)
            {
                return -1;
            }

            try
            {
                return dgvEvents.FirstDisplayedScrollingRowIndex;
            }
            catch (Exception)
            {
                // Bảng chưa được vẽ lần nào thì thuộc tính này có thể ném ngoại lệ.
                return -1;
            }
        }

        /// <summary>
        /// Đưa khung nhìn về đúng bản ghi người dùng đang đọc sau khi đã chèn thêm dòng.
        /// </summary>
        /// <param name="firstVisibleBefore">Dòng đầu tiên nhìn thấy trước khi chèn.</param>
        /// <param name="insertedCount">Số dòng vừa chèn lên đầu.</param>
        /// <remarks>
        /// Chỉ cần bù cho vị trí cuộn. Dòng đang chọn thì KHÔNG phải xử lý: trạng thái
        /// chọn nằm trên chính đối tượng DataGridViewRow, nên chèn thêm dòng phía trên
        /// làm chỉ số của nó tăng lên nhưng nó vẫn là đúng bản ghi đó.
        /// Ngược lại, FirstDisplayedScrollingRowIndex lại tính theo CHỈ SỐ, nên không bù
        /// thì nội dung đang đọc sẽ tự trượt xuống.
        ///
        /// Nếu người dùng đang ở trên cùng (đang theo dòng chảy thời gian thực) thì để
        /// nguyên, vì họ muốn thấy dòng mới nhất. Chỉ bù khi họ đã cuộn xuống đọc phần cũ.
        /// </remarks>
        private void RestoreViewPosition(int firstVisibleBefore, int insertedCount)
        {
            if (insertedCount <= 0 || dgvEvents.Rows.Count == 0)
            {
                return;
            }

            // Đang ở đầu bảng thì giữ nguyên để dòng mới nhất luôn nằm trong tầm mắt.
            if (firstVisibleBefore <= 0)
            {
                return;
            }

            int target = firstVisibleBefore + insertedCount;
            if (target > dgvEvents.Rows.Count - 1)
            {
                target = dgvEvents.Rows.Count - 1;
            }

            try
            {
                dgvEvents.FirstDisplayedScrollingRowIndex = target;
            }
            catch (Exception)
            {
                // Không cuộn được thì bỏ qua: đây chỉ là tiện lợi khi xem, không phải dữ liệu.
            }
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
                GetSizeText(entry),
                entry.FullPath
            });

            DataGridViewRow row = dgvEvents.Rows[0];

            // Tô màu riêng ô "Loại sự kiện" để nhận ra loại thay đổi mà không phải đọc chữ.
            // Chỉ tô một ô chứ không tô cả dòng: tô cả dòng sẽ làm bảng rối và khó đọc
            // phần đường dẫn, vốn là nội dung dài nhất.
            row.Cells[1].Style.BackColor = GetEventTypeColor(entry.EventType);
            row.Cells[1].Style.SelectionBackColor = GetEventTypeColor(entry.EventType);
            row.Cells[1].Style.SelectionForeColor = System.Drawing.SystemColors.ControlText;

            // Với sự kiện đổi tên, đưa tên cũ vào chú thích của ô đường dẫn:
            // bảng chỉ có 4 cột theo thiết kế, nhưng thông tin này không được để mất.
            if (entry.EventType == FileEventType.Renamed && !string.IsNullOrEmpty(entry.OldFullPath))
            {
                row.Cells[colFullPath.Index].ToolTipText = "Tên cũ: " + entry.OldFullPath;
            }

            // Lý do không có kích thước (tệp bị khóa, đã bị xóa...) đưa vào chú thích ô "Kích thước".
            row.Cells[colSize.Index].ToolTipText = GetSizeToolTip(entry);

            // Cắt bớt phần cũ nhất khi bảng quá dài. Dữ liệu đầy đủ vẫn nằm trong tệp nhật ký.
            while (dgvEvents.Rows.Count > MaxDisplayedEvents)
            {
                dgvEvents.Rows.RemoveAt(dgvEvents.Rows.Count - 1);
            }

            eventCounter.Increment(entry.EventType);
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
                toolTipMain.SetToolTip(lblStatus, string.Empty);
                return;
            }

            int writeFailures = logService.WriteFailureCount - writeFailuresAtSessionStart;

            if (writeFailures > 0)
            {
                // Màu đỏ: nghiêm trọng hơn tràn bộ đệm, vì sự kiện vẫn hiện trên bảng nhưng
                // KHÔNG được lưu lại — tắt chương trình là mất.
                string text = "● Đang giám sát — lỗi ghi nhật ký " + writeFailures.ToString("N0") + " lần";
                if (overflowCount > 0)
                {
                    text += ", bỏ sót " + overflowCount.ToString("N0") + " lần";
                }

                lblStatus.Text = text;
                lblStatus.ForeColor = Color.FromArgb(196, 43, 28);
                toolTipMain.SetToolTip(lblStatus,
                    writeFailures.ToString("N0") + " sự kiện không ghi được xuống tệp nhật ký." +
                    Environment.NewLine + "Lỗi gần nhất: " + logService.LastWriteError +
                    Environment.NewLine + Environment.NewLine +
                    "Thư mục nhật ký: " + logService.LogFolder + Environment.NewLine +
                    "Hãy kiểm tra dung lượng ổ đĩa và quyền ghi vào thư mục này.");
                return;
            }

            if (overflowCount > 0)
            {
                // Màu cam: vẫn đang chạy nhưng dữ liệu không còn đầy đủ.
                lblStatus.Text = "● Đang giám sát — bỏ sót " + overflowCount.ToString("N0") + " lần";
                lblStatus.ForeColor = Color.FromArgb(200, 100, 0);
                toolTipMain.SetToolTip(lblStatus,
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
            toolTipMain.SetToolTip(lblStatus, "Đang theo dõi bình thường, chưa bỏ sót thay đổi nào.");
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

            // Chỉ xóa được khi trên bảng đang có gì đó.
            btnClearView.Enabled = dgvEvents.Rows.Count > 0;

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
        /// Dừng giám sát khi đóng chương trình.
        /// </summary>
        /// <remarks>
        /// Chỉ dừng ở đây, việc giải phóng để cho Dispose của Form lo (xem
        /// MainForm.Designer.cs). Tách như vậy vì FormClosing không phải lúc nào cũng
        /// chạy — Form bị Dispose trực tiếp thì sự kiện này không phát ra.
        /// Hủy đăng ký trước khi dừng để không nhận thêm sự kiện đến muộn.
        /// </remarks>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            monitorService.FileEventDetected -= MonitorService_FileEventDetected;
            monitorService.ErrorOccurred -= MonitorService_ErrorOccurred;
            monitorService.Stop();
        }

        #endregion

        #region Danh sách sự kiện

        /// <summary>
        /// Chữ hiển thị trong cột "Kích thước".
        /// </summary>
        /// <remarks>
        /// Phân biệt ba trường hợp không có con số, vì ý nghĩa khác nhau:
        /// - Deleted: bỏ trống — tệp không còn, không có gì để đo, đây không phải lỗi.
        /// - Thư mục : ghi "Thư mục" — thư mục không có kích thước.
        /// - Còn lại : "N/A" — đáng lẽ đọc được nhưng không đọc được (tệp bị khóa, đã bị xóa
        ///   ngay sau khi tạo...). Lý do cụ thể nằm trong chú thích của ô.
        /// </remarks>
        private static string GetSizeText(FileEventLog entry)
        {
            if (entry.FileSize.HasValue)
            {
                return FormatSize(entry.FileSize.Value);
            }

            if (entry.EventType == FileEventType.Deleted)
            {
                return string.Empty;
            }

            if (entry.Note == FileSizeProbe.NoteDirectory)
            {
                return FileSizeProbe.NoteDirectory;
            }

            return "N/A";
        }

        /// <summary>
        /// Chú thích khi đưa chuột vào ô "Kích thước": số byte chính xác, hoặc lý do không có.
        /// </summary>
        private static string GetSizeToolTip(FileEventLog entry)
        {
            if (entry.FileSize.HasValue)
            {
                return entry.FileSize.Value.ToString("N0") + " byte";
            }

            return entry.Note ?? string.Empty;
        }

        /// <summary>
        /// Đổi số byte thành dạng dễ đọc: 512 B, 1,5 KB, 3,2 MB...
        /// Dùng định dạng số của máy người dùng (dấu thập phân là dấu phẩy với máy tiếng Việt).
        /// </summary>
        internal static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? bytes.ToString("N0") + " B"
                : value.ToString("0.#") + " " + units[unit];
        }

        /// <summary>
        /// Màu nền của ô "Loại sự kiện" theo từng loại thay đổi.
        /// </summary>
        /// <remarks>
        /// Dùng màu nhạt để chữ đen vẫn đọc rõ. Ý nghĩa màu theo quy ước thông thường:
        /// xanh lá là thêm vào, vàng là sửa, đỏ là mất đi, xanh dương là di chuyển.
        /// Màu chỉ là dấu hiệu phụ - chữ trong ô vẫn ghi rõ tên loại, nên người dùng
        /// khó phân biệt màu vẫn đọc được bình thường.
        /// </remarks>
        private static Color GetEventTypeColor(FileEventType eventType)
        {
            switch (eventType)
            {
                case FileEventType.Created:
                    return Color.FromArgb(226, 245, 228);
                case FileEventType.Changed:
                    return Color.FromArgb(255, 247, 214);
                case FileEventType.Deleted:
                    return Color.FromArgb(253, 231, 232);
                case FileEventType.Renamed:
                    return Color.FromArgb(226, 238, 252);
                default:
                    return SystemColors.Window;
            }
        }

        /// <summary>
        /// Cập nhật nhãn tổng số sự kiện của phiên giám sát hiện tại.
        /// </summary>
        /// <remarks>
        /// Khi số dòng đang hiển thị khác tổng số sự kiện, ghi thêm cả hai con số.
        /// Chênh lệch xảy ra khi người dùng bấm "Xóa danh sách", hoặc khi bảng đã đầy
        /// và phần cũ nhất bị cắt bớt. Không ghi rõ thì nhãn trông như đếm sai.
        /// </remarks>
        private void UpdateEventCount()
        {
            string text = eventCounter.ToSummary();

            if (dgvEvents.Rows.Count != eventCounter.Total)
            {
                text += "   —   đang hiển thị " + dgvEvents.Rows.Count.ToString("N0");
            }

            lblEventCount.Text = text;
            toolTipMain.SetToolTip(lblEventCount, eventCounter.ToDetailedSummary());
        }

        /// <summary>
        /// Bấm "Xóa danh sách": dọn bảng sự kiện trên màn hình.
        /// </summary>
        /// <remarks>
        /// KHÔNG đụng tới tệp nhật ký — đây là điểm khác biệt so với nút "Xóa log"
        /// ở tab Nhật ký. Vì không mất dữ liệu nên cũng không cần hỏi xác nhận.
        /// Bộ đếm tổng số sự kiện được giữ nguyên: đã phát hiện bao nhiêu thay đổi
        /// là sự thật của phiên giám sát, xóa màn hình không làm điều đó thay đổi.
        /// </remarks>
        private void btnClearView_Click(object sender, EventArgs e)
        {
            dgvEvents.Rows.Clear();

            // Dọn cả những bản ghi đang chờ, nếu không chúng sẽ hiện ra ngay sau khi xóa.
            // Chúng đã được ghi vào tệp nhật ký nên không mất dữ liệu.
            lock (pendingLock)
            {
                pendingEvents.Clear();
            }

            UpdateEventCount();
            UpdateButtonStates();
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
