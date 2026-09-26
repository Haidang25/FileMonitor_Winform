using System;

namespace FileMonitorApps
{
    /// <summary>
    /// Điều kiện lọc nhật ký: khoảng ngày, loại sự kiện và từ khóa.
    /// </summary>
    /// <remarks>
    /// Gom các điều kiện vào một đối tượng để LogService nhận MỘT tham số thay vì
    /// bốn tham số rời. Thêm tiêu chí lọc mới thì chỉ cần thêm thuộc tính ở đây,
    /// không phải sửa chữ ký phương thức.
    ///
    /// Lớp chỉ chứa dữ liệu, không đọc gì từ control. MainForm tự đọc giá trị trên
    /// giao diện rồi điền vào đối tượng này.
    /// </remarks>
    internal class LogFilter
    {
        /// <summary>Ngày bắt đầu (chỉ lấy phần ngày, tính từ 00:00:00).</summary>
        public DateTime FromDate { get; set; }

        /// <summary>Ngày kết thúc (tính tới hết ngày, 23:59:59).</summary>
        public DateTime ToDate { get; set; }

        /// <summary>
        /// Loại sự kiện cần lấy. null nghĩa là lấy tất cả các loại.
        /// </summary>
        /// <remarks>
        /// Dùng kiểu nullable (FileEventType?) thay vì thêm một giá trị "All" vào enum:
        /// "tất cả" là một lựa chọn lọc, không phải một loại sự kiện có thật,
        /// và không được phép xuất hiện trong tệp nhật ký.
        /// </remarks>
        public FileEventType? EventType { get; set; }

        /// <summary>
        /// Từ khóa tìm trong tên tệp hoặc đường dẫn. Rỗng nghĩa là không lọc theo từ khóa.
        /// </summary>
        public string Keyword { get; set; }

        public LogFilter()
        {
            // Mặc định khớp với InitDateFilter của MainForm: 7 ngày gần nhất, mọi loại.
            FromDate = DateTime.Today.AddDays(-7);
            ToDate = DateTime.Today;
            EventType = null;

            // Để chuỗi rỗng thay vì null, đỡ phải kiểm tra null ở khắp nơi.
            Keyword = string.Empty;
        }

        /// <summary>
        /// Kiểm tra một bản ghi có thỏa mọi điều kiện lọc hay không.
        /// </summary>
        /// <remarks>
        /// Đặt phương thức này ở đây (thay vì trong LogService) vì nó chỉ dùng dữ liệu
        /// của chính LogFilter. Nhờ vậy kiểm thử được mà không cần tệp nào trên đĩa.
        /// </remarks>
        public bool Matches(FileEventLog entry)
        {
            if (entry == null)
            {
                return false;
            }

            // Kiểm tra loại sự kiện TRƯỚC: đây là phép so sánh rẻ nhất (so hai số nguyên),
            // loại được nhiều bản ghi nhất ngay từ đầu nên các phép so chuỗi phía sau ít phải chạy.
            return MatchesEventType(entry)
                && MatchesDate(entry)
                && MatchesKeyword(entry);
        }

        /// <summary>
        /// Bản ghi có đúng loại sự kiện đang lọc hay không.
        /// EventType = null nghĩa là "Tất cả loại", mọi bản ghi đều khớp.
        /// </summary>
        /// <remarks>
        /// So sánh trực tiếp hai giá trị enum chứ không đổi ra chuỗi rồi so như code cũ
        /// trong MainForm (entry.EventType.ToString() == "Created"): so enum là so số nguyên,
        /// nhanh hơn, và gõ sai tên loại sẽ bị trình biên dịch bắt ngay thay vì âm thầm
        /// không khớp bản ghi nào.
        /// </remarks>
        public bool MatchesEventType(FileEventLog entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (!EventType.HasValue)
            {
                return true;
            }

            return entry.EventType == EventType.Value;
        }

        /// <summary>
        /// Bản ghi có nằm trong khoảng ngày đang lọc hay không.
        /// </summary>
        public bool MatchesDate(FileEventLog entry)
        {
            // TODO (bước 3):
            //   - from = FromDate.Date
            //   - to   = ToDate.Date.AddDays(1).AddTicks(-1)   // hết ngày, xem FilterByDate cũ
            //   - return entry.Time >= from && entry.Time <= to;
            throw new NotImplementedException();
        }

        /// <summary>
        /// Tên tệp hoặc đường dẫn có chứa từ khóa hay không. Từ khóa rỗng thì khớp tất cả.
        /// </summary>
        public bool MatchesKeyword(FileEventLog entry)
        {
            // TODO (bước 5): nếu Keyword khác rỗng thì tìm trong FileName hoặc FullPath,
            //   dùng IndexOf(..., StringComparison.CurrentCultureIgnoreCase) như code cũ
            throw new NotImplementedException();
        }
    }
}
