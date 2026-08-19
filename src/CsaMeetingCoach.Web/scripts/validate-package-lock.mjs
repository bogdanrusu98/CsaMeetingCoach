import { readFileSync } from "node:fs";

const lock = JSON.parse(
  readFileSync(new URL("../package-lock.json", import.meta.url), "utf8"),
);
const approvedFeedHost = /^ms-feed-[0-9]+\.pkgs\.visualstudio\.com$/i;

for (const packageMetadata of Object.values(lock.packages ?? {})) {
  if (!packageMetadata.resolved) {
    continue;
  }

  const resolved = new URL(packageMetadata.resolved);
  const approvedHost =
    resolved.hostname.toLowerCase() === "packagefeedproxy.microsoft.io" ||
    approvedFeedHost.test(resolved.hostname);
  if (resolved.protocol !== "https:" || !approvedHost) {
    throw new Error("package-lock.json contains an unapproved resolved URL.");
  }
}

console.log("All package-lock URLs use approved Microsoft CFS hosts.");
