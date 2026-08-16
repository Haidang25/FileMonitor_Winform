using System;
using System.Collections.Generic;
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
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            SetCueBanner(txtFolderPath, "Ví dụ: D:\\MonitorTest");
            LoadFileFilters();
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
