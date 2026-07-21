import assert from "node:assert/strict";
import test from "node:test";
import { httpUrlOrNull, sourceMetadata } from "../src/source-attribution.js";

void test("keeps referrer and file URLs in separate fields", () => {
  const result = sourceMetadata({
    filename: "C:\\Users\\Example\\Downloads\\report.pdf",
    url: "https://cdn.example.test/download?id=secret",
    finalUrl: "https://cdn.example.test/final?id=secret",
    referrer: "https://portal.example.test/course",
  });
  assert.equal(result.fileName, "report.pdf");
  assert.equal(result.initiatingPageUrl, null);
  assert.equal(result.referrerUrl, "https://portal.example.test/course");
  assert.equal(result.initialUrl, "https://cdn.example.test/download?id=secret");
});

void test("does not treat blob, data, or malformed values as HTTP source URLs", () => {
  assert.equal(httpUrlOrNull("blob:https://example.test/id"), null);
  assert.equal(httpUrlOrNull("data:text/plain,hello"), null);
  assert.equal(httpUrlOrNull("not a URL"), null);
});
