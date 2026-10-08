---
name: codepier
description: |
  Development against a CodePier hot-swapped Kubernetes pod. Use whenever the project has a
  codepier.yaml, or the user mentions codepier, a hot swap, "the swapped pod", "the container",
  or the codepier CLI (up, down, ssh, exec, proxy, tail).

  Read this BEFORE running any install, build, codegen, migration, or test command in such a
  project — the code runs in a Linux container in a cluster, not on the local machine, and running
  the command on the wrong side corrupts the tree. Also use when diagnosing "works locally but not
  in the pod" failures, native-module/platform errors right after an install, or when resolving a
  .local hostname or port to a service.
metadata:
  version: 2.0.47
---

# CodePier

CodePier hot-swaps a Kubernetes workload with a dev container. `codepier up` scales the real
workload to 0, starts a `<workload>-hot-swap` pod running the image from `codepier.yaml`, and
Mutagen keeps the local tree and the container's tree in sync.

**The consequence that matters: the editor is local, the runtime is Linux in a cluster, and the two
filesystems are continuously mirrored into each other.** Almost every mistake in a CodePier project
comes from forgetting one of those three facts.

**If a `codepier` command is refused before it even runs** — "blocked by the classifier", or a
permission prompt in a session that cannot answer one — the project is missing the allow rule, not
the tool. `codepier skills install` writes it into `.claude/settings.json` alongside this skill; add
it by hand if it is absent:

```json
{ "permissions": { "allow": ["Bash(codepier:*)"] } }
```

## 0. Which cluster: `~/.kube/codepier`, or ask.

**`~/.kube/codepier` is the kubeconfig for this tool. If it exists, use it.** If it does not, ask
the user which kubeconfig and context to use — and wait for the answer.

**Never fall back to the ambient default** — `kubectl config current-context`, `$KUBECONFIG`, or
`~/.kube/config`. A machine that works on several clusters keeps a file per cluster under
`~/.kube/`, and the ambient default is frequently production. A read-only `get` against the wrong
cluster is still the wrong cluster, and `codepier up` scales a workload to zero.

Pass it explicitly on every invocation, so nothing depends on ambient state:

```bash
codepier --config ~/.kube/codepier exec -- true
kubectl --kubeconfig ~/.kube/codepier get deploy -n <ns>
```

**Confirm before reaching for `kubectl` at all.** `~/.kube/codepier` settles _which_ cluster, not
_whether_ you may poke at it directly. `codepier exec` / `tail` / `sync-status` are the tool's own
surface and need no extra ceremony, but plain `kubectl` is a wider blade: ask the user before your
first `kubectl` in a session, say what you intend to read, and keep it read-only. Anything that
mutates cluster state — `scale`, `delete`, `apply`, `patch`, `rollout` — is out of bounds regardless
of the answer (see Never, below).

Resolve this once per session and reuse the answer. If the user names a cluster in passing ("use
5stackgg"), that is the answer — keep passing it explicitly. If any command fails with an auth or
permission error, re-confirm the target before retrying; never silently fall through to another
context.

## 1. Establish the session first

Read `./codepier.yaml` and work out:

| What         | Where                                                                |
| ------------ | -------------------------------------------------------------------- |
| namespace    | `namespaces` (string, or an array meaning "was chosen at `up` time") |
| workload     | `workload`, falling back to the legacy `deployment`                  |
| kind         | `kind`, default `Deployment`                                         |
| container    | `containers`, or `dev` when `supplemental: true`                     |
| working dir  | `workdir`                                                            |
| path mapping | `sync`, as `<local>:<container>`                                     |

Then confirm a swap is actually live:

```bash
codepier exec -- true
```

**If that fails, the workload has no swap.** Starting one is the user's decision, not yours: `up`
scales the real workload to zero and will disconnect a teammate who already has a swap on it. Ask,
and wait for an answer.

Once the user has said yes, `--command` is how you run one — it needs no TTY, tears the swap down
afterwards, and exits with the command's own status, so a script or agent can branch on it:

```bash
codepier up --namespace <ns> --command 'rm -rf node_modules && yarn install'
```

It waits for the initial sync before running, and flushes changes back before tearing down — so
whatever the command wrote in the container is on disk locally when the process exits. Bare
`codepier up` opens an interactive shell instead and will crash outright without a TTY.

**Never pass `--deployment` to `up`** — `codepier.yaml` already names the workload. Pass
`--namespace` only when `namespaces` is an array, which means the namespace was chosen at `up` time.

### Tearing a swap down

`--command` tears its own swap down on exit, so you normally never run `down`. You need it when a
session died without cleaning up — a killed process, a dropped connection — which leaves the swap in
place and the **real workload scaled to zero**. Check with `codepier down` before walking away from a
session that ended badly.

Bare `codepier down` prompts you to pick from a list, so it hangs forever without a TTY. Always say
which one:

```bash
codepier down --deployment <workload> --namespace <ns>   # exactly one — prefer this
codepier down --all                                      # every swap in the cluster
```

`--deployment` takes the **original** workload name (the one in `codepier.yaml`), not the
`-hot-swap` object. A single match needs no `--all`: naming it is the choice. With no terminal and
several matches it refuses and lists them rather than hanging.

`down` restores the replica count `up` recorded on the swap. Swaps created by an older CLI carry no
such annotation and come back at 1 replica — check the count afterwards if the workload ran more.

## 2. Two filesystems, one tree

`sync` maps local paths to container paths — with `.:/opt/myapp`, the local `src/index.ts` is
`/opt/myapp/src/index.ts` in the pod. Translate in both directions when reading a stack trace or
citing a file, and cite the **local** path to the user.

**Always edit files locally.** Mutagen propagates them within a second or so. Never edit through
`codepier exec` — a heredoc or `sed` inside the pod races the syncer and can lose the change.

## 3. Build-critical work runs in the container

Use `codepier exec` for anything whose _output_ lands in a synced path or that depends on the Linux
runtime:

```bash
codepier exec -- pnpm install
codepier exec -- pnpm run codegen
codepier exec -- pnpm run build
codepier exec -- pnpm test
codepier exec --cwd /opt/myapp/packages/db -- pnpm run migrate
```

`exec` is non-interactive: it resolves the pod from `codepier.yaml`, starts in `workdir` (override
with `--cwd`), streams stdout/stderr, and exits with the command's own exit code. Diagnostics go to
stderr, so stdout is safe to pipe.

Things that only read the source — lint, formatting, type-reading, `grep` — are fine locally.

**If you are unsure whether a command needs to run in the container, ask the user.** Getting this
wrong is expensive to undo.

## 4. The sync-back trap

Mutagen runs in `two-way-resolved` mode: files written **in the container flow back to the local
machine**, and vice versa. So an install run on the wrong side doesn't just fail — it overwrites
the other side's artifacts.

Symptoms: a native module built for the wrong platform, `Exec format error`, an architecture
mismatch, or a binary that runs in the pod but not locally (or the reverse) right after a dependency
change.

Platform-specific output — `node_modules`, `dist`, `target`, `.venv` — must be either listed in
`ignore:` or kept on a `cache:` volume so it never crosses the boundary. If a project has neither
and hits this, say so rather than papering over it with a reinstall; some repos add a dedicated
script that installs into the container and moves the result into place.

## 5. Proxy

`codepier proxy` serves the `proxy:` entries over HTTPS using locally-trusted mkcert certificates.
`.local` hostnames resolve via mDNS with no `/etc/hosts` edits. `--port N` avoids needing sudo for 443. `--exec "<cmd>"` runs a command once the proxy is ready, with `NODE_EXTRA_CA_CERTS` set so
child processes trust the certs:

```bash
codepier proxy --port 8443
codepier proxy --exec "pnpm run codegen && pnpm dev"
```

To resolve a URL, match the `hostname` entry, then check its `routes[]` for a matching
`pathPrefix` **before** falling back to the host's own `service`/`port` — first match wins, and a
prefix matches the exact path and any subpath (`/auth` matches `/auth/github`, not `/authxyz`). An
entry with no `service` points at a port on the local machine, not the cluster. `remoteHostname`
rewrites the `Host` header sent upstream.

## 6. Logs

`codepier tail` is how you read the running app. The hot-swap container's entrypoint is a sleep, so
`kubectl logs` shows nothing useful — the app's output belongs to whatever the user started in
their own `codepier up` / `codepier ssh` shell. While that shell is open, the CLI mirrors it to
`~/.codepier/sessions/<namespace>-<workload>.log`, and `tail` follows that file:

```bash
codepier tail
```

It streams until the user's shell exits, so **run it in the background and read the output**, or it
will block. Its output is plain text when it isn't going to a terminal, so the backgrounded output
reads clean; the mirror file itself keeps colour codes, so read it through `tail` rather than
opening the file. The mirror is capped at the most recent 2MB, and the file
exists only while a session is live — no file means no shell, and with no session `tail` falls back
to the container's own (near-empty) logs. A mirror left behind by a killed shell is detected by the
PID in the `.pid` file beside it and removed the next time `tail` runs. **Empty output still does not mean the app is down**: it
can equally mean the user has no shell open. Check for the session file before concluding anything,
and ask the user if it isn't there.

Only the swapped container's shell is mirrored. Another container in the pod — a sidecar, or the
workload's real container next to a `supplemental` swap — has real logs of its own, and
`codepier tail --container <name>` reads them straight from the pod instead of the mirror
(`--container-logs` does the same for the swapped container).

When `tail` shows nothing or the wrong thing, rerun it with `-v`: it prints to stderr which stream it
picked (session mirror or pod), the pod and container it resolved, the log request's HTTP status,
and every stream drop and reconnect.

`forward:` lists `local:remote` port forwards. Don't assume a port is reachable on localhost unless
it's there or covered by a `proxy` entry.

## 7. Never

- Use the ambient kubeconfig/context. Ask which cluster, and pass `--config` / `--context`
  explicitly every time — the default is often production.
- Start or stop a session without asking. Once the user agrees, run it headless with
  `codepier up --command '<cmd>'`; bare `up` needs a TTY it will not have, and bare `down` will sit
  on a prompt — pass `--deployment`/`--namespace`, or `--all`.
- Walk away from a session that died mid-run without checking `codepier down`. A stranded swap
  leaves the real workload at zero replicas.
- `kubectl scale`, `kubectl delete`, or otherwise mutate the workload; the swap owns its lifecycle
  and a stray change strands the real workload at 0 replicas.
- Edit files inside the pod.
- Run installs or builds on the local machine "just to check" — that is the failure this whole
  setup is designed to avoid.

## Reference

`references/codepier-yaml.md` — every `codepier.yaml` field, its meaning, and worked examples.
