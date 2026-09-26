using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace FileMonitorApps
{
    /// <summary>
    /// Kết quả kiểm tra một đường dẫn thư mục trước khi giám sát.
    /// </summary>
    internal class FolderValidationResult
    {
        /// <summary>Thư mục có dùng được để giám sát hay không.</summary>
        public bool IsValid { get; private set; }

        /// <summary>Đường dẫn tuyệt đối đã chuẩn hóa. Chỉ có giá trị khi IsValid = true.</summary>
        public string NormalizedPath { get; private set; }

        /// <summary>Lý do không hợp lệ, viết sẵn để hiện cho người dùng. Rỗng nếu hợp lệ.</summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Cảnh báo không chặn (ví dụ thư mục nằm trên ổ mạng). Rỗng nếu không có gì.
        /// Giao diện nên hỏi người dùng có muốn tiếp tục hay không.
        /// </summary>
        public string Warning { get; private set; }

        private FolderValidationResult()
        {
            NormalizedPath = string.Empty;
            ErrorMessage = string.Empty;
            Warning = string.Empty;
        }

        public static FolderValidationResult Valid(string normalizedPath, string warning)
        {
            FolderValidationResult result = new FolderValidationResult();
            result.IsValid = true;
            result.NormalizedPath = normalizedPath;
            result.Warning = warning ?? string.Empty;
            return result;
        }

        public static FolderValidationResult Invalid(string errorMessage)
        {
            FolderValidationResult result = new FolderValidationResult();
            result.IsValid = false;
            result.ErrorMessage = errorMessage;
            return result;
        }
    }

    /// <summary>
    /// Kiểm tra đường dẫn thư mục người dùng chọn hoặc gõ vào, trước khi bắt đầu giám sát.
    /// </summary>
    /// <remarks>
    /// Tách khỏi MainForm để phần kiểm tra không phụ thuộc giao diện và kiểm thử được
    /// độc lập (checklist G: MainForm không chứa logic nghiệp vụ).
    ///
    /// Thứ tự kiểm tra đi từ rẻ tới đắt, từ lỗi gõ phím tới lỗi hệ thống:
    ///   1. Rỗng                      → chưa nhập gì
    ///   2. Ký tự cấm                 → gõ sai
    ///   3. Không phải đường dẫn đầy đủ → "abc", "\abc", "D:abc"
    ///   4. Chuẩn hóa (GetFullPath)   → quá dài, định dạng lạ
    ///   5. Ổ đĩa                     → ổ không có, ổ USB/CD chưa cắm
    ///   6. Là tệp chứ không phải thư mục
    ///   7. Thư mục không tồn tại     → gợi ý thư mục cha gần nhất còn tồn tại
    ///   8. Không đọc được            → thiếu quyền
    ///   9. Trùng thư mục nhật ký     → vòng lặp ghi log vô hạn
    ///  10. Ổ mạng                    → chỉ cảnh báo, không chặn
    ///  11. Gốc ổ đĩa + thư mục con   → chỉ cảnh báo, không chặn
    /// Mỗi bước trả về một thông báo riêng, để người dùng biết chính xác phải sửa gì
    /// thay vì một câu "đường dẫn không hợp lệ" chung chung.
    /// </remarks>
    internal static class FolderValidator
    {
        /// <summary>
        /// Ký tự Windows cấm trong tên tệp và thư mục (ngoài dấu gạch chéo và dấu hai chấm,
        /// được xét riêng). Liệt kê tường minh thay vì dùng Path.GetInvalidPathChars(),
        /// vì hàm đó trên .NET Framework 4.6.2 trở lên KHÔNG còn chứa '?' và '*'.
        /// </summary>
        private static readonly char[] ForbiddenChars = { '<', '>', '"', '|', '?', '*' };

        /// <summary>
        /// Kiểm tra một đường dẫn thư mục.
        /// </summary>
        /// <param name="rawPath">Đường dẫn người dùng nhập hoặc chọn.</param>
        /// <param name="logFolder">Thư mục chứa nhật ký của chương trình (LogService.LogFolder).</param>
        /// <param name="filter">Mẫu lọc phần mở rộng sẽ dùng, ví dụ "*.*".</param>
        /// <param name="includeSubdirectories">Có theo dõi cả thư mục con hay không.</param>
        public static FolderValidationResult Validate(string rawPath, string logFolder,
            string filter, bool includeSubdirectories)
        {
            // 1. Rỗng
            string path = CleanInput(rawPath);
            if (path.Length == 0)
            {
                return FolderValidationResult.Invalid("Vui lòng chọn hoặc nhập thư mục cần giám sát.");
            }

            // 2. Ký tự cấm
            char badChar;
            if (TryFindForbiddenChar(path, out badChar))
            {
                return FolderValidationResult.Invalid(
                    "Đường dẫn chứa ký tự không hợp lệ: " + DescribeChar(badChar) + Environment.NewLine +
                    path + Environment.NewLine + Environment.NewLine +
                    "Tên thư mục trên Windows không được chứa các ký tự  \\ / : * ? \" < > |");
            }

            // 3. Phải là đường dẫn đầy đủ
            if (!IsFullyQualified(path))
            {
                return FolderValidationResult.Invalid(
                    "Hãy nhập đường dẫn đầy đủ, bắt đầu bằng tên ổ đĩa, ví dụ D:\\MonitorTest" +
                    Environment.NewLine + Environment.NewLine + "Đường dẫn vừa nhập: " + path +
                    Environment.NewLine + Environment.NewLine +
                    "Đường dẫn tương đối sẽ bị hiểu theo thư mục đang chạy chương trình, " +
                    "dễ dẫn tới giám sát nhầm chỗ.");
            }

            // 4. Chuẩn hóa: gộp dấu gạch chéo thừa, xử lý "..", đổi "/" thành "\"
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (PathTooLongException)
            {
                return FolderValidationResult.Invalid("Đường dẫn quá dài so với giới hạn của hệ điều hành.");
            }
            catch (ArgumentException)
            {
                return FolderValidationResult.Invalid("Đường dẫn không hợp lệ:" + Environment.NewLine + path);
            }
            catch (NotSupportedException)
            {
                return FolderValidationResult.Invalid(
                    "Định dạng đường dẫn không được hỗ trợ:" + Environment.NewLine + path);
            }
            catch (SecurityException)
            {
                return FolderValidationResult.Invalid(
                    "Không đủ quyền để xử lý đường dẫn này:" + Environment.NewLine + path);
            }

            // Bỏ dấu "\" cuối (trừ khi là gốc ổ đĩa "D:\") để hiển thị và so sánh thống nhất.
            path = TrimTrailingSeparator(path);

            // 5. Ổ đĩa
            string driveError = CheckDrive(path);
            if (driveError.Length > 0)
            {
                return FolderValidationResult.Invalid(driveError);
            }

            // 6. Là tệp, không phải thư mục
            if (File.Exists(path))
            {
                return FolderValidationResult.Invalid(
                    "Đường dẫn trỏ tới một TỆP, không phải thư mục:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine +
                    "Chương trình giám sát cả một thư mục. Hãy chọn thư mục chứa tệp này:" +
                    Environment.NewLine + Path.GetDirectoryName(path));
            }

            // 7. Không tồn tại
            if (!Directory.Exists(path))
            {
                string message = "Thư mục không tồn tại:" + Environment.NewLine + path;
                string nearest = FindNearestExistingParent(path);
                if (nearest.Length > 0)
                {
                    message += Environment.NewLine + Environment.NewLine +
                        "Thư mục gần nhất còn tồn tại:" + Environment.NewLine + nearest;
                }

                return FolderValidationResult.Invalid(message);
            }

            // 8. Đọc được hay không. Thư mục tồn tại chưa chắc đã đọc được; thử liệt kê một
            // phần tử để phát hiện lỗi phân quyền ngay, thay vì để FileSystemWatcher báo lỗi
            // khó hiểu về sau.
            try
            {
                using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
                {
                    entries.MoveNext();
                }
            }
            catch (UnauthorizedAccessException)
            {
                return FolderValidationResult.Invalid(
                    "Tài khoản hiện tại không có quyền đọc thư mục:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine +
                    "Hãy chọn thư mục khác, hoặc chạy chương trình với quyền Administrator.");
            }
            catch (IOException ex)
            {
                return FolderValidationResult.Invalid(
                    "Không đọc được thư mục:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine + "Chi tiết: " + ex.Message);
            }

            // 9. Vòng lặp nhật ký
            string loopError = CheckLogFolderLoop(path, logFolder, filter, includeSubdirectories);
            if (loopError.Length > 0)
            {
                return FolderValidationResult.Invalid(loopError);
            }

            // 10–11. Ổ mạng, phạm vi quá rộng: không chặn, chỉ cảnh báo
            return FolderValidationResult.Valid(path,
                JoinWarnings(GetNetworkWarning(path), GetScopeWarning(path, includeSubdirectories)));
        }

        #region Các bước kiểm tra (hàm thuần, không đụng đĩa — kiểm thử được độc lập)

        /// <summary>
        /// Bỏ khoảng trắng và dấu nháy kép bao ngoài. Dùng "Copy as path" trong File Explorer
        /// sẽ được chuỗi dạng "D:\MonitorTest" kèm cả dấu nháy.
        /// </summary>
        internal static string CleanInput(string rawPath)
        {
            return (rawPath ?? string.Empty).Trim().Trim('"').Trim();
        }

        /// <summary>
        /// Tìm ký tự cấm đầu tiên trong đường dẫn.
        /// </summary>
        /// <remarks>
        /// Dấu hai chấm chỉ được phép ở vị trí thứ hai, ngay sau tên ổ đĩa ("D:").
        /// Ở chỗ khác ("D:\a:b") nó là cú pháp alternate data stream của NTFS, không phải
        /// tên thư mục. Ký tự điều khiển (mã 0–31, ví dụ TAB dán nhầm vào) cũng bị cấm.
        /// </remarks>
        internal static bool TryFindForbiddenChar(string path, out char found)
        {
            found = '\0';

            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];

                bool forbidden = c < 32
                    || Array.IndexOf(ForbiddenChars, c) >= 0
                    || (c == ':' && i != 1);

                if (forbidden)
                {
                    found = c;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Đường dẫn có đầy đủ hay không: "D:\..." hoặc đường dẫn mạng "\\máy\thư mục\...".
        /// </summary>
        /// <remarks>
        /// Viết tay theo quy tắc của Windows thay vì dùng Path.IsPathRooted, vì IsPathRooted
        /// coi cả hai dạng sau là "có gốc" dù chúng vẫn phụ thuộc thư mục hiện hành:
        /// - "\MonitorTest" : gốc của Ổ ĐĨA HIỆN HÀNH, không biết là ổ nào.
        /// - "D:MonitorTest": thư mục hiện hành CỦA ổ D, không phải D:\MonitorTest.
        /// </remarks>
        internal static bool IsFullyQualified(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // Đường dẫn mạng: \\máy\thư mục chia sẻ (cả hai phần đều phải có)
            if (path.Length > 2 && IsSeparator(path[0]) && IsSeparator(path[1]))
            {
                string rest = path.Substring(2);
                int sep = rest.IndexOfAny(new char[] { '\\', '/' });
                return sep > 0 && sep < rest.Length - 1;
            }

            // Ổ đĩa: chữ cái + ":" + dấu gạch chéo
            return path.Length >= 3
                && IsDriveLetter(path[0])
                && path[1] == ':'
                && IsSeparator(path[2]);
        }

        /// <summary>
        /// Đường dẫn mạng dạng \\máy\thư mục.
        /// </summary>
        internal static bool IsUncPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.Length > 2
                && IsSeparator(path[0]) && IsSeparator(path[1]);
        }

        /// <summary>
        /// Bỏ dấu gạch chéo cuối, trừ khi đường dẫn là gốc ổ đĩa ("D:\" giữ nguyên).
        /// </summary>
        internal static string TrimTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string trimmed = path.TrimEnd('\\', '/');

            // "D:" không phải "D:\": gốc ổ đĩa phải giữ lại dấu gạch chéo.
            if (trimmed.Length == 2 && trimmed[1] == ':')
            {
                return trimmed + "\\";
            }

            return trimmed.Length == 0 ? path : trimmed;
        }

        /// <summary>
        /// child có phải chính là parent, hoặc nằm bên trong parent hay không.
        /// So sánh không phân biệt hoa/thường như hệ thống tệp của Windows.
        /// </summary>
        /// <remarks>
        /// Thêm dấu "\" vào cuối trước khi so, nếu không "D:\Logs2" sẽ bị coi là
        /// nằm trong "D:\Logs" chỉ vì có cùng phần đầu.
        /// </remarks>
        internal static bool IsSameOrInside(string child, string parent)
        {
            if (string.IsNullOrEmpty(child) || string.IsNullOrEmpty(parent))
            {
                return false;
            }

            string c = NormalizeForCompare(child);
            string p = NormalizeForCompare(parent);

            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Mẫu lọc có thể khớp với tệp nhật ký (đuôi .log) hay không.
        /// </summary>
        internal static bool FilterMayMatchLogFiles(string filter)
        {
            if (string.IsNullOrEmpty(filter) || filter == "*" || filter == "*.*")
            {
                return true;
            }

            // Mẫu dạng "*.đuôi": chỉ khớp tệp nhật ký khi đuôi đúng là .log
            if (filter.StartsWith("*.", StringComparison.Ordinal))
            {
                return string.Equals(filter.Substring(1), LogService.FileExtension,
                    StringComparison.OrdinalIgnoreCase);
            }

            // Mẫu khác (hiếm gặp): coi như có thể khớp, chặn nhầm còn hơn để lọt vòng lặp.
            return true;
        }

        /// <summary>
        /// Kiểm tra một đường dẫn có phải thư mục gốc của ổ đĩa hay không (ví dụ C:\).
        /// </summary>
        internal static bool IsDriveRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string trimmed = path.TrimEnd('\\', '/');
            return trimmed.Length == 2 && IsDriveLetter(trimmed[0]) && trimmed[1] == ':';
        }

        #endregion

        #region Các bước kiểm tra có đụng tới hệ thống

        /// <summary>
        /// Kiểm tra ổ đĩa của đường dẫn. Trả về thông báo lỗi, hoặc chuỗi rỗng nếu ổn.
        /// </summary>
        /// <remarks>
        /// Tách riêng khỏi bước "thư mục không tồn tại" vì cách sửa khác hẳn nhau:
        /// gõ nhầm tên ổ (Z:) thì phải sửa đường dẫn, còn ổ USB/CD chưa cắm thì chỉ cần
        /// cắm vào. Gộp chung một câu "không tồn tại" thì người dùng không biết đường nào.
        /// </remarks>
        private static string CheckDrive(string path)
        {
            if (IsUncPath(path))
            {
                return string.Empty;
            }

            string root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return string.Empty;
            }

            try
            {
                DriveInfo drive = new DriveInfo(root);

                if (drive.DriveType == DriveType.NoRootDirectory)
                {
                    return "Ổ đĩa " + root + " không tồn tại trên máy này.";
                }

                if (!drive.IsReady)
                {
                    return "Ổ đĩa " + root + " chưa sẵn sàng." + Environment.NewLine +
                        Environment.NewLine + "Nếu là ổ USB hoặc ổ CD/DVD, hãy cắm thiết bị vào rồi thử lại.";
                }
            }
            catch (ArgumentException)
            {
                return "Ổ đĩa " + root + " không hợp lệ.";
            }

            return string.Empty;
        }

        /// <summary>
        /// Chặn trường hợp thư mục giám sát chứa chính thư mục nhật ký.
        /// </summary>
        /// <remarks>
        /// Nếu để lọt, mỗi lần chương trình ghi một dòng nhật ký, FileSystemWatcher lại thấy
        /// tệp nhật ký thay đổi, phát sự kiện Changed, sự kiện đó lại được ghi thành một dòng
        /// nhật ký mới... Vòng lặp không bao giờ dừng: bảng đầy dòng rác và tệp nhật ký phình
        /// ra liên tục. Bộ chống trùng chỉ làm chậm vòng lặp (500 ms/lần), không chặn được.
        ///
        /// Chỉ chặn khi thật sự có vòng lặp: thư mục nhật ký nằm trong phạm vi theo dõi
        /// VÀ mẫu lọc khớp được với tệp .log.
        /// </remarks>
        private static string CheckLogFolderLoop(string path, string logFolder, string filter,
            bool includeSubdirectories)
        {
            if (string.IsNullOrEmpty(logFolder) || !FilterMayMatchLogFiles(filter))
            {
                return string.Empty;
            }

            string logs;
            try
            {
                logs = Path.GetFullPath(logFolder);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            bool watchesLogFolder = IsSameOrInside(path, logs)
                || (includeSubdirectories && IsSameOrInside(logs, path));

            if (!watchesLogFolder)
            {
                return string.Empty;
            }

            return "Không thể giám sát thư mục này vì nó chứa thư mục nhật ký của chính chương trình:" +
                Environment.NewLine + logs + Environment.NewLine + Environment.NewLine +
                "Mỗi dòng nhật ký được ghi sẽ lại bị phát hiện là một thay đổi mới, tạo thành vòng lặp " +
                "không dừng." + Environment.NewLine + Environment.NewLine +
                "Cách khắc phục: chọn thư mục khác, bỏ chọn \"Bao gồm thư mục con\", " +
                "hoặc lọc theo một phần mở rộng khác .log.";
        }

        /// <summary>
        /// Cảnh báo nếu thư mục nằm trên ổ mạng. Trả về chuỗi rỗng nếu là ổ cục bộ.
        /// </summary>
        /// <remarks>
        /// Ổ mạng nằm ngoài phạm vi đã cam kết của đồ án: FileSystemWatcher trên ổ mạng có thể
        /// bỏ sót sự kiện, và mất kết nối mạng sẽ làm dừng giám sát. Vẫn cho phép dùng
        /// vì nhiều trường hợp vẫn chạy được, nhưng người dùng cần biết trước.
        /// </remarks>
        private static string GetNetworkWarning(string path)
        {
            bool isNetwork = IsUncPath(path);

            if (!isNetwork)
            {
                try
                {
                    string root = Path.GetPathRoot(path);
                    isNetwork = !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
                }
                catch (Exception)
                {
                    isNetwork = false;
                }
            }

            if (!isNetwork)
            {
                return string.Empty;
            }

            return "Thư mục này nằm trên ổ mạng:" + Environment.NewLine + path + Environment.NewLine +
                Environment.NewLine +
                "Chương trình được thiết kế cho ổ đĩa cục bộ. Trên ổ mạng, một số thay đổi có thể " +
                "không được phát hiện, và khi mất kết nối thì việc giám sát sẽ dừng.";
        }

        /// <summary>
        /// Cảnh báo khi theo dõi cả một ổ đĩa kèm thư mục con. Trả về chuỗi rỗng nếu không sao.
        /// </summary>
        /// <remarks>
        /// Chuyển từ MainForm sang (trước là ConfirmHighVolumeScope): đánh giá phạm vi nào là
        /// "quá rộng" là quy tắc nghiệp vụ, giao diện chỉ việc hỏi người dùng.
        /// </remarks>
        private static string GetScopeWarning(string path, bool includeSubdirectories)
        {
            if (!includeSubdirectories || !IsDriveRoot(path))
            {
                return string.Empty;
            }

            return "Bạn đang chọn thư mục gốc của ổ đĩa kèm toàn bộ thư mục con:" +
                Environment.NewLine + path + Environment.NewLine + Environment.NewLine +
                "Phạm vi này sinh ra rất nhiều sự kiện (tệp tạm của hệ điều hành, bộ nhớ đệm " +
                "của trình duyệt, tiến trình đồng bộ ngầm...) và dễ làm tràn bộ đệm, " +
                "khiến một số thay đổi bị bỏ sót.";
        }

        /// <summary>
        /// Gộp các cảnh báo khác rỗng thành một, thêm câu hỏi "Vẫn tiếp tục?" ở cuối.
        /// </summary>
        internal static string JoinWarnings(params string[] warnings)
        {
            List<string> parts = new List<string>();
            foreach (string warning in warnings)
            {
                if (!string.IsNullOrEmpty(warning))
                {
                    parts.Add(warning);
                }
            }

            if (parts.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine + Environment.NewLine, parts.ToArray()) +
                Environment.NewLine + Environment.NewLine + "Vẫn tiếp tục?";
        }

        /// <summary>
        /// Đi ngược lên các thư mục cha để tìm thư mục gần nhất còn tồn tại.
        /// Giúp người dùng nhận ra chỗ gõ sai, ví dụ "D:\MonitorTets\con" → "D:\" còn tồn tại.
        /// </summary>
        private static string FindNearestExistingParent(string path)
        {
            try
            {
                string current = Path.GetDirectoryName(path);

                while (!string.IsNullOrEmpty(current))
                {
                    if (Directory.Exists(current))
                    {
                        return current;
                    }

                    current = Path.GetDirectoryName(current);
                }
            }
            catch (Exception)
            {
                // Chỉ là gợi ý thêm; không tìm được thì thôi.
            }

            return string.Empty;
        }

        #endregion

        #region Tiện ích

        private static bool IsSeparator(char c)
        {
            return c == '\\' || c == '/';
        }

        private static bool IsDriveLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        private static string NormalizeForCompare(string path)
        {
            return path.Replace('/', '\\').TrimEnd('\\') + "\\";
        }

        /// <summary>
        /// Mô tả ký tự để hiện trong thông báo; ký tự điều khiển không in ra được
        /// nên ghi bằng tên.
        /// </summary>
        private static string DescribeChar(char c)
        {
            if (c == '\t')
            {
                return "dấu TAB";
            }

            if (c < 32)
            {
                return "ký tự điều khiển (mã " + ((int)c).ToString() + ")";
            }

            return "'" + c + "'";
        }

        #endregion
    }
}
