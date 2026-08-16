namespace FileMonitorApps
{
    partial class MainForm
    {
        /// <summary>
        /// Biến cần thiết cho trình thiết kế (Designer).
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Giải phóng tài nguyên đang được sử dụng.
        /// </summary>
        /// <param name="disposing">true nếu cần giải phóng tài nguyên được quản lý; ngược lại là false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Phương thức bắt buộc cho Designer - không chỉnh sửa nội dung
        /// của phương thức này bằng trình soạn thảo mã.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabMonitor = new System.Windows.Forms.TabPage();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtFolderPath = new System.Windows.Forms.TextBox();
            this.lblFolderPath = new System.Windows.Forms.Label();
            this.lblFilter = new System.Windows.Forms.Label();
            this.cboFileFilter = new System.Windows.Forms.ComboBox();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.tabMain.SuspendLayout();
            this.tabMonitor.SuspendLayout();
            this.SuspendLayout();
            //
            // tabMain
            //
            this.tabMain.Controls.Add(this.tabMonitor);
            this.tabMain.Controls.Add(this.tabLog);
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabMain.Location = new System.Drawing.Point(0, 0);
            this.tabMain.Name = "tabMain";
            this.tabMain.Padding = new System.Drawing.Point(12, 4);
            this.tabMain.SelectedIndex = 0;
            this.tabMain.Size = new System.Drawing.Size(884, 561);
            this.tabMain.TabIndex = 0;
            //
            // tabMonitor
            //
            this.tabMonitor.BackColor = System.Drawing.SystemColors.Control;
            this.tabMonitor.Controls.Add(this.cboFileFilter);
            this.tabMonitor.Controls.Add(this.lblFilter);
            this.tabMonitor.Controls.Add(this.btnBrowse);
            this.tabMonitor.Controls.Add(this.txtFolderPath);
            this.tabMonitor.Controls.Add(this.lblFolderPath);
            this.tabMonitor.Location = new System.Drawing.Point(4, 26);
            this.tabMonitor.Name = "tabMonitor";
            this.tabMonitor.Padding = new System.Windows.Forms.Padding(3);
            this.tabMonitor.Size = new System.Drawing.Size(876, 531);
            this.tabMonitor.TabIndex = 0;
            this.tabMonitor.Text = "Giám sát";
            //
            // btnBrowse
            //
            this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowse.Location = new System.Drawing.Point(752, 17);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(110, 27);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "Chọn thư mục";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            //
            // txtFolderPath
            //
            this.txtFolderPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtFolderPath.Location = new System.Drawing.Point(140, 19);
            this.txtFolderPath.Name = "txtFolderPath";
            this.txtFolderPath.Size = new System.Drawing.Size(600, 23);
            this.txtFolderPath.TabIndex = 1;
            //
            // lblFolderPath
            //
            this.lblFolderPath.AutoSize = true;
            this.lblFolderPath.Location = new System.Drawing.Point(16, 22);
            this.lblFolderPath.Name = "lblFolderPath";
            this.lblFolderPath.Size = new System.Drawing.Size(112, 17);
            this.lblFolderPath.TabIndex = 0;
            this.lblFolderPath.Text = "Thư mục giám sát:";
            //
            // lblFilter
            //
            this.lblFilter.AutoSize = true;
            this.lblFilter.Location = new System.Drawing.Point(260, 63);
            this.lblFilter.Name = "lblFilter";
            this.lblFilter.Size = new System.Drawing.Size(114, 17);
            this.lblFilter.TabIndex = 3;
            this.lblFilter.Text = "Lọc phần mở rộng:";
            //
            // cboFileFilter
            //
            this.cboFileFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboFileFilter.FormattingEnabled = true;
            this.cboFileFilter.Location = new System.Drawing.Point(390, 59);
            this.cboFileFilter.Name = "cboFileFilter";
            this.cboFileFilter.Size = new System.Drawing.Size(230, 25);
            this.cboFileFilter.TabIndex = 4;
            //
            // tabLog
            //
            this.tabLog.BackColor = System.Drawing.SystemColors.Control;
            this.tabLog.Location = new System.Drawing.Point(4, 26);
            this.tabLog.Name = "tabLog";
            this.tabLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabLog.Size = new System.Drawing.Size(876, 531);
            this.tabLog.TabIndex = 1;
            this.tabLog.Text = "Nhật ký";
            //
            // folderBrowserDialog
            //
            this.folderBrowserDialog.Description = "Chọn thư mục cần giám sát thay đổi tệp tin:";
            this.folderBrowserDialog.ShowNewFolderButton = false;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 561);
            this.Controls.Add(this.tabMain);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MinimumSize = new System.Drawing.Size(700, 420);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "FileMonitor";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.tabMain.ResumeLayout(false);
            this.tabMonitor.ResumeLayout(false);
            this.tabMonitor.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabMonitor;
        private System.Windows.Forms.TabPage tabLog;
        private System.Windows.Forms.Label lblFolderPath;
        private System.Windows.Forms.TextBox txtFolderPath;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Label lblFilter;
        private System.Windows.Forms.ComboBox cboFileFilter;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}
