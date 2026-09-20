namespace DownloadRouter.Core.Paths;

public static class SelectionDestination
{
    public static string ValidateFolder(string folder)
    {
        if (!Path.IsPathFullyQualified(folder))
            throw new ArgumentException("저장 위치는 절대 경로여야 합니다.");
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException("선택한 폴더가 존재하지 않습니다.");
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("연결된 폴더는 저장 위치로 사용할 수 없습니다.");
        }
        return full;
    }

    public static string ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.'))
            throw new ArgumentException("유효한 파일 이름을 입력하세요. 경로와 끝의 공백·마침표는 사용할 수 없습니다.");
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException("Windows 예약 이름은 사용할 수 없습니다.");
        return name;
    }
}
