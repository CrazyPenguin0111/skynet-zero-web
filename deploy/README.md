# Deploy the community platform

The backend runs on one Linux VDS. The same published browser client falls back to the local bot on GitHub Pages, where no API exists. Accounts, matches, ratings, studies and messages require the backend. Do not put a database or Discord secret in the static site.

## Current target

- Site: `https://play.5dchess.net`
- SSH: `echung@play.5dchess.net`, port 22
- Original league database: `/home/eugene/elo_bot.db` on the development machine
- Discord callback: `https://play.5dchess.net/signin-discord`

Deployment has not been performed: both the IP and domain rejected the available SSH key, and interactive SSH requests a password. Before installation, inspect the host architecture, OS, existing services on ports 80/443/5080 and its current reverse proxy. Preserve other sites. The service below does not require Docker. It listens only on loopback.

## Build and test

Use the .NET 10 SDK, its `wasm-tools` workload, and Python 3. For browser tests, install Playwright and Chromium in a development environment.

```sh
python3 tools/verify-bundle.py
dotnet workload install wasm-tools
dotnet run --project tests/EngineTests.csproj -c Release
dotnet run --project platform-tests/PlatformTests.csproj -c Release
bash tools/package-platform.sh linux-x64
node tools/wasm-smoke.mjs
node tools/worker-smoke.mjs
node tools/browser-controls.mjs
```

Use `linux-arm64` instead if the VDS reports `aarch64`. The resulting `artifacts/skynetzero-<rid>-<revision>.tar.gz` contains a self-contained server and the client; no .NET installation is needed on the VDS. The system still needs the normal Linux .NET native dependencies, including ICU, OpenSSL and zlib. The package excludes databases, cookie keys and credentials. Keep the source revision with each release.

For local browser verification, publish the client and launch a disposable database:

```sh
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5088 \
ASPNETCORE_WEBROOT="$PWD/artifacts/publish/wwwroot" \
DataPath=/tmp/skynet-browser-platform \
dotnet run --project server/SkynetZero.Server.csproj -c Release
```

In another terminal run `node tools/community-browser.mjs http://127.0.0.1:5088`. This creates two test accounts and exercises private studies, analysis, messages, a complete two-game match, reconnect, tournament registration and mobile layout. `PLAYWRIGHT_MODULE`, `CHROMIUM_PATH` and `SKYNET_SCREENSHOTS` are optional overrides. Never run it against production.

## First installation

After inspecting the host, create a dedicated unprivileged system account `skynetzero`, a release directory under `/opt/skynetzero/releases/`, and private state under `/var/lib/skynetzero`. Transfer the package, compare its SHA-256 checksum, and extract it into a new release directory. Point `/opt/skynetzero/current` to that directory. Application files should be root-owned; the service account only writes its state directory.

```sh
sudo useradd --system --home-dir /var/lib/skynetzero --shell /usr/sbin/nologin skynetzero
sudo install -d -o skynetzero -g skynetzero -m 0700 /var/lib/skynetzero
sudo install -d -m 0755 /opt/skynetzero/releases /etc/skynetzero
sudo install -m 0600 deploy/platform.env.example /etc/skynetzero/platform.env
sudo install -m 0644 deploy/skynetzero.service /etc/systemd/system/skynetzero.service
```

Run account creation only if that account does not already exist. Set `/etc/skynetzero/platform.env` on the VDS. `TrustedProxy=127.0.0.1` matches a reverse proxy on the same host; use its actual source address if the hosting layout differs. Never trust arbitrary forwarded headers. `AllowedHosts` must include the public domain. Authentication cookies require HTTPS in production.

Import the old league data **before accepting registrations**. Transfer a consistent SQLite backup of the source database to a private temporary path readable by the service account; do not copy a live SQLite file without its WAL. Stop the platform during operator commands:

```sh
sudo -u skynetzero env DataPath=/var/lib/skynetzero \
  /opt/skynetzero/current/SkynetZero.Server --import-league /private/path/elo_bot.snapshot.db
```

The import opens the source read-only, preserves all historical rows, and never overwrites an existing imported/claimed record. It can be rerun safely. It does not resume old seasons. Expected source inventory: **119 players, 1,237 historical games, 70 pairings, 108 rating history rows and 3 seasons** (1,537 archived rows including player snapshots). The source has Discord IDs, not usernames: OAuth claims match the permanent verified Discord ID, so username changes cannot give another person a league record.

Add [the Caddy site block](Caddyfile) to the existing Caddy configuration, validate it and reload. If the host already uses Nginx or another proxy, route this domain to `127.0.0.1:5080`, preserve the original Host, send `X-Forwarded-Proto` and `X-Forwarded-For`, disable response buffering for `/api/events`, and allow SSE connections longer than 15 seconds. Obtain TLS for the domain through that proxy. Do not install a second listener on ports already in use.

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now skynetzero
curl --fail http://127.0.0.1:5080/health -H 'X-Forwarded-Proto: https'
curl --fail https://play.5dchess.net/health
```

Verify public HTTPS, registration, login, a casual two-game match between controlled accounts, saved studies after restart, and the Discord redirect. Inspect `journalctl -u skynetzero`. Clock time continues through disconnects and service restarts; schedule maintenance when games are idle.

## Native bot and analysis searches

Signed-in players use `/api/engine/search`; guests, disabled engines and busy pools use the AOT browser engine. The endpoint requires the session cookie and CSRF token, checks suspended accounts, and permits six requests per account per minute. Searches never acquire the multiplayer write lock or write a game/study record.

`NativeSearch__Enabled=true` and `NativeSearch__Workers=1` are the defaults. Each request launches the same server executable in `--engine-worker` mode, before database initialization. It has a 384 MiB managed-heap ceiling, one search thread with background GC/JIT, a bounded 64,000-character PGN, bounded output, and a hard deadline of the selected search budget plus four seconds for startup/parsing. Cancellation kills the process tree. Keep one worker until VDS measurements justify more; the supported maximum is four. Set `NativeSearch__Enabled=false` to use only browser search. Allow at least 25 seconds for this route in the reverse proxy.

Depth is measured in complete multiverse turns. Native opening searches reached depth 10 within 16 seconds on the development machine, but VDS CPU speed and branching positions materially affect depth. Verify actual results after deployment; no fixed depth is guaranteed for every position.

## Discord and administrators

In the [Discord developer portal](https://discord.com/developers/applications), create/select the application, add the exact OAuth2 redirect above, and put its client ID and secret into `Discord__ClientId` and `Discord__ClientSecret` in the server environment file. Restart the service. Only the `identify` scope is requested; no bot invitation or message permission is required. Tokens are not persisted. Never paste the secret into chat or commit it.

Ordinary accounts use username/password or Discord sign-in. New non-league rating pools begin at 1500; a newly connected Discord player without a legacy record starts at the bot's league baseline of 1380. Discord-based accounts can always sign in again through Discord. Email recovery and two-factor authentication are not included in this release.

Register the intended administrator, then stop the service and grant the role explicitly:

```sh
sudo systemctl stop skynetzero
sudo -u skynetzero env DataPath=/var/lib/skynetzero \
  /opt/skynetzero/current/SkynetZero.Server --grant-admin YOUR_SITE_USERNAME
sudo systemctl start skynetzero
```

Alternatively, put the administrator's Discord user ID in `AdminDiscordIds`; the role is granted only after verified Discord authentication. The first registered account is never made administrator automatically.

Administrators open league registration, then start the season manually or choose a specific scheduled start. There is no recurring automatic job. A new season is available after the current one finishes. League players are grouped by their league Elo at the start, with Entry below 1410, Advanced below 1550, and Pro 1550 or above; balanced groups have at most seven players. Imported missed-season counters are retained, but the website does not apply inactivity deductions or modify Discord server roles.

## Updates, backups and rollback

Publish each update into a separate release directory. Run the tests and back up state before switching the `current` symlink. Stop the service while switching, restart, and verify HTTPS health and the browser client. Database migrations run at startup. Run only **one** instance against this SQLite database.

```sh
sudo -u skynetzero python3 /opt/skynetzero/current/deploy/backup-platform.py \
  /var/lib/skynetzero /var/lib/skynetzero/backups
```

The backup uses SQLite's online backup API, verifies database integrity, and includes persistent data-protection keys. Store an encrypted copy off the VDS. Back up `/etc/skynetzero/platform.env` separately with restricted access. Configure a daily systemd timer or existing backup service after choosing its external storage destination. Test a restore into a disposable directory before relying on it.

For an application-only rollback with a compatible schema, stop the service, point `current` to the prior release and restart. If a migration is incompatible, restore the matching database and keys from the pre-update archive while stopped, preserving the new database for investigation. Restoring an older backup loses subsequent activity; do not silently roll back user records.

This release targets a small community on a single server. It has bounded requests, API throttling, transactional results, login lockout, CSRF protection, ownership checks, message blocking and an administrator audit log. It has not been load-tested to large chess-site traffic, and it does not include automated anti-cheat, payments, email, team studies, or distributed hosting. Full PGNs are replayed for server validation; complex multiverse positions can take longer than ordinary chess positions.
