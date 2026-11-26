// Production-image check (misc/docs/guides/local-setup.md §9): forwards this network namespace's 127.0.0.1 ports to the
// same ports on the Docker host, so containers reach local Supabase, Redis and Mailpit at the addresses tokens were issued for.
const net = require("node:net");
for (const port of [55421, 55422, 55424, 6379]) {
  net
    .createServer((c) => {
      const s = net.connect(port, "host.docker.internal");
      c.pipe(s).pipe(c);
      c.on("error", () => s.destroy());
      s.on("error", () => c.destroy());
    })
    .listen(port, "127.0.0.1", () => console.log("relay", port));
}
