using System;
using System.IO;
using System.Security;

namespace FileMonitorApps
{
    /// <summary>
    /// Đọc kích thước của tệp vừa thay đổi, không bao giờ ném ngoại lệ ra ngoài.
    /// </summary>
    /// <remarks>
    /// Điểm kỹ thuật ④ của đồ án: tệp vừa được tạo hoặc sửa có thể đang bị chương trình
    /// khác giữ, hoặc đã biến mất ngay sau khi sự kiện được phát (tệp tạm của Word, trình
    /// duyệt...). Đọc thông tin tệp lúc đó có thể ném IOException. Hàm này chạy trên LUỒNG NỀN
    /// của FileSystemWatcher, một ngoại lệ lọt ra ngoài sẽ làm sập cả chương trình, nên mọi
    /// lỗi đều được bắt lại: kích thước để null (hiển thị "N/A") và ghi lý do vào Note.
    ///
    /// Lưu ý khi viết báo cáo: FileInfo.Length chỉ đọc THÔNG TIN (metadata) của tệp trong thư
    /// mục chứ không mở nội dung tệp, nên phần lớn trường hợp tệp đang bị khóa vẫn đọc được
    /// kích thước bình thường. Lỗi hay gặp nhất trên thực tế là tệp đã bị xóa trước khi kịp đọc
    /// (FileNotFoundException — cũng là một loại IOException).
    /// </remarks>
    internal static class FileSizeProbe
    {
        /// <summary>Ghi chú khi đối tượng thay đổi là thư mục (thư mục không có kích thước).</summary>
        public const string NoteDirectory = "Thư mục";

        /// <summary>Ghi chú khi tệp biến mất trước khi kịp đọc.</summary>
        public const string NoteGone = "Tệp đã bị xóa hoặc di chuyển trước khi đọc được kích thước";

        /// <summary>Ghi chú khi tệp đang bị tiến trình khác giữ.</summary>
        public const string NoteLocked = "Tệp đang bị tiến trình khác khóa, không đọc được kích thước";

        /// <summary>Ghi chú khi không có quyền đọc thông tin tệp.</summary>
        public const string NoteDenied = "Không có quyền đọc thông tin tệp";

        /// <summary>Ghi chú khi đường dẫn không đọc được vì lý do khác.</summary>
        public const string NoteInvalid = "Đường dẫn không đọc được thông tin";

        /// <summary>
        /// Điền FileSize (và Note nếu có vấn đề) cho một bản ghi.
        /// Bỏ qua sự kiện Deleted: tệp không còn nữa thì không có gì để đọc.
        /// </summary>
        public static void Fill(FileEventLog entry)
        {
            if (entry == null || entry.EventType == FileEventType.Deleted
                || string.IsNullOrEmpty(entry.FullPath))
            {
                return;
            }

            long? size;
            string note;
            TryGetSize(entry.FullPath, out size, out note);

            entry.FileSize = size;
            entry.Note = note;
        }

        /// <summary>
        /// Đọc kích thước một tệp.
        /// </summary>
        /// <param name="path">Đường dẫn tệp.</param>
        /// <param name="size">Kích thước tính bằng byte, hoặc null nếu không đọc được / là thư mục.</param>
        /// <param name="note">Lý do khi size = null; chuỗi rỗng nếu đọc được.</param>
        /// <returns>true nếu đọc được kích thước.</returns>
        /// <remarks>
        /// Thứ tự các khối catch có ý nghĩa: FileNotFoundException, DirectoryNotFoundException
        /// và PathTooLongException đều là lớp con của IOException, nên phải bắt chúng TRƯỚC.
        /// Đặt catch (IOException) lên đầu thì mọi trường hợp đều bị báo nhầm là "đang bị khóa".
        /// </remarks>
        public static bool TryGetSize(string path, out long? size, out string note)
        {
            size = null;
            note = string.Empty;

            try
            {
                // Sự kiện Created/Changed/Renamed cũng được phát cho thư mục.
                if (Directory.Exists(path))
                {
                    note = NoteDirectory;
                    return false;
                }

                size = new FileInfo(path).Length;
                return true;
            }
            catch (FileNotFoundException)
            {
                note = NoteGone;
            }
            catch (DirectoryNotFoundException)
            {
                // Cả thư mục chứa tệp đã bị xóa hoặc đổi tên.
                note = NoteGone;
            }
            catch (PathTooLongException)
            {
                note = NoteInvalid;
            }
            catch (IOException)
            {
                // Còn lại sau các lớp con ở trên: vi phạm chia sẻ (sharing violation),
                // tệp bị khóa, lỗi thiết bị...
                note = NoteLocked;
            }
            catch (UnauthorizedAccessException)
            {
                note = NoteDenied;
            }
            catch (SecurityException)
            {
                note = NoteDenied;
            }
            catch (ArgumentException)
            {
                note = NoteInvalid;
            }
            catch (NotSupportedException)
            {
                note = NoteInvalid;
            }

            size = null;
            return false;
        }
    }
}
