export type SupportedBrowser = "Whale" | "Edge" | "Chrome" | "Brave" | "Vivaldi" | "Opera";

export function detectBrowser(userAgent: string): SupportedBrowser {
  if (/Whale\//u.test(userAgent)) return "Whale";
  if (/Edg\//u.test(userAgent)) return "Edge";
  if (/OPR\//u.test(userAgent)) return "Opera";
  if (/Vivaldi\//u.test(userAgent)) return "Vivaldi";
  if (/Brave\//u.test(userAgent)) return "Brave";
  return "Chrome";
}
