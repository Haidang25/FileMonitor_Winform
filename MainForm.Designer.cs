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
            if (disposing)
            {
                // Giải phóng phần lõi ở đây chứ không chỉ trong FormClosing:
                // Form có thể bị Dispose mà không qua FormClosing (ví dụ tạo ra để
                // kiểm thử rồi hủy, hoặc bị đóng bằng Dispose trực tiếp).
                if (monitorService != null)
                {
                    monitorService.Dispose();
                }

                if (components != null)
                {
                    components.Dispose();
                }
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
            System.Windows.Forms.DataGridViewCellStyle alternatingRowStyle = new System.Windows.Forms.DataGridViewCellStyle();
            this.components = new System.ComponentModel.Container();
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabMonitor = new System.Windows.Forms.TabPage();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtFolderPath = new System.Windows.Forms.TextBox();
            this.lblFolderPath = new System.Windows.Forms.Label();
            this.lblFilter = new System.Windows.Forms.Label();
            this.cboFileFilter = new System.Windows.Forms.ComboBox();
            this.chkIncludeSubdirs = new System.Windows.Forms.CheckBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnClearView = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.dgvEvents = new System.Windows.Forms.DataGridView();
            this.colTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colEventType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFileName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFullPath = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.lblEventCount = new System.Windows.Forms.Label();
            this.btnLoadLog = new System.Windows.Forms.Button();
            this.btnExportLog = new System.Windows.Forms.Button();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.dgvLogHistory = new System.Windows.Forms.DataGridView();
            this.colLogTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colLogType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colLogFileName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colLogFullPath = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.toolTipMain = new System.Windows.Forms.ToolTip(this.components);
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.cboEventTypeFilter = new System.Windows.Forms.ComboBox();
            this.lblFrom = new System.Windows.Forms.Label();
            this.dtpFrom = new System.Windows.Forms.DateTimePicker();
            this.lblTo = new System.Windows.Forms.Label();
            this.dtpTo = new System.Windows.Forms.DateTimePicker();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            ((System.ComponentModel.ISupportInitialize)(this.dgvEvents)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvLogHistory)).BeginInit();
            this.tabLog.SuspendLayout();
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
            this.tabMonitor.Controls.Add(this.lblStatus);
            this.tabMonitor.Controls.Add(this.btnClearView);
            this.tabMonitor.Controls.Add(this.btnStop);
            this.tabMonitor.Controls.Add(this.btnStart);
            this.tabMonitor.Controls.Add(this.lblEventCount);
            this.tabMonitor.Controls.Add(this.dgvEvents);
            this.tabMonitor.Controls.Add(this.chkIncludeSubdirs);
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
            this.txtFolderPath.TextChanged += new System.EventHandler(this.txtFolderPath_TextChanged);
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
            // chkIncludeSubdirs
            //
            this.chkIncludeSubdirs.AutoSize = true;
            this.chkIncludeSubdirs.Checked = true;
            this.chkIncludeSubdirs.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkIncludeSubdirs.Location = new System.Drawing.Point(19, 62);
            this.chkIncludeSubdirs.Name = "chkIncludeSubdirs";
            this.chkIncludeSubdirs.Size = new System.Drawing.Size(151, 21);
            this.chkIncludeSubdirs.TabIndex = 3;
            this.chkIncludeSubdirs.Text = "Bao gồm thư mục con";
            this.chkIncludeSubdirs.UseVisualStyleBackColor = true;
            //
            // lblFilter
            //
            this.lblFilter.AutoSize = true;
            this.lblFilter.Location = new System.Drawing.Point(260, 63);
            this.lblFilter.Name = "lblFilter";
            this.lblFilter.Size = new System.Drawing.Size(114, 17);
            this.lblFilter.TabIndex = 4;
            this.lblFilter.Text = "Lọc phần mở rộng:";
            //
            // cboFileFilter
            //
            this.cboFileFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboFileFilter.FormattingEnabled = true;
            this.cboFileFilter.Location = new System.Drawing.Point(390, 59);
            this.cboFileFilter.Name = "cboFileFilter";
            this.cboFileFilter.Size = new System.Drawing.Size(230, 25);
            this.cboFileFilter.TabIndex = 5;
            //
            // btnStart
            //
            this.btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(215)))));
            this.btnStart.FlatAppearance.BorderSize = 0;
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Location = new System.Drawing.Point(19, 100);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(170, 34);
            this.btnStart.TabIndex = 6;
            this.btnStart.Text = "Bắt đầu giám sát";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            //
            // btnStop
            //
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(205, 100);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(170, 34);
            this.btnStop.TabIndex = 7;
            this.btnStop.Text = "Dừng giám sát";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            //
            // btnClearView
            //
            this.btnClearView.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearView.Enabled = false;
            this.btnClearView.Location = new System.Drawing.Point(687, 481);
            this.btnClearView.Name = "btnClearView";
            this.btnClearView.Size = new System.Drawing.Size(170, 30);
            this.btnClearView.TabIndex = 11;
            this.btnClearView.Text = "Xóa danh sách";
            this.btnClearView.UseVisualStyleBackColor = true;
            this.toolTipMain.SetToolTip(this.btnClearView, "Chỉ xóa danh sách đang hiển thị." +
                " Nhật ký đã ghi trong tệp vẫn còn nguyên, xem lại ở tab Nhật ký.");
            this.btnClearView.Click += new System.EventHandler(this.btnClearView_Click);
            //
            // lblStatus
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Location = new System.Drawing.Point(657, 105);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(200, 24);
            this.lblStatus.TabIndex = 8;
            this.lblStatus.Text = "● Chưa giám sát";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // dgvEvents
            //
            this.dgvEvents.AllowUserToAddRows = false;
            this.dgvEvents.AllowUserToDeleteRows = false;
            this.dgvEvents.AllowUserToResizeRows = false;
            this.dgvEvents.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            alternatingRowStyle.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(247)))), ((int)(((byte)(247)))), ((int)(((byte)(247)))));
            this.dgvEvents.AlternatingRowsDefaultCellStyle = alternatingRowStyle;
            this.dgvEvents.BackgroundColor = System.Drawing.SystemColors.Window;
            this.dgvEvents.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.dgvEvents.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvEvents.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colTime,
            this.colEventType,
            this.colFileName,
            this.colFullPath});
            this.dgvEvents.Location = new System.Drawing.Point(19, 145);
            this.dgvEvents.Name = "dgvEvents";
            this.dgvEvents.ReadOnly = true;
            this.dgvEvents.MinimumSize = new System.Drawing.Size(400, 120);
            this.dgvEvents.RowHeadersVisible = false;
            this.dgvEvents.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvEvents.Size = new System.Drawing.Size(838, 330);
            this.dgvEvents.TabIndex = 9;
            //
            // colTime
            //
            this.colTime.HeaderText = "Thời gian";
            this.colTime.Name = "colTime";
            this.colTime.ReadOnly = true;
            this.colTime.Width = 100;
            //
            // colEventType
            //
            this.colEventType.HeaderText = "Loại sự kiện";
            this.colEventType.Name = "colEventType";
            this.colEventType.ReadOnly = true;
            this.colEventType.Width = 120;
            //
            // colFileName
            //
            this.colFileName.HeaderText = "Tên tệp";
            this.colFileName.Name = "colFileName";
            this.colFileName.ReadOnly = true;
            this.colFileName.Width = 220;
            //
            // colFullPath
            //
            this.colFullPath.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colFullPath.HeaderText = "Đường dẫn";
            this.colFullPath.MinimumWidth = 200;
            this.colFullPath.Name = "colFullPath";
            this.colFullPath.ReadOnly = true;
            //
            // lblEventCount
            //
            this.lblEventCount.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblEventCount.AutoSize = true;
            this.lblEventCount.Location = new System.Drawing.Point(19, 487);
            this.lblEventCount.Name = "lblEventCount";
            this.lblEventCount.Size = new System.Drawing.Size(122, 17);
            this.lblEventCount.TabIndex = 10;
            this.lblEventCount.Text = "Tổng số sự kiện: 0";
            //
            // tabLog
            //
            this.tabLog.BackColor = System.Drawing.SystemColors.Control;
            this.tabLog.Controls.Add(this.dgvLogHistory);
            this.tabLog.Controls.Add(this.cboEventTypeFilter);
            this.tabLog.Controls.Add(this.txtSearch);
            this.tabLog.Controls.Add(this.dtpTo);
            this.tabLog.Controls.Add(this.lblTo);
            this.tabLog.Controls.Add(this.dtpFrom);
            this.tabLog.Controls.Add(this.lblFrom);
            this.tabLog.Controls.Add(this.btnClearLog);
            this.tabLog.Controls.Add(this.btnExportLog);
            this.tabLog.Controls.Add(this.btnLoadLog);
            this.tabLog.Location = new System.Drawing.Point(4, 26);
            this.tabLog.Name = "tabLog";
            this.tabLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabLog.Size = new System.Drawing.Size(876, 531);
            this.tabLog.TabIndex = 1;
            this.tabLog.Text = "Nhật ký";
            //
            // txtSearch
            //
            this.txtSearch.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSearch.Location = new System.Drawing.Point(19, 19);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(620, 23);
            this.txtSearch.TabIndex = 10;
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            //
            // cboEventTypeFilter
            //
            this.cboEventTypeFilter.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.cboEventTypeFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboEventTypeFilter.FormattingEnabled = true;
            this.cboEventTypeFilter.Location = new System.Drawing.Point(655, 18);
            this.cboEventTypeFilter.Name = "cboEventTypeFilter";
            this.cboEventTypeFilter.Size = new System.Drawing.Size(202, 25);
            this.cboEventTypeFilter.TabIndex = 11;
            this.cboEventTypeFilter.SelectedIndexChanged += new System.EventHandler(this.cboEventTypeFilter_SelectedIndexChanged);
            //
            // lblFrom
            //
            this.lblFrom.AutoSize = true;
            this.lblFrom.Location = new System.Drawing.Point(19, 63);
            this.lblFrom.Name = "lblFrom";
            this.lblFrom.Size = new System.Drawing.Size(56, 17);
            this.lblFrom.TabIndex = 20;
            this.lblFrom.Text = "Từ ngày:";
            //
            // dtpFrom
            //
            this.dtpFrom.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this.dtpFrom.Location = new System.Drawing.Point(85, 59);
            this.dtpFrom.Name = "dtpFrom";
            this.dtpFrom.Size = new System.Drawing.Size(150, 25);
            this.dtpFrom.TabIndex = 21;
            this.dtpFrom.ValueChanged += new System.EventHandler(this.dtpFrom_ValueChanged);
            //
            // lblTo
            //
            this.lblTo.AutoSize = true;
            this.lblTo.Location = new System.Drawing.Point(255, 63);
            this.lblTo.Name = "lblTo";
            this.lblTo.Size = new System.Drawing.Size(65, 17);
            this.lblTo.TabIndex = 22;
            this.lblTo.Text = "Đến ngày:";
            //
            // dtpTo
            //
            this.dtpTo.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this.dtpTo.Location = new System.Drawing.Point(330, 59);
            this.dtpTo.Name = "dtpTo";
            this.dtpTo.Size = new System.Drawing.Size(150, 25);
            this.dtpTo.TabIndex = 23;
            this.dtpTo.ValueChanged += new System.EventHandler(this.dtpTo_ValueChanged);
            //
            // btnLoadLog
            //
            this.btnLoadLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(215)))));
            this.btnLoadLog.FlatAppearance.BorderSize = 0;
            this.btnLoadLog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLoadLog.ForeColor = System.Drawing.Color.White;
            this.btnLoadLog.Location = new System.Drawing.Point(19, 100);
            this.btnLoadLog.Name = "btnLoadLog";
            this.btnLoadLog.Size = new System.Drawing.Size(120, 34);
            this.btnLoadLog.TabIndex = 0;
            this.btnLoadLog.Text = "Tải log";
            this.btnLoadLog.UseVisualStyleBackColor = false;
            this.btnLoadLog.Click += new System.EventHandler(this.btnLoadLog_Click);
            //
            // btnExportLog
            //
            this.btnExportLog.Location = new System.Drawing.Point(155, 100);
            this.btnExportLog.Name = "btnExportLog";
            this.btnExportLog.Size = new System.Drawing.Size(120, 34);
            this.btnExportLog.TabIndex = 1;
            this.btnExportLog.Text = "Xuất log";
            this.btnExportLog.UseVisualStyleBackColor = true;
            this.btnExportLog.Click += new System.EventHandler(this.btnExportLog_Click);
            //
            // btnClearLog
            //
            this.btnClearLog.Location = new System.Drawing.Point(291, 100);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(120, 34);
            this.btnClearLog.TabIndex = 2;
            this.btnClearLog.Text = "Xóa log";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            //
            // dgvLogHistory
            //
            this.dgvLogHistory.AllowUserToAddRows = false;
            this.dgvLogHistory.AllowUserToDeleteRows = false;
            this.dgvLogHistory.AllowUserToResizeRows = false;
            this.dgvLogHistory.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvLogHistory.AlternatingRowsDefaultCellStyle = alternatingRowStyle;
            this.dgvLogHistory.BackgroundColor = System.Drawing.SystemColors.Window;
            this.dgvLogHistory.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.dgvLogHistory.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvLogHistory.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colLogTime,
            this.colLogType,
            this.colLogFileName,
            this.colLogFullPath});
            this.dgvLogHistory.Location = new System.Drawing.Point(19, 145);
            this.dgvLogHistory.Name = "dgvLogHistory";
            this.dgvLogHistory.ReadOnly = true;
            this.dgvLogHistory.MinimumSize = new System.Drawing.Size(400, 120);
            this.dgvLogHistory.RowHeadersVisible = false;
            this.dgvLogHistory.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvLogHistory.Size = new System.Drawing.Size(838, 366);
            this.dgvLogHistory.TabIndex = 3;
            //
            // colLogTime
            //
            this.colLogTime.HeaderText = "Thời gian";
            this.colLogTime.Name = "colLogTime";
            this.colLogTime.ReadOnly = true;
            this.colLogTime.Width = 150;
            //
            // colLogType
            //
            this.colLogType.HeaderText = "Loại";
            this.colLogType.Name = "colLogType";
            this.colLogType.ReadOnly = true;
            this.colLogType.Width = 110;
            //
            // colLogFileName
            //
            this.colLogFileName.HeaderText = "Tên tệp";
            this.colLogFileName.Name = "colLogFileName";
            this.colLogFileName.ReadOnly = true;
            this.colLogFileName.Width = 200;
            //
            // colLogFullPath
            //
            this.colLogFullPath.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colLogFullPath.HeaderText = "Đường dẫn";
            this.colLogFullPath.MinimumWidth = 200;
            this.colLogFullPath.Name = "colLogFullPath";
            this.colLogFullPath.ReadOnly = true;
            //
            // saveFileDialog
            //
            this.saveFileDialog.DefaultExt = "csv";
            this.saveFileDialog.Filter = "Tệp CSV (*.csv)|*.csv|Tất cả các tệp (*.*)|*.*";
            this.saveFileDialog.Title = "Xuất nhật ký ra tệp CSV";
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
            this.MinimumSize = new System.Drawing.Size(820, 520);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "FileMonitor";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.dgvEvents)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvLogHistory)).EndInit();
            this.tabLog.ResumeLayout(false);
            this.tabLog.PerformLayout();
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
        private System.Windows.Forms.CheckBox chkIncludeSubdirs;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnClearView;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.DataGridView dgvEvents;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEventType;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFileName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFullPath;
        private System.Windows.Forms.Label lblEventCount;
        private System.Windows.Forms.Button btnLoadLog;
        private System.Windows.Forms.Button btnExportLog;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.DataGridView dgvLogHistory;
        private System.Windows.Forms.DataGridViewTextBoxColumn colLogTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn colLogType;
        private System.Windows.Forms.DataGridViewTextBoxColumn colLogFileName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colLogFullPath;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private System.Windows.Forms.ToolTip toolTipMain;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.ComboBox cboEventTypeFilter;
        private System.Windows.Forms.Label lblFrom;
        private System.Windows.Forms.DateTimePicker dtpFrom;
        private System.Windows.Forms.Label lblTo;
        private System.Windows.Forms.DateTimePicker dtpTo;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}
