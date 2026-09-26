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
            // TODO (bước 1): đặt giá trị mặc định
            //   - FromDate = 7 ngày trước, ToDate = hôm nay (khớp với InitDateFilter của MainForm)
            //   - EventType = null
            //   - Keyword = chuỗi rỗng (không để null, đỡ phải kiểm tra null ở khắp nơi)
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
            // TODO (bước 2): trả về false nếu entry == null
            // TODO (bước 3): kiểm tra khoảng ngày
            //   - from = FromDate.Date
            //   - to   = ToDate.Date.AddDays(1).AddTicks(-1)   // hết ngày, xem FilterByDate cũ
            // TODO (bước 4): nếu EventType có giá trị thì so với entry.EventType
            // TODO (bước 5): nếu Keyword khác rỗng thì tìm trong FileName hoặc FullPath,
            //   dùng IndexOf(..., StringComparison.CurrentCultureIgnoreCase) như code cũ
            throw new NotImplementedException();
        }
    }
}
