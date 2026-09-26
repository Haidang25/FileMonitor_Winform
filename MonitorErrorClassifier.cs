using System;
using System.ComponentModel;
using System.IO;

namespace FileMonitorApps
{
    /// <summary>
    /// Các nhóm sự cố của việc giám sát, phân theo cách xử lý.
    /// </summary>
    internal enum MonitorErrorKind
    {
        /// <summary>Không có sự cố.</summary>
        None,

        /// <summary>Tràn bộ đệm: mất một số sự kiện nhưng vẫn giám sát tiếp được.</summary>
        BufferOverflow,

        /// <summary>Thư mục đang giám sát không còn tồn tại.</summary>
        FolderLost,

        /// <summary>Không đủ quyền truy cập thư mục.</summary>
        AccessDenied,

        /// <summary>Sự cố khác.</summary>
        Other
    }

    /// <summary>
    /// Phân loại ngoại lệ của việc giám sát và viết lời giải thích cho người dùng.
    /// </summary>
    /// <remarks>
    /// Tách khỏi MainForm: nhận ra "thư mục đã mất" hay "thiếu quyền" từ một ngoại lệ là
    /// hiểu biết về FileSystemWatcher và hệ điều hành, không phải việc của giao diện.
    /// Lớp tĩnh, không phụ thuộc WinForms, kiểm thử được độc lập.
    /// </remarks>
    internal static class MonitorErrorClassifier
    {
        /// <summary>ERROR_ACCESS_DENIED của Windows.</summary>
        private const int ErrorAccessDenied = 5;

        /// <summary>
        /// Phân loại một sự cố xảy ra TRONG LÚC đang giám sát.
        /// </summary>
        /// <remarks>
        /// Xét "mất thư mục" TRƯỚC "thiếu quyền": khi thư mục bị xóa, Windows cũng báo mã 5
        /// (Access is denied), xét ngược thứ tự sẽ báo nhầm là thiếu quyền.
        /// Khi quyền bị thu hồi, sự kiện Error mang Win32Exception mã 5 chứ không phải
        /// UnauthorizedAccessException, nên phải nhận cả hai dạng.
        /// </remarks>
        public static MonitorErrorKind Classify(Exception error, string folderPath)
        {
            if (error is InternalBufferOverflowException)
            {
                return MonitorErrorKind.BufferOverflow;
            }

            if (error is DirectoryNotFoundException || FolderMissing(folderPath))
            {
                return MonitorErrorKind.FolderLost;
            }

            if (IsAccessDenied(error))
            {
                return MonitorErrorKind.AccessDenied;
            }

            return MonitorErrorKind.Other;
        }

        /// <summary>
        /// Lời giải thích đầy đủ cho hộp thoại khi việc giám sát bị dừng giữa chừng.
        /// </summary>
        public static string DescribeFault(MonitorErrorKind kind)
        {
            switch (kind)
            {
                case MonitorErrorKind.FolderLost:
                    return "Thư mục đang giám sát không còn tồn tại: nó đã bị xóa, đổi tên, di chuyển " +
                        "(kể cả bỏ vào Thùng rác), hoặc ổ USB / ổ mạng chứa nó đã bị ngắt." +
                        Environment.NewLine + Environment.NewLine +
                        "Nếu thư mục được khôi phục, hãy bấm \"Bắt đầu giám sát\" lại.";

                case MonitorErrorKind.AccessDenied:
                    return "Tài khoản hiện tại không còn quyền truy cập thư mục đang giám sát " +
                        "(quyền vừa bị thay đổi, hoặc thư mục bị khóa bởi phần mềm bảo mật).";

                default:
                    return "Nguyên nhân thường gặp: thư mục đang theo dõi bị xóa, bị đổi tên, " +
                        "hoặc nằm trên ổ đĩa mạng đã ngắt kết nối.";
            }
        }

        /// <summary>
        /// Lý do ngắn gọn, dùng cho nhãn trạng thái sau khi giám sát bị dừng do sự cố.
        /// </summary>
        public static string ShortReason(MonitorErrorKind kind)
        {
            switch (kind)
            {
                case MonitorErrorKind.FolderLost:
                    return "thư mục giám sát không còn tồn tại";
                case MonitorErrorKind.AccessDenied:
                    return "mất quyền truy cập thư mục";
                default:
                    return "gặp sự cố";
            }
        }

        /// <summary>
        /// Giải thích nguyên nhân khi KHÔNG BẮT ĐẦU được việc giám sát.
        /// </summary>
        /// <remarks>
        /// FolderValidator đã thử đọc thư mục trước đó, nên tới được đây thường là do quyền
        /// thay đổi ngay giữa lúc kiểm tra và lúc bắt đầu, hoặc thư mục đọc được nhưng Windows
        /// không cho THEO DÕI (ReadDirectoryChangesW cần quyền riêng).
        ///
        /// Khi thiếu quyền, FileSystemWatcher của .NET Framework KHÔNG ném
        /// UnauthorizedAccessException mà ném FileNotFoundException với câu "Error reading
        /// the directory", nên phải xét cả trường hợp thư mục vẫn tồn tại.
        /// DirectoryNotFoundException/FileNotFoundException là lớp con của IOException,
        /// nên được xét trước.
        /// </remarks>
        public static string DescribeStartError(Exception error, string folderPath)
        {
            if (error is UnauthorizedAccessException)
            {
                return "Tài khoản hiện tại không đủ quyền theo dõi thư mục này. " +
                    "Hãy chọn thư mục khác, hoặc chạy chương trình bằng quyền Administrator.";
            }

            if (error is DirectoryNotFoundException)
            {
                return "Thư mục vừa bị xóa, đổi tên hoặc di chuyển.";
            }

            if (error is IOException)
            {
                if (!FolderMissing(folderPath))
                {
                    return "Windows không cho phép theo dõi thư mục này. Nguyên nhân thường gặp: " +
                        "tài khoản không đủ quyền, hoặc thư mục nằm trên ổ không hỗ trợ theo dõi thay đổi.";
                }

                return "Thư mục vừa bị xóa, đổi tên hoặc di chuyển.";
            }

            return "Đã xảy ra lỗi ngoài dự kiến.";
        }

        /// <summary>Lỗi thiếu quyền, ở cả dạng .NET (UnauthorizedAccessException) lẫn dạng Win32 (mã 5).</summary>
        private static bool IsAccessDenied(Exception error)
        {
            Win32Exception win32 = error as Win32Exception;
            return error is UnauthorizedAccessException
                || (win32 != null && win32.NativeErrorCode == ErrorAccessDenied);
        }

        /// <summary>Thư mục đã cho không còn tồn tại. Đường dẫn rỗng thì coi như không biết (false).</summary>
        private static bool FolderMissing(string folderPath)
        {
            string folder = (folderPath ?? string.Empty).Trim();
            return folder.Length > 0 && !Directory.Exists(folder);
        }
    }
}
