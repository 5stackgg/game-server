// Runs inside the api container, which can reach the simulators and
// mediamtx by name:
//   rcon <host> <password> <command...>   what the panel's console sends
//   camera-on <path> / camera-off <path>  a webcam publishing to mediamtx, or not
import net from "node:net";

const [command, ...args] = process.argv.slice(process.argv.indexOf("-") + 1);

function packet(id, type, body) {
  const payload = Buffer.from(body, "utf8");
  const buffer = Buffer.alloc(14 + payload.length);
  buffer.writeInt32LE(10 + payload.length, 0);
  buffer.writeInt32LE(id, 4);
  buffer.writeInt32LE(type, 8);
  payload.copy(buffer, 12);
  return buffer;
}

function rcon(host, password, line) {
  return new Promise((resolve, reject) => {
    const socket = net.connect(27015, host);
    let received = Buffer.alloc(0);
    socket.setTimeout(10_000, () => reject(new Error("rcon timed out")));
    socket.on("error", reject);
    socket.on("connect", () => socket.write(packet(1, 3, password)));
    socket.on("data", (chunk) => {
      received = Buffer.concat([received, chunk]);
      while (received.length >= 4) {
        const size = received.readInt32LE(0);
        if (received.length < size + 4) {
          return;
        }
        const id = received.readInt32LE(4);
        const type = received.readInt32LE(8);
        const body = received.subarray(12, size + 2).toString("utf8");
        received = received.subarray(size + 4);
        if (type === 2 && id === 1) {
          socket.write(packet(2, 2, line));
        } else if (type === 0 && id === 2) {
          socket.end();
          resolve(body);
        }
      }
    });
  });
}

async function camera(path, on) {
  const base = "http://mediamtx:9997/v3/config/paths";
  const response = on
    ? await fetch(`${base}/add/${path}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          runOnInit:
            "ffmpeg -re -f lavfi -i testsrc=size=160x120:rate=5 -c:v libx264 -preset ultrafast " +
            "-tune zerolatency -f rtsp rtsp://localhost:$RTSP_PORT/$MTX_PATH",
          runOnInitRestart: true,
        }),
      })
    : await fetch(`${base}/delete/${path}`, { method: "POST" });
  if (!response.ok) {
    throw new Error(`mediamtx ${response.status}: ${await response.text()}`);
  }
}

switch (command) {
  case "rcon":
    process.stdout.write(await rcon(args[0], args[1], args.slice(2).join(" ")));
    break;
  case "camera-on":
    await camera(args[0], true);
    break;
  case "camera-off":
    await camera(args[0], false);
    break;
  default:
    throw new Error(`unknown command ${command}`);
}
