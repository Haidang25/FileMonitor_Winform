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
            // Tham số wParam = 1: vẫn giữ gợi ý khi ô nhập đang được chọn.
            SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, hint);
        }

        #endregion
    }
}
