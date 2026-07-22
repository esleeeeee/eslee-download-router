export function trustedDownloadFileName(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  if (!trimmed) {
    return null;
  }

  const parts = trimmed.split(/[\\/]/u);
  const fileName = parts.at(-1)?.trim();
  if (
    !fileName ||
    fileName.toLowerCase() === "download" ||
    /\.(?:crdownload|partial|tmp)$/iu.test(fileName)
  ) {
    return null;
  }

  return fileName;
}
