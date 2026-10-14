using DownloadRouter.Core.Localization;

namespace DownloadRouter.Core.Paths;

public static class SelectionDestination
{
    public static string ValidateFolder(string folder)
    {
        if (!Path.IsPathFullyQualified(folder))
            throw new ArgumentException(AppText.Pick("저장 위치는 절대 경로여야 합니다.", "The destination must be an absolute path."));
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException(AppText.Pick("선택한 폴더가 존재하지 않습니다.", "The selected folder does not exist."));
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException(AppText.Pick("연결된 폴더는 저장 위치로 사용할 수 없습니다.", "A linked folder cannot be used as a destination."));
        }
        return full;
    }

    public static string ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.'))
            throw new ArgumentException(AppText.Pick("유효한 파일 이름을 입력하세요. 경로와 끝의 공백·마침표는 사용할 수 없습니다.", "Enter a valid file name. Paths and a trailing space or period are not allowed."));
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException(AppText.Pick("Windows 예약 이름은 사용할 수 없습니다.", "Windows reserved names cannot be used."));
        return name;
    }
}
