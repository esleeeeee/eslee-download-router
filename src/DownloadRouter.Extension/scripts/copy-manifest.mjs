import { copyFile, mkdir } from "node:fs/promises";

const output = new URL("../dist/", import.meta.url);
await mkdir(output, { recursive: true });
await copyFile(new URL("../manifest.json", import.meta.url), new URL("manifest.json", output));
