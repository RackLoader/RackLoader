import { spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const directory = path.dirname(fileURLToPath(import.meta.url));
const configPath = path.join(directory, "wrangler.jsonc");
const wranglerPath = path.join(directory, "node_modules", "wrangler", "bin", "wrangler.js");
const deploymentDirectory = path.join(directory, ".deployment");
const encryptionKeyPath = path.join(deploymentDirectory, "key-encryption-key");
const adminTokenPath = path.join(deploymentDirectory, "admin-token");
const workerUrlPath = path.join(deploymentDirectory, "worker-url.txt");
const isWindows = process.platform === "win32";

await ensureWranglerInstalled();
await authenticate();

let config = JSON.parse(await readFile(configPath, "utf8"));
let databaseId = findConfiguredDatabaseId(config);
if (!databaseId) databaseId = await findRemoteDatabaseId();
const hasDatabase = Boolean(databaseId);

await mkdir(deploymentDirectory, { recursive: true });
let encryptionKey;
try {
  encryptionKey = (await readFile(encryptionKeyPath, "utf8")).trim();
} catch {
  if (hasDatabase) {
    throw new Error(`Missing ${path.relative(directory, encryptionKeyPath)}. Restore this deployment secret backup; generating a new value would make stored publisher keys unreadable.`);
  }
  encryptionKey = randomBytes(32).toString("base64");
  await writeSecretFile(encryptionKeyPath, encryptionKey);
}

let adminToken;
try {
  adminToken = (await readFile(adminTokenPath, "utf8")).trim();
} catch {
  adminToken = `rka_${randomBytes(32).toString("base64url")}`;
  await writeSecretFile(adminTokenPath, adminToken);
}

if (!hasDatabase) {
  console.log("Creating Cloudflare D1 database…");
  await runWrangler(["d1", "create", "rackloader-publishers", "--binding", "DB"]);
  databaseId = await findRemoteDatabaseId();
  if (!databaseId)
    throw new Error("Wrangler created D1 but the new database could not be found in the account.");
}

writeDatabaseBinding(config, databaseId);
await writeFile(configPath, `${JSON.stringify(config, null, 2)}\n`, "utf8");

console.log("Applying D1 migrations…");
await runWrangler(["d1", "migrations", "apply", "rackloader-publishers", "--remote"]);

console.log("Uploading Worker secrets…");
await runWrangler(["secret", "bulk"], {
  input: JSON.stringify({ ADMIN_TOKEN: adminToken, KEY_ENCRYPTION_KEY: encryptionKey })
});

console.log("Deploying RackLoader Publisher…");
const deployOutput = await runWrangler(["deploy"], { quiet: true });
process.stdout.write(deployOutput);
const workerUrl = deployOutput.match(/https:\/\/[a-zA-Z0-9.-]+\.workers\.dev/)?.[0];
if (!workerUrl)
  throw new Error("Worker deployed, but Wrangler's output did not include a workers.dev URL. Copy the deployment URL from the output above into .deployment/worker-url.txt.");

await writeSecretFile(workerUrlPath, workerUrl);
console.log("\nDeployment is ready.");
console.log(`Website: ${workerUrl}`);
console.log(`Admin token: ${adminToken}`);
console.log(`Local deployment secrets: ${path.relative(directory, deploymentDirectory)} (ignored by git; back up this folder securely).`);

async function ensureWranglerInstalled() {
  try {
    await readFile(wranglerPath);
  } catch {
    console.log("Installing Wrangler…");
    await runProgram(isWindows ? "npm.cmd" : "npm", ["ci"], { cwd: directory, inherit: true });
  }
}

async function authenticate() {
  const result = await runWrangler(["whoami"], { allowFailure: true, quiet: true, returnResult: true, config: false });
  if (result.status === 0) return;
  console.log("Authorize Wrangler in your browser to continue.");
  await runWrangler(["login"], { inherit: true, config: false });
  await runWrangler(["whoami"], { inherit: true, config: false });
}

async function runWrangler(args, options = {}) {
  const commandArguments = [wranglerPath, ...args];
  if (options.config !== false) commandArguments.push("--config", configPath);
  const result = await runProgram(process.execPath, commandArguments, {
    cwd: directory,
    input: options.input,
    inherit: options.inherit,
    quiet: options.quiet
  });
  if (!options.allowFailure && result.status !== 0)
    throw new Error(`Wrangler command failed: wrangler ${args.join(" ")}\n${result.stderr || result.stdout}`);
  return options.returnResult ? result : result.stdout;
}

function runProgram(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: options.cwd,
      windowsHide: true,
      shell: isWindows && command.toLowerCase().endsWith(".cmd"),
      stdio: options.inherit ? "inherit" : ["pipe", "pipe", "pipe"]
    });
    let stdout = "";
    let stderr = "";
    if (!options.inherit) {
      child.stdout.setEncoding("utf8").on("data", (chunk) => { stdout += chunk; });
      child.stderr.setEncoding("utf8").on("data", (chunk) => { stderr += chunk; });
      if (options.input !== undefined) child.stdin.end(`${options.input}\n`);
      else child.stdin.end();
    }
    child.on("error", reject);
    child.on("close", (status) => {
      if (!options.inherit && !options.quiet) {
        if (stdout) process.stdout.write(stdout);
        if (stderr) process.stderr.write(stderr);
      }
      resolve({ status: status ?? 1, stdout, stderr });
    });
  });
}

function findConfiguredDatabaseId(config) {
  const databases = config.d1_databases || [];
  const isConfigured = (entry) => entry.database_id && entry.database_id !== "REPLACE_WITH_D1_DATABASE_ID";
  const candidate = databases.find((entry) => entry.database_name === "rackloader-publishers" && isConfigured(entry)) ||
    databases.find((entry) => entry.binding === "DB" && isConfigured(entry));
  return candidate?.database_id;
}

async function findRemoteDatabaseId() {
  const output = await runWrangler(["d1", "list", "--json"], { quiet: true });
  const jsonStart = output.indexOf("[");
  const jsonEnd = output.lastIndexOf("]");
  if (jsonStart < 0 || jsonEnd < jsonStart)
    throw new Error("Wrangler returned an unreadable D1 database list.");
  const matches = JSON.parse(output.slice(jsonStart, jsonEnd + 1))
    .filter((database) => database.name === "rackloader-publishers");
  if (matches.length > 1)
    throw new Error("More than one D1 database named rackloader-publishers exists. Keep the intended database ID in wrangler.jsonc to select it.");
  return matches[0]?.uuid;
}

function writeDatabaseBinding(config, databaseId) {
  const databases = config.d1_databases || [];
  const existing = databases.find((entry) =>
    entry.binding === "DB" || entry.database_name === "rackloader-publishers") || {};
  config.d1_databases = [
    {
      ...existing,
      binding: "DB",
      database_name: "rackloader-publishers",
      database_id: databaseId,
      migrations_dir: "migrations"
    },
    ...databases.filter((entry) =>
      entry.binding !== "DB" && entry.database_name !== "rackloader-publishers")
  ];
}

async function writeSecretFile(filePath, value) {
  await writeFile(filePath, `${value}\n`, { encoding: "utf8", mode: 0o600 });
}
