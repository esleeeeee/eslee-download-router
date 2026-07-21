export interface DownloadSourceMetadata {
  fileName: string;
  filePath: string | null;
  initiatingPageUrl: null;
  initialUrl: string | null;
  finalUrl: string | null;
  referrerUrl: string | null;
}

export interface DownloadLike {
  filename?: string;
  url?: string;
  finalUrl?: string;
  referrer?: string;
}

export function sourceMetadata(item: DownloadLike): DownloadSourceMetadata {
  const filePath = nonEmpty(item.filename);
  return {
    fileName: baseName(filePath) ?? "download",
    filePath,
    // Chromium's downloads API does not expose the initiating tab ID. Never guess
    // from the active tab; referrer and file URLs remain separate fallback fields.
    initiatingPageUrl: null,
    initialUrl: httpUrlOrNull(item.url),
    finalUrl: httpUrlOrNull(item.finalUrl),
    referrerUrl: httpUrlOrNull(item.referrer),
  };
}

export function httpUrlOrNull(value: string | undefined): string | null {
  const normalized = nonEmpty(value);
  if (normalized === null) {
    return null;
  }

  try {
    const url = new URL(normalized);
    return url.protocol === "http:" || url.protocol === "https:" ? normalized : null;
  } catch {
    return null;
  }
}

function baseName(path: string | null): string | null {
  if (path === null) {
    return null;
  }

  const parts = path.split(/[\\/]/u);
  return parts.at(-1) ?? null;
}

function nonEmpty(value: string | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : null;
}
