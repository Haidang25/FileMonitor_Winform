using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

        /// <summary>Giá trị gốc của Keyword, đúng như người dùng gõ.</summary>
        private string keyword = string.Empty;

        /// <summary>
        /// Các từ của Keyword đã chuẩn hóa sẵn (bỏ dấu, chữ thường).
        /// Tính một lần khi gán Keyword, không tính lại cho từng bản ghi.
        /// </summary>
        private string[] searchTerms = new string[0];

        /// <summary>
        /// Từ khóa tìm trong tên tệp hoặc đường dẫn. Rỗng nghĩa là không lọc theo từ khóa.
        /// </summary>
        /// <remarks>
        /// Gán null được hiểu là chuỗi rỗng. Mỗi lần gán, các từ tìm kiếm được chuẩn hóa
        /// lại ngay: MainForm gán Keyword một lần mỗi khi người dùng gõ phím, còn
        /// MatchesKeyword chạy cho TỪNG bản ghi — chuẩn hóa ở đây thì chỉ làm một lần.
        /// </remarks>
        public string Keyword
        {
            get { return keyword; }
            set
            {
                keyword = value ?? string.Empty;
                searchTerms = SplitTerms(keyword);
            }
        }

        /// <summary>Tạo bộ lọc mặc định: 7 ngày gần nhất, mọi loại, không từ khóa.</summary>
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
        /// Mốc đầu của khoảng lọc: 00:00:00 của ngày sớm hơn.
        /// </summary>
        /// <remarks>
        /// Người dùng có thể chọn "Từ ngày" sau "Đến ngày" (MainForm tự chỉnh lại, nhưng
        /// LogFilter không được phép dựa vào điều đó). Khi hai ngày bị ngược thì đổi chỗ,
        /// giống cách ReadRange của LogService đang làm, để đọc tệp và lọc bản ghi
        /// không bao giờ hiểu khoảng ngày theo hai cách khác nhau.
        /// </remarks>
        public DateTime RangeStart
        {
            get
            {
                DateTime first = FromDate <= ToDate ? FromDate : ToDate;
                return first.Date;
            }
        }

        /// <summary>
        /// Mốc cuối của khoảng lọc: 23:59:59.9999999 của ngày muộn hơn.
        /// </summary>
        /// <remarks>
        /// Lấy tới HẾT ngày chứ không dừng ở 00:00:00: DateTimePicker trả về ngày kèm giờ
        /// hiện tại hoặc 00:00:00 tùy lúc, nếu so thẳng thì chọn "đến hôm nay" sẽ bỏ sót
        /// các sự kiện xảy ra sau giờ đó của chính hôm nay.
        ///
        /// Ngày cuối cùng mà DateTime biểu diễn được (31/12/9999) phải xử lý riêng:
        /// cộng thêm một ngày sẽ vượt giới hạn và ném ArgumentOutOfRangeException.
        /// </remarks>
        public DateTime RangeEnd
        {
            get
            {
                DateTime last = FromDate <= ToDate ? ToDate : FromDate;

                if (last.Date == DateTime.MaxValue.Date)
                {
                    return DateTime.MaxValue;
                }

                return last.Date.AddDays(1).AddTicks(-1);
            }
        }

        /// <summary>
        /// Bản ghi có nằm trong khoảng ngày đang lọc hay không (tính cả hai ngày đầu mút).
        /// </summary>
        /// <remarks>
        /// So sánh với khoảng đóng [RangeStart, RangeEnd], chính xác tới từng tick (0,1 micro giây),
        /// nên sự kiện lúc 23:59:59.999 của ngày cuối vẫn được tính, còn 00:00:00 của ngày
        /// hôm sau thì không.
        /// </remarks>
        public bool MatchesDate(FileEventLog entry)
        {
            if (entry == null)
            {
                return false;
            }

            return entry.Time >= RangeStart && entry.Time <= RangeEnd;
        }

        /// <summary>
        /// Bản ghi có chứa từ khóa trong tên tệp hoặc đường dẫn hay không.
        /// Từ khóa rỗng thì khớp tất cả.
        /// </summary>
        /// <remarks>
        /// Quy tắc tìm (cố ý làm "dễ tính" vì người dùng thường gõ nhanh, không dấu):
        /// 1. Không phân biệt hoa/thường.
        /// 2. Không phân biệt dấu tiếng Việt: gõ "bao cao" tìm ra "Báo cáo",
        ///    gõ "dang" tìm ra "Đăng".
        /// 3. Nhiều từ cách nhau bởi khoảng trắng thì phải có ĐỦ các từ (phép AND),
        ///    không cần liền nhau: "bao docx" tìm ra "D:\Báo cáo\quy3.docx".
        /// 4. "/" được coi như "\": gõ "baocao/quy3" vẫn khớp đường dẫn Windows.
        /// 5. Tìm trong tên tệp, đường dẫn, và cả ĐƯỜNG DẪN CŨ của sự kiện Renamed,
        ///    để tìm theo tên cũ vẫn ra được tệp đã bị đổi tên.
        /// </remarks>
        public bool MatchesKeyword(FileEventLog entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (searchTerms.Length == 0)
            {
                return true;
            }

            // Gộp các trường vào một chuỗi để mỗi từ chỉ phải tìm một lần.
            // Ký tự '\n' ngăn cách để một từ không khớp "vắt" qua ranh giới hai trường.
            string haystack = NormalizeForSearch(
                entry.FileName + "\n" + entry.FullPath + "\n" + entry.OldFullPath);

            foreach (string term in searchTerms)
            {
                // Ordinal: cả hai phía đã được chuẩn hóa về chữ thường không dấu,
                // nên so từng ký tự là đủ và nhanh nhất.
                if (haystack.IndexOf(term, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tách từ khóa thành các từ đã chuẩn hóa, bỏ khoảng trắng thừa.
        /// </summary>
        private static string[] SplitTerms(string text)
        {
            List<string> terms = new List<string>();

            foreach (string part in text.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string term = NormalizeForSearch(part);
                if (term.Length > 0)
                {
                    terms.Add(term);
                }
            }

            return terms.ToArray();
        }

        /// <summary>
        /// Chuẩn hóa chuỗi để so sánh: chữ thường, bỏ dấu tiếng Việt, "/" thành "\".
        /// </summary>
        /// <remarks>
        /// Cách bỏ dấu: Normalize(FormD) tách mỗi chữ có dấu thành chữ gốc + các dấu rời
        /// ("ế" → "e" + dấu mũ + dấu sắc), rồi bỏ các ký tự thuộc loại NonSpacingMark.
        ///
        /// Riêng "đ" phải đổi tay: đó là một chữ cái riêng trong Unicode chứ không phải
        /// "d" + dấu, nên FormD không tách được.
        ///
        /// Không dùng CompareOptions.IgnoreNonSpace của .NET: kết quả phụ thuộc thư viện
        /// ngôn ngữ của hệ điều hành, và vẫn không xử lý được "đ".
        ///
        /// ToLowerInvariant chứ không ToLower: tránh lỗi "chữ I của tiếng Thổ Nhĩ Kỳ",
        /// khi máy đặt vùng Thổ Nhĩ Kỳ thì "I".ToLower() ra "ı" chứ không ra "i".
        /// </remarks>
        internal static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string decomposed = text.Normalize(NormalizationForm.FormD);
            StringBuilder builder = new StringBuilder(decomposed.Length);

            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                switch (c)
                {
                    case 'đ':
                    case 'Đ':
                        builder.Append('d');
                        break;
                    case '/':
                        builder.Append('\\');
                        break;
                    default:
                        builder.Append(char.ToLowerInvariant(c));
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
