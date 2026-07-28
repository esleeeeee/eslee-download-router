import http from "node:http";

const port = Number.parseInt(process.argv[2] ?? "8765", 10);
const host = "127.0.0.1";

const page = `<!doctype html>
<meta charset="utf-8">
<title>eslee Download Router Whale validation</title>
<h1>Whale validation downloads</h1>
<ul>
  <li><a href="/automatic">Automatic test</a></li>
  <li><a href="/select">SelectSubfolder test</a></li>
  <li><a href="/slow">Cancellation test</a></li>
  <li><a href="/pending">Pending-name test</a></li>
</ul>`;

const fixtures = new Map([
  ["/automatic", ["whale-automatic-test.txt", "automatic routing fixture\n"]],
  ["/select", ["whale-tree-test.txt", "tree selection fixture\n"]],
]);

const server = http.createServer((request, response) => {
  const url = new URL(request.url ?? "/", `http://${host}:${port}`);
  if (url.pathname === "/") {
    response.writeHead(200, { "content-type": "text/html; charset=utf-8" });
    response.end(page);
    return;
  }

  const fixture = fixtures.get(url.pathname);
  if (fixture) {
    const [fileName, body] = fixture;
    response.writeHead(200, {
      "content-type": "text/plain; charset=utf-8",
      "content-disposition": `attachment; filename="${fileName}"`,
      "content-length": Buffer.byteLength(body),
    });
    response.end(body);
    return;
  }

  if (url.pathname === "/pending") {
    const body = "pending filename fixture\n";
    response.writeHead(200, {
      "content-type": "application/octet-stream",
      "content-length": Buffer.byteLength(body),
    });
    response.end(body);
    return;
  }

  if (url.pathname === "/slow") {
    const chunk = Buffer.alloc(64 * 1024, 0x61);
    const chunks = 200;
    let sent = 0;
    response.writeHead(200, {
      "content-type": "application/octet-stream",
      "content-disposition": "attachment; filename=whale-cancel-test.bin",
      "content-length": chunk.length * chunks,
    });
    const timer = setInterval(() => {
      if (response.destroyed || sent >= chunks) {
        clearInterval(timer);
        if (!response.destroyed) response.end();
        return;
      }
      response.write(chunk);
      sent += 1;
    }, 100);
    response.on("close", () => clearInterval(timer));
    return;
  }

  response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
  response.end("not found\n");
});

server.listen(port, host, () => {
  process.stdout.write(`Whale validation server listening on http://${host}:${port}/\n`);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
