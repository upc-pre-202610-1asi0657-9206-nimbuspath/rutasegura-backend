import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const directory = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const portIndex = args.indexOf("--port");
const port = portIndex < 0 ? 5500 : Number(args[portIndex + 1]);
if (!Number.isInteger(port) || port < 0 || port > 65535) { console.error("Puerto invalido."); process.exit(1); }
const files = new Map([
  ["/", ["index.html", "text/html; charset=utf-8"]],
  ["/index.html", ["index.html", "text/html; charset=utf-8"]],
  ["/styles.css", ["styles.css", "text/css; charset=utf-8"]],
  ["/app.js", ["app.js", "application/javascript; charset=utf-8"]],
  ["/firebase-config.js", ["firebase-config.js", "application/javascript; charset=utf-8"]],
  ["/firebase-messaging-sw.js", ["firebase-messaging-sw.js", "application/javascript; charset=utf-8"]]
]);

const server = createServer(async (request, response) => {
  const actualPort = server.address().port;
  if (![ `127.0.0.1:${actualPort}`, `localhost:${actualPort}` ].includes(request.headers.host)) { response.writeHead(403).end(); return; }
  if (!["GET", "HEAD"].includes(request.method)) { response.writeHead(405, { Allow: "GET, HEAD" }).end(); return; }
  let resource;
  try { resource = files.get(new URL(request.url, "http://127.0.0.1").pathname); }
  catch { response.writeHead(400).end(); return; }
  if (!resource) { response.writeHead(404).end(); return; }
  try {
    const content = await readFile(join(directory, resource[0]));
    response.writeHead(200, {
      "Content-Type": resource[1], "Content-Length": content.length,
      "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff",
      "X-Frame-Options": "DENY", "Referrer-Policy": "no-referrer",
      ...(resource[0] === "firebase-messaging-sw.js" ? { "Service-Worker-Allowed": "/" } : {})
    });
    response.end(request.method === "HEAD" ? undefined : content);
  } catch { response.writeHead(500).end("No se pudo cargar la pagina local."); }
});
server.on("error", error => {
  console.error(error.code === "EADDRINUSE" ? `El puerto ${port} esta ocupado. Usa -Port 5501 en el script.` : "No se pudo iniciar la pagina local.");
  process.exitCode = 1;
});
server.listen(port, "127.0.0.1", () => {
  console.log(`Pagina de prueba FCM: http://127.0.0.1:${server.address().port}`);
  console.log("Abre esa direccion en Chrome o Edge. Pulsa Obtener token y acepta el permiso.");
  console.log("Ctrl+C para detener. No se leen credenciales privadas ni se registran tokens en el servidor.");
});
