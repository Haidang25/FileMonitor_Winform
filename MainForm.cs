using System;
using System.IO;
using System.Runtime.InteropServices;
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
        /// Xử lý sự kiện bấm nút "Chọn thư mục": mở hộp thoại duyệt thư mục
        /// và điền đường dẫn người dùng chọn vào ô txtFolderPath.
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
                    txtFolderPath.Text = folderBrowserDialog.SelectedPath;
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
        /// Trả về đường dẫn thư mục đang được chọn sau khi đã kiểm tra hợp lệ.
        /// Nếu không hợp lệ, hiển thị thông báo và trả về chuỗi rỗng.
        /// Các bước sau (bắt đầu giám sát) sẽ dùng lại phương thức này.
        /// </summary>
        private string GetValidatedFolderPath()
        {
            string path = txtFolderPath.Text.Trim();

            if (path.Length == 0)
            {
                MessageBox.Show(this,
                    "Vui lòng chọn thư mục cần giám sát.",
                    "Thiếu thông tin",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txtFolderPath.Focus();
                return string.Empty;
            }

            if (!Directory.Exists(path))
            {
                MessageBox.Show(this,
                    "Thư mục không tồn tại hoặc không truy cập được:" + Environment.NewLine + path,
                    "Đường dẫn không hợp lệ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txtFolderPath.Focus();
                txtFolderPath.SelectAll();
                return string.Empty;
            }

            return path;
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
