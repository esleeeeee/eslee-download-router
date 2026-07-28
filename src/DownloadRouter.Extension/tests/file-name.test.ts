import assert from "node:assert/strict";
import test from "node:test";
import { trustedDownloadFileName } from "../src/file-name.js";

void test("temporary Chromium names are never presented as final names", () => {
  assert.equal(trustedDownloadFileName("download"), null);
  assert.equal(trustedDownloadFileName("C:\\Downloads\\미확인 197533.crdownload"), null);
  assert.equal(trustedDownloadFileName("file.partial"), null);
  assert.equal(trustedDownloadFileName("file.tmp"), null);
});

void test("a downloads API final path yields only its base file name", () => {
  assert.equal(trustedDownloadFileName("C:\\Downloads\\실제 파일 이름.zip"), "실제 파일 이름.zip");
});
