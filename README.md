# Hands-on .NET → Git → Jenkins → Docker → Kubernetes on Ubuntu

This is your practical workbook. Execute the commands yourself, read the files before running them, and write the observed results. Completing it gives you a real independently executed practice project to discuss. It does not turn the exercise into production employment experience.

Target: your Windows laptop develops the code; your Git repository stores it; Jenkins on your Ubuntu server checks out commits, builds and tests the API, packages a Docker image, and deploys it into a Kubernetes cluster on that server.

## 1. What you will build

A .NET 10 API records dummy payment requests in PostgreSQL. It validates amounts, supports an Idempotency-Key, rejects reuse with a different payload, and stores one record when concurrent requests use the same merchant/key. It does not process or transfer money. This connects your .NET background to infrastructure and a financial-domain discussion.

| Component | Job in this project |
|---|---|
| .NET API | Validate requests, store records, expose health and version endpoints |
| PostgreSQL | Persist records and enforce unique merchant/key pairs |
| xUnit | Check business validation and payload comparison |
| Dockerfile | Build source into a runnable non-root image |
| Compose | Run the local database, initializer and API |
| Git | Track code, tests, pipeline and deployment manifests |
| Jenkinsfile | Automate checkout, build, tests, image creation, deployment and smoke checks |
| kind | Run a real Kubernetes control plane and node inside Docker on Ubuntu |
| Deployment | Maintain two API replicas and roll out changes |
| Service | Give those changing Pods a stable route |
| StatefulSet + PVC | Run a practice database with persistent storage |
| Job | Initialize the fixed lab schema before deploying the API |
| Secret | Supply database configuration without committing it |

The server has two container layers: Docker hosts Jenkins and the kind node. Kubernetes uses the container runtime inside the kind node for API and database Pods. An image present in the host Docker daemon is not automatically present inside kind; this is why we load it explicitly.

This is a single-server practice cluster. Its two replicas provide process redundancy, not resilience to losing the Ubuntu server. For production, evaluate managed Kubernetes, separate build agents, artifact registries, restricted service accounts, TLS/authentication, managed databases, backups and monitoring.

## 2. Your schedule and stopping rules

Aim for 9–12 focused hours. Installation and downloads can add time. If your interview is Saturday at 3 pm IST, do the main lab Thursday evening/Friday, failures and rehearsal Saturday morning, and stop changes by 1 pm.

| Session | Time budget | Outcome you must demonstrate |
|---|---:|---|
| 1: local API | 60–90 min | Tests pass; create/replay/conflict requests work |
| 2: Docker | 60–90 min | API and database run through Compose; you explain every Dockerfile line |
| 3: Git and Ubuntu | 45–75 min | Source pushed and cloned; server Docker verified |
| 4: Kubernetes manually | 90–120 min | Two ready Pods; request survives a Pod restart |
| 5: Jenkins | 90–150 min | Git change automatically reaches the server |
| 6: break/fix and interview | 90–120 min | Diagnose four failures and explain a rollback |

Do not advance through an unexplained red checkpoint. Record the command, error, evidence, cause and fix. Keep an evidence.md locally with commit IDs, build numbers, image tags, test results and your explanations. Never copy credentials into it.

## 3. Before starting: tools and terminal conventions

All commands below labelled LAPTOP run in **Ubuntu under WSL2** on Windows, in your project directory. SERVER means your SSH session on Ubuntu. PowerShell commands are explicitly labelled. Keeping one Linux command style avoids PowerShell quoting mistakes.

If you already develop directly in Ubuntu/Linux, use that as your laptop environment. For Windows without WSL, install WSL2 using Administrator PowerShell `wsl --install`, reboot if requested, and create your Ubuntu user. Install Docker Desktop with the WSL2 engine, enable integration for your Ubuntu distro, and select Linux containers. Follow the official installer links at the end for prerequisites/licensing. Install the .NET **10 SDK** inside WSL; installing it only on Windows does not install it inside WSL. On a supported modern Ubuntu distro, the following non-admin SDK bootstrap keeps the SDK in your user directory for this short-lived lab. Microsoft's Ubuntu installation page lists exact distribution support and native dependencies.

LAPTOP:
```bash
sudo apt update
sudo apt install -y git curl python3 openssl unzip libicu-dev libssl-dev zlib1g libstdc++6
# If dotnet --list-sdks already lists 10.x, skip the next three commands.
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
uname -m
dotnet --info
docker version
docker compose version
git --version
python3 --version
```

If you used the user-directory installer, add `export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"` to ~/.bashrc so new terminals find it. Checkpoint: dotnet lists a 10.x SDK; Docker shows both client and server; Compose works. If Docker shows only client, start Docker Desktop and verify WSL integration. Do not install a second Docker daemon inside WSL for this walkthrough.

Download and extract the supplied ZIP. Put the project in your WSL home directory, for example `~/dotnet-devops-lab`, rather than building under `/mnt/c` if possible. In WSL, Windows Downloads is usually under `/mnt/c/Users/YOUR_WINDOWS_USER/Downloads`. Replace that placeholder. ZIP contains the dotnet-devops-lab folder.

```bash
cd ~
unzip /mnt/c/Users/YOUR_WINDOWS_USER/Downloads/dotnet-devops-lab.zip
cd ~/dotnet-devops-lab
```

Ubuntu server: dedicated practice VM preferred, modern Ubuntu, SSH access, internet downloads, at least about 4 CPU/8 GB RAM and 25 GB free disk for a comfortable lab. Lower resources may run slowly. Both amd64 and arm64 are accommodated; the server builds its own native images. On an existing server, check services and ports first, and use the separate names and loopback bindings below. Never run broad Docker cleanup against other workloads.

## 4. Phase A — run and understand the API locally

### A1. Read the source before running it

Open these files in your editor:

- `Program.cs`: endpoints, dependency injection, exception handling, readiness and duplicate handling.
- `PaymentRules.cs`: validation and normalization.
- `LabDbContext.cs`: database mapping and the unique index.
- `PaymentRulesTests.cs`: ten xUnit test cases.

Explain aloud: two replicas can both see “no existing row.” An application-level check alone is insufficient. The database unique index chooses one winner. The other catches that specific uniqueness violation, loads the winning record and returns the same ID, or 409 for a different payload. We do not catch every database error as a duplicate.

This only makes recording this request idempotent. A real payment gateway requires stable business operation IDs, durable workflow state, provider idempotency, retries, reconciliation and potentially an outbox. No external payment effect exists here.

### A2. Create the local secret and restore packages

LAPTOP, project root:
```bash
umask 077
printf 'LAB_DB_PASSWORD=%s\n' "$(openssl rand -hex 24)" > .env
dotnet restore DevOpsLab.slnx
dotnet build DevOpsLab.slnx -c Release --no-restore
dotnet test DevOpsLab.slnx -c Release --no-build
```

Restore generates `packages.lock.json` in both project directories because Directory.Build.props enables lock files. You must commit those later. The Dockerfile intentionally uses `--locked-mode`: missing or stale lock files fail the build instead of silently resolving changed dependencies.

Checkpoint: ten test cases pass. Package/network failure is an environment issue to investigate, not a reason to delete tests.

### A3. Start only the database, initialize the schema, run .NET outside Docker

```bash
docker compose up -d db
docker compose ps
```
Wait until db shows healthy. Then:
```bash
set -a
. ./.env
set +a
export ConnectionStrings__LabDb="Host=localhost;Port=5434;Database=payments;Username=labuser;Password=$LAB_DB_PASSWORD"
dotnet run --project src/PaymentRequest.Api -- --init-db
dotnet run --project src/PaymentRequest.Api -- --urls http://localhost:5000
```

Keep that terminal running. In a second WSL terminal, project root:
```bash
curl -fsS http://localhost:5000/health/live
curl -fsS http://localhost:5000/health/ready
curl -fsS http://localhost:5000/version
python3 scripts/smoke.py http://localhost:5000
```

Expected: PASS for create, replay, mismatch, invalid amount and ten concurrent same-key requests. Save the printed record ID.

Manually inspect one request:
```bash
curl -i http://localhost:5000/api/payment-requests \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: manual-laptop-001' \
  -d '{"merchantReference":"merchant-demo","amount":125.50,"currency":"INR"}'
```
Repeat exactly: first 201, next 200 with the same ID. Change amount to 126.50 with the same key: 409. New key and amount -1: 400. A malformed JSON body should be rejected by request binding.

Query the table:
```bash
docker compose exec db psql -U labuser -d payments \
  -c 'SELECT "Id", "MerchantReference", "Amount", "Status" FROM "PaymentRequests";'
```

Answer before moving on: why decimal for money? Why can SQL constraints still be necessary when C# checks duplicates? Why is readiness querying the table, while liveness is not? Why are schema changes initialized separately?

## 5. Phase B — Docker deeply enough to explain it

Stop the native API with Ctrl+C, then run:
```bash
docker compose up --build -d
docker compose ps
docker compose logs --tail=60 init api
python3 scripts/smoke.py http://localhost:5000
docker images lab-payments
docker compose exec api id
docker history lab-payments:local
```

Checkpoint: initializer exits successfully; API runs; database is healthy; smoke checks pass; API user is non-root. An exited-successful init container is expected, not a crashed API.

Read Dockerfile line by line:

| Instruction/concept | Explain using this project |
|---|---|
| SDK build stage | Contains compiler and tooling; restores and builds source |
| publish stage | Produces deployable application output |
| ASP.NET runtime stage | Runs published output without shipping the SDK |
| COPY | Adds build context files; .dockerignore removes irrelevant/sensitive files |
| RUN | Executes during image build, creating layers/cacheable steps |
| ENV | Default runtime environment; connection secret is injected later |
| USER $APP_UID | Run with the numeric non-root user provided by the .NET image |
| EXPOSE 8080 | Documents the application port; does not publish a host port |
| ENTRYPOINT | Starts the API when a container is created |
| Image vs container | Packaged template vs a running instance with its own lifecycle |

Ports: `127.0.0.1:5000:8080` means host loopback port 5000 maps to container 8080. PostgreSQL is reached from Compose API by hostname **db** and port **5432**. From native laptop .NET, use **localhost:5434**. Inside the API container, localhost points to that API container, not to the database.

A Compose network provides service-name DNS. A named volume stores PostgreSQL data separately from the database container's writable layer.

Persistence exercise:
```bash
docker compose restart db
# Wait for db healthy, then:
curl -i http://localhost:5000/api/payment-requests/SAVED_RECORD_ID
docker volume ls
```
Replace SAVED_RECORD_ID. Expect the record still exists. Restarting a container is not a backup test. `docker compose down` removes containers/network but normally keeps named volumes; `docker compose down -v` deletes this lab's volume and its data. Do not run -v until intentionally cleaning up.

Cache exercise: build twice and notice CACHED lines. Change PaymentRules.cs and rebuild; our simple COPY-all layout invalidates restore when source changes. Explain an improvement: copy solution/project/lock files first, restore, then copy source and publish. Ensure all project references and Directory.Build.props are copied before restore. Implement this after the main pipeline works, not during initial setup.

Useful commands and when to use them:
```bash
docker ps -a                      # running and stopped containers
docker compose logs -f api        # API stdout/stderr
docker inspect CONTAINER_NAME     # configuration, mounts, network and exit state
docker stats --no-stream          # resource observation
docker compose exec db sh         # shell inside a running DB container
docker compose down              # stop only this Compose project
```

Do not dump connection environment variables into screenshots or commits. Image tags identify references; digests identify content. `latest` is just a mutable tag, not an ordering mechanism. Our releases use commit+build tags; a production pipeline should enforce immutable artifacts and deploy recorded digests.

## 6. Phase C — Git from laptop to server

Use a new practice repository containing only this dummy project. A public GitHub repository avoids introducing private checkout credentials into your first build. Review everything before publishing. For a private repository, configure a read-only checkout credential in Jenkins; do not put access tokens in remote URLs.

LAPTOP:
```bash
git init -b main
git config user.name 'YOUR_NAME'
git config user.email 'YOUR_GIT_EMAIL'
git add .
git status
git diff --cached --stat
git check-ignore .env
```

Expected: .env is ignored; packages.lock.json files are staged; no passwords, kubeconfigs, tokens, SSH keys or generated bin/obj files. Configure your real name/email instead of leaving placeholders.

Create an empty repository on GitHub in your browser, e.g. `dotnet-devops-lab`, without an initial README. Then:
```bash
git commit -m "Add payment request API and DevOps practice lab"
git remote add origin https://github.com/YOUR_ACCOUNT/dotnet-devops-lab.git
# For HTTPS authentication from WSL, use GitHub CLI's browser/device flow:
sudo apt install -y gh
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git
git push -u origin main
```

Follow the device-code/browser instructions and select your intended GitHub account. If gh is absent from your distro repositories, use GitHub CLI's official Linux installation instructions or your already configured Git SSH setup. Account passwords are not Git HTTPS authentication. Never paste a token into the repository.

Checkpoint: GitHub contains source, tests, lock files, Dockerfile, Jenkinsfile and k8s manifests. It does not contain .env.

## 7. Phase D — prepare Ubuntu server

Connect from Windows PowerShell using your existing SSH access:
```powershell
ssh -i "C:\path\server-key.pem" ubuntu@YOUR_SERVER_IP
```
Your server user may differ. SERVER:
```bash
uname -m
cat /etc/os-release
free -h
df -h
sudo ss -lntp
command -v docker || true
```
If Docker already works, skip its installation. If absent, use the official Docker Ubuntu repository instructions. For a clean supported Ubuntu VM, the essential commands are:
```bash
sudo apt update
sudo apt install -y ca-certificates curl git python3 openssl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
sudo tee /etc/apt/sources.list.d/docker.sources >/dev/null <<APT
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
APT
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker "$USER"
```
Log out of SSH and log back in for group membership. Docker group access effectively grants root-level host control. Use trusted accounts on the practice VM.

```bash
docker version
docker run --rm hello-world
git clone https://github.com/YOUR_ACCOUNT/dotnet-devops-lab.git ~/dotnet-devops-lab
cd ~/dotnet-devops-lab
```
Install kind for the server architecture:
```bash
case "$(uname -m)" in
  x86_64) LAB_ARCH=amd64 ;;
  aarch64) LAB_ARCH=arm64 ;;
  *) echo 'Unsupported architecture'; exit 1 ;;
esac
curl -fsSL "https://kind.sigs.k8s.io/dl/v0.33.0/kind-linux-$LAB_ARCH" -o /tmp/lab-kind
sudo install -m 0755 /tmp/lab-kind /usr/local/bin/kind
kind version
kind create cluster --name devops-lab --config k8s/kind.yaml --wait 180s
```

Install kubectl matching the Kubernetes version inside this node:
```bash
KUBECTL_VERSION=$(docker exec devops-lab-control-plane kubeadm version -o short)
curl -fsSL "https://dl.k8s.io/release/$KUBECTL_VERSION/bin/linux/$LAB_ARCH/kubectl" -o /tmp/lab-kubectl
curl -fsSL "https://dl.k8s.io/release/$KUBECTL_VERSION/bin/linux/$LAB_ARCH/kubectl.sha256" -o /tmp/lab-kubectl.sha256
printf '%s  /tmp/lab-kubectl\n' "$(cat /tmp/lab-kubectl.sha256)" | sha256sum -c -
sudo install -m 0755 /tmp/lab-kubectl /usr/local/bin/kubectl
kubectl config use-context kind-devops-lab
kubectl get nodes -o wide
kubectl version
kubectl get storageclass
```

Checkpoint: one Ready node; client and server compatible; default storage class available. If cluster creation fails, inspect `docker logs devops-lab-control-plane` and `kind export logs /tmp/devops-lab-logs --name devops-lab`. Do not create a second differently named cluster to hide the error.

## 8. Phase E — deploy Kubernetes manually before Jenkins

SERVER, project root:
```bash
kubectl config current-context
kubectl create namespace devops-lab
bash scripts/create-secret.sh
docker build -t lab-payments:manual-v1 .
kind load docker-image lab-payments:manual-v1 --name devops-lab
```

The secret script verifies the context, generates a random password, creates a temporary permission-restricted file, creates lab-db and deletes that file. It does not print the password. Run it once for a new database. Regenerating the Secret does not change the password already initialized inside a PostgreSQL volume.

### E1. Database and storage
```bash
kubectl -n devops-lab apply -f k8s/postgres.yaml
kubectl -n devops-lab rollout status statefulset/postgres --timeout=180s
kubectl -n devops-lab get pods,pvc,services
```
Read postgres.yaml: the headless Service supplies postgres DNS; the StatefulSet has stable identity postgres-0; its volumeClaimTemplate creates storage. A PVC requests storage from a storage class; the PV is the provisioned resource. On kind this is local practice storage. Losing/deleting the cluster or VM can lose the database. A PVC does not imply a backup or cross-node database failover.

### E2. Initialize schema using a Job
```bash
sed -e 's|__IMAGE__|lab-payments:manual-v1|g' -e 's|__RELEASE__|manual-v1|g' \
  k8s/init-db.yaml > release-schema.yaml
kubectl -n devops-lab apply -f release-schema.yaml
kubectl -n devops-lab wait --for=condition=complete job/schema-manual-v1 --timeout=120s
kubectl -n devops-lab logs job/schema-manual-v1
```

Expected: Lab schema initialized. Our --init-db uses EnsureCreated for a fixed-schema exercise. It does not incrementally migrate future schema changes. A production system needs reviewed EF migrations and a controlled migration strategy; never assume rollback of an application reverses database changes.

### E3. Deploy API and Service
```bash
sed -e 's|__IMAGE__|lab-payments:manual-v1|g' -e 's|__RELEASE__|manual-v1|g' \
  k8s/api.yaml > release-api.yaml
kubectl -n devops-lab apply -f release-api.yaml
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
kubectl -n devops-lab get deployment,replicasets,pods,services -o wide
curl -fsS http://127.0.0.1:18080/version
python3 scripts/smoke.py http://127.0.0.1:18080
```

Do not apply the templates before rendering __IMAGE__ and __RELEASE__. `scripts/deploy-manual.sh` automates E1–E3 for later repetitions; first execute the individual commands so you understand them.

Traffic: Ubuntu 127.0.0.1:18080 → kind node port 30080 → Service → a ready API Pod port 8080. `port:80` is the Service port; `targetPort:8080` is the container port; `nodePort:30080` is on the Kubernetes node. kind maps the last one to Ubuntu host loopback. These are four different meanings of port.

| Object | Explain by pointing to the running lab |
|---|---|
| Pod | One scheduled application unit, here one API container |
| ReplicaSet | Maintains a count of Pods matching a template |
| Deployment | Manages ReplicaSets, rollout and application revisions |
| Service | Stable abstraction selecting ready Pods by labels |
| Namespace | Logical scope for resources; not a security boundary by itself |
| Secret | Sensitive config object; base64 representation is not encryption |
| Job | Finite task, such as schema initialization |
| StatefulSet | Stable identities and storage association for the lab DB |
| Requests | Scheduling resource reservation/requirement |
| Limits | CPU throttling boundary or memory limit that can lead to OOM termination |

Our startup probe allows initialization time. Readiness checks whether the API can query its table; failure removes it from ready Service endpoints. Liveness checks that the process can respond; repeated failures restart the container. Database failure should not itself trigger endless liveness restarts.

### E4. Self-healing, persistence, scale

Save the record ID from smoke.py. In another server terminal:
```bash
kubectl -n devops-lab get pods -w
```
First terminal:
```bash
POD=$(kubectl -n devops-lab get pod -l app=payments-api -o jsonpath='{.items[0].metadata.name}')
kubectl -n devops-lab delete pod "$POD"
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
curl -i http://127.0.0.1:18080/api/payment-requests/SAVED_RECORD_ID
kubectl -n devops-lab delete pod postgres-0
kubectl -n devops-lab rollout status statefulset/postgres --timeout=180s
curl -i http://127.0.0.1:18080/api/payment-requests/SAVED_RECORD_ID
kubectl -n devops-lab scale deployment payments-api --replicas=3
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
kubectl -n devops-lab get pods -l app=payments-api
kubectl -n devops-lab scale deployment payments-api --replicas=2
```

Expected: new API Pod, same stored record; DB Pod reattaches its PVC and record survives; three ready API replicas after scaling. Database restart can cause temporary unavailability. This is a one-replica DB, not a highly available database.

Manual scale differs from HPA. HPA needs a metrics source and configuration; it is not installed here. Subsequent applying our manifest restores its declared replica count of two.

## 9. Phase F — Jenkins on Ubuntu

Jenkins itself runs in Docker. A named volume preserves jobs, plugins and credentials. The provided infra image adds the Docker CLI, kind and kubectl to Jenkins. The CLI talks to the host Docker daemon through its socket.

**Lab-specific privilege choice:** mounting `/var/run/docker.sock` gives Jenkins jobs broad host control. The uploaded kubeconfig also has administrator privileges in this practice cluster. Use only trusted code in this isolated lab. Production should use separated build agents and least-privilege deployment identities. Do not run untrusted pull requests through this controller.

SERVER:
```bash
KUBECTL_VERSION=$(docker exec devops-lab-control-plane kubeadm version -o short)
docker build -f infra/jenkins.Dockerfile \
  --build-arg KUBECTL_VERSION="$KUBECTL_VERSION" -t lab-jenkins:1 .
docker volume create lab-jenkins-home
docker run -d --name ci-jenkins --restart unless-stopped \
  --network kind \
  --group-add "$(stat -c '%g' /var/run/docker.sock)" \
  -p 127.0.0.1:8081:8080 \
  --memory 2g -e JAVA_OPTS='-Xmx1024m' \
  -v lab-jenkins-home:/var/jenkins_home \
  -v /var/run/docker.sock:/var/run/docker.sock \
  lab-jenkins:1
docker logs --tail=60 ci-jenkins
docker exec ci-jenkins docker version
docker exec ci-jenkins kind get clusters
```

Checkpoint: Jenkins running, Docker CLI sees host daemon, kind lists devops-lab. Permission denied on socket? Verify socket group ID and --group-add; do not chmod the socket to 666.

### F1. Access Jenkins securely from the laptop

Open a separate Windows PowerShell terminal and leave this SSH tunnel running:
```powershell
ssh -i "C:\path\server-key.pem" -N -L 8081:127.0.0.1:8081 -L 18080:127.0.0.1:18080 ubuntu@YOUR_SERVER_IP
```

Browse `http://localhost:8081` for Jenkins and `http://localhost:18080/version` for server API. No public Jenkins/API port needs to be opened. SSH port access must already work. If local 8081/18080 is occupied, change only the first port in its -L mapping and browse that new local port.

On server, retrieve the initial password:
```bash
docker exec ci-jenkins cat /var/jenkins_home/secrets/initialAdminPassword
```

Unlock Jenkins, install suggested plugins, create your administrator account. Confirm Pipeline, Git and Credentials Binding plugins are available. UI labels can vary by Jenkins release. For this isolated lab, set the built-in node's executors to **1** under Manage Jenkins → Nodes → Built-In Node → Configure. This lets the controller execute this demonstration; production controller/agent separation is a separate architecture decision.

### F2. Create the Jenkins Kubernetes credential

The host kubeconfig typically points to host localhost. Inside Jenkins, localhost means Jenkins itself. Generate a kubeconfig using kind's internal endpoint, reachable on its Docker network:
```bash
umask 077
kind get kubeconfig --name devops-lab --internal > lab-jenkins-kubeconfig
chmod 600 lab-jenkins-kubeconfig
```

Copy this file to your Windows laptop using a separate PowerShell terminal:
```powershell
scp -i "C:\path\server-key.pem" ubuntu@YOUR_SERVER_IP:~/dotnet-devops-lab/lab-jenkins-kubeconfig "$env:USERPROFILE\Downloads\lab-jenkins-kubeconfig"
```

Jenkins → Manage Jenkins → Credentials → System/global store → Add Credentials:
- Kind: Secret file
- File: lab-jenkins-kubeconfig
- ID: **lab-kubeconfig** (must match Jenkinsfile)
- Description: practice kind deployment identity

Delete the temporary laptop copy after upload. Keep it out of Git; .gitignore covers its name. This file is a credential, not a harmless connection URL. Recreating the kind cluster requires generating and updating it again.

### F3. Configure Pipeline from Git

New Item → name payment-devops-lab → Pipeline.
Under Pipeline:
- Definition: Pipeline script from SCM
- SCM: Git
- Repository URL: your repository's HTTPS URL
- Credentials: none for the public practice repository
- Branch: */main
- Script Path: Jenkinsfile
Save and click Build Now once.

After the first build Jenkinsfile installs its SCM polling trigger. Every approximately two minutes Jenkins checks for a new commit. This is real automatic Git-driven CI/CD through polling; it is not a webhook. A webhook would need a suitably secured reachable Jenkins endpoint or another integration. We keep this lab's Jenkins behind SSH.

Read the pipeline before watching it:

| Stage | What must succeed before the next stage |
|---|---|
| Checkout | Jenkins retrieves the Git commit |
| Build | Docker SDK stage restores locked dependencies and compiles |
| Unit tests | Test container runs xUnit; nonzero status stops deployment; TRX archived |
| Runtime image | Produces deployable image tagged commit-buildNumber |
| Load image | Copies image into kind's runtime |
| Database initialization | DB ready; unique schema Job completes |
| Deploy | Applies manifest; waits for two healthy replicas |
| Smoke checks | Health/version and real HTTP duplicate/concurrency checks succeed |

`agent any` allocates a Jenkins execution environment; here the built-in node. `withCredentials` makes the kubeconfig temporarily available through KUBECONFIG. `disableConcurrentBuilds` prevents overlapping releases from this job. `timeout` prevents indefinite hangs. `post/always` archives available test results even when a stage fails. A production deployment also needs environment promotion/approval policy and safeguards beyond serializing one job.

Checkpoint: green pipeline; ten unit tests; smoke PASS; version release equals commit-build number; two ready replicas. Runtime smoke failure marks the pipeline red after deployment; this Jenkinsfile **does not automatically roll back**. Investigate and explicitly roll back or redeploy a fix.

### F4. Prove automatic deployment from your laptop

LAPTOP: in Program.cs find /version and add an extra property inside its response object:
```csharp
message = "deployed-from-my-laptop-v2",
```
Example response object now has release, message and instance. Then:
```bash
dotnet test DevOpsLab.slnx -c Release
git add src/PaymentRequest.Api/Program.cs
git commit -m "Add version message to prove automatic deployment"
git push
```

Do not manually build/deploy on the server. Watch Jenkins detect the commit and complete all stages. Through the SSH tunnel:
```bash
curl -fsS http://localhost:18080/version
```

Expected: new release tag and message. Record the exact commit ID, Jenkins build number and image. This is your primary evidence that you understand the entire path.

### F5. Failed tests must block a release

In the practice repository only, intentionally change one test assertion to an incorrect expectation. Commit/push. Observe the Unit tests stage fail. Check the deployed /version still shows the prior successful release. Fix the assertion, commit/push and watch a green build.

Explain: unit tests run before runtime image/deploy; a failed test exits nonzero, causing the pipeline to stop. Test gating catches covered errors; it cannot prove all behavior is correct. Our HTTP checks supplement these unit tests.

## 10. Phase G — failure drills you should execute yourself

Do one drill at a time. Open a terminal watching Pods and another with API requests. Restore healthy state before the next. On server, run `git pull` after laptop changes when using source files for manual commands; Jenkins checks out its own workspace automatically.

### Drill 1: bad image
```bash
kubectl -n devops-lab set image deployment/payments-api api=lab-payments:does-not-exist
kubectl -n devops-lab get pods
kubectl -n devops-lab describe pod NEW_FAILING_POD_NAME
kubectl -n devops-lab rollout status deployment/payments-api --timeout=30s
```
Find the new failing Pod name in get pods. Expect ImagePullBackOff/ErrImagePull and image-related events. Old replicas should remain available with maxUnavailable 0 if they were healthy and capacity permits. Fix:
```bash
kubectl -n devops-lab rollout undo deployment/payments-api
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
```
Say: container never started, so application logs are not the first diagnostic; inspect events, image reference, registry access/pull secret, architecture and pull policy. Loading the image into kind and using IfNotPresent prevents trying to pull our valid local tag.

### Drill 2: database outage and readiness
```bash
kubectl -n devops-lab scale statefulset postgres --replicas=0
kubectl -n devops-lab get pods -w
```
After readiness failures, API Pods remain running but become 0/1 Ready. Check:
```bash
kubectl -n devops-lab get endpointslices -l kubernetes.io/service-name=payments-api -o yaml
curl -i --max-time 5 http://127.0.0.1:18080/health/ready
kubectl -n devops-lab logs deployment/payments-api --tail=40
```
Because Service has no ready backends, the curl may fail at connection/network level instead of returning API 503. To observe API liveness directly during the outage, in a separate server terminal:
```bash
kubectl -n devops-lab port-forward deployment/payments-api 19080:8080
```
Then curl 127.0.0.1:19080/health/live (200) and /health/ready (503). Restore:
```bash
kubectl -n devops-lab scale statefulset postgres --replicas=1
kubectl -n devops-lab rollout status statefulset/postgres --timeout=180s
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
python3 scripts/smoke.py http://127.0.0.1:18080
```
Readiness separates dependency availability from process liveness. It does not repair the database.

### Drill 3: wrong Service selector
```bash
kubectl -n devops-lab patch service payments-api \
  -p '{"spec":{"selector":{"app":"no-such-api"}}}'
kubectl -n devops-lab get pods --show-labels
kubectl -n devops-lab get endpointslices -l kubernetes.io/service-name=payments-api
curl -i --max-time 5 http://127.0.0.1:18080/version
```
Expected: Pods healthy but Service has no matching backends. Restore:
```bash
kubectl -n devops-lab patch service payments-api \
  -p '{"spec":{"selector":{"app":"payments-api"}}}'
```
Say: working Pods do not guarantee working traffic; compare selector, labels, endpoints, targetPort and listener.

### Drill 4: wrong readiness path
```bash
kubectl -n devops-lab patch deployment payments-api --type=json \
  -p '[{"op":"replace","path":"/spec/template/spec/containers/0/readinessProbe/httpGet/path","value":"/missing"}]'
kubectl -n devops-lab get pods
kubectl -n devops-lab describe pod NEW_POD_NAME
```
Expected: new Pod running but unready; rollout stalls, old healthy replicas retained under our strategy. Probe events show HTTP 404. Restore with rollout undo, wait for rollout. Then repeat changing **livenessProbe** path instead, observe restart count rise after its failure threshold and inspect `kubectl logs POD --previous`. Undo again. Readiness excludes traffic; liveness restarts. Startup probe suppresses those checks until startup succeeds.

### Drill 5: rollback a real release
```bash
kubectl -n devops-lab rollout history deployment/payments-api
kubectl -n devops-lab rollout history deployment/payments-api --revision=REVISION_NUMBER
kubectl -n devops-lab rollout undo deployment/payments-api --to-revision=GOOD_REVISION_NUMBER
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
curl -fsS http://127.0.0.1:18080/version
```
Use observed revision numbers, not guesses. Previous images must still be accessible to run. Rollback changes Pod template, not the database, not Git history and not business effects. Correct the source too: on laptop `git revert BAD_COMMIT_ID`, push, let Jenkins deploy the correction. Otherwise a later pipeline can reintroduce the bad configuration. Avoid destructive git reset on shared history.

### Drill 6: preserve Jenkins state
```bash
docker restart ci-jenkins
```
Wait for Jenkins. Job history and credential configuration should remain because /var/jenkins_home is on a named volume. This proves persistence across container restart, not backup or disaster recovery. Use selected-volume backups and restore tests for real Jenkins recovery.

## 11. Troubleshooting method: choose the failing layer

Start with the symptom and recent change, then gather evidence before changing configuration.

| Symptom | First evidence | Typical cause/fix |
|---|---|---|
| docker build restore failure | First error before later cascade | SDK/package/version, network, missing/stale lock file; regenerate and review locks locally |
| docker permission denied in Jenkins | docker version, socket ownership | Incorrect supplemental group; recreate Jenkins with correct --group-add |
| Jenkins cannot checkout | Console log, repository URL, branch | Wrong URL/branch or missing read-only credential |
| Jenkins cannot connect to Kubernetes | Context and endpoint, Docker network | Host-local endpoint used inside container; regenerate internal kubeconfig |
| Unauthorized/Forbidden | Identity and permission message | Expired credential vs insufficient RBAC; do not solve production by granting admin |
| Pod Pending | describe pod/events, get pvc/nodes | Insufficient resources, unbound storage, scheduling constraints |
| ImagePullBackOff | describe pod/events | Wrong tag, missing loaded image, registry authentication/network |
| CrashLoopBackOff | logs, logs --previous, describe | Process startup exception, missing config, bad command; DB auth failures may be visible |
| OOMKilled | describe termination reason, resource limits | Memory limit exceeded; measure and investigate allocation/load |
| Running 0/1 Ready | Probe events, direct API health | Wrong health path, unavailable DB, timeout |
| Service unreachable, Pods Ready | Labels, EndpointSlices, ports | Selector mismatch, targetPort, mapping or network problem |
| Deployment timeout | New ReplicaSet/Pod events | Readiness, capacity, image issue; do not simply increase timeout blindly |
| DB password authentication failure | DB logs, secret usage, persisted volume | Password was regenerated after DB initialization; existing DB password does not automatically change |

Command sequence:
```bash
kubectl config current-context
kubectl -n devops-lab get deployment,replicasets,pods,services,pvc
kubectl -n devops-lab get events --sort-by=.metadata.creationTimestamp
kubectl -n devops-lab describe pod POD_NAME
kubectl -n devops-lab logs POD_NAME --tail=100
kubectl -n devops-lab logs POD_NAME --previous
kubectl -n devops-lab get endpointslices -l kubernetes.io/service-name=payments-api
```
`logs --previous` needs a previous terminated container; an error when none exists is expected. Review logs before sharing; do not dump secrets. `kubectl top` needs metrics-server, which this kit does not install. Disk growth: inspect `docker system df` and images first; delete only identified lab artifacts, not broad system prune.

## 12. Registry and production extensions — after the core lab

The main pipeline distributes images using `kind load docker-image` on one host. It intentionally has **no registry push stage**. Explain this clearly; a multi-server deployment generally needs an artifact registry such as Azure Container Registry.

To learn the registry step, create a practice Docker Hub repository using your own account. Log in interactively using a token, then SERVER:
```bash
docker login --username YOUR_DOCKERHUB_ACCOUNT
# Paste a token at the password prompt, not in the command.
docker tag lab-payments:manual-v1 YOUR_DOCKERHUB_ACCOUNT/lab-payments:manual-v1
docker push YOUR_DOCKERHUB_ACCOUNT/lab-payments:manual-v1
docker pull YOUR_DOCKERHUB_ACCOUNT/lab-payments:manual-v1
```

Deploy that fully qualified image reference after rendering templates. A public repository can be pulled without a secret; a private registry needs a properly scoped imagePullSecret attached to the Pod's service account/template. Check network/rate-limit restrictions. If server is ARM, its image is ARM; another amd64 node cannot necessarily run it. Use buildx multi-platform builds and compatible base images for mixed architectures.

To automate pushing, store registry username/token in Jenkins credentials, add a stage after tests/image creation, use `docker login --password-stdin` within withCredentials, push a unique tag, then deploy the same tested artifact. Use a temporary Docker config directory and remove it afterwards; Jenkins masking is not a substitute for preventing leaks. For a real Azure implementation, prefer the organization's managed identity/service connection model and ACR+AKS policy. Do not claim Azure deployment was completed in this lab.

Further deepening, in this order:
1. Replace EnsureCreated with reviewed EF migrations; prove compatible old/new app versions during rollout.
2. Add authenticated API access, TLS ingress and authorization. This lab currently uses trusted SSH access and dummy data.
3. Separate Jenkins controller from agents; replace cluster-admin kubeconfig with namespace-scoped RBAC and appropriate database-initialization permissions.
4. Pin base image digests and dependency lock files; scan images and dependencies, collect SBOMs, promote the same artifact through environments.
5. Add integration tests against a disposable DB; handle migration failure and deploy failure explicitly.
6. Learn readiness under load, graceful shutdown and request draining. Our terminationGracePeriod alone does not prove zero dropped requests.
7. Add logs/metrics/traces and alerts; investigate HPA with metrics-server.
8. Test backups and restores. Move production DB persistence to an appropriate managed/HA solution.
9. Learn Helm values/templates, GitOps reconciliation, NetworkPolicies, resource quotas and disruption budgets after native manifests are familiar.

Two API replicas and maxUnavailable=0 do not guarantee zero downtime. Capacity, health checks, networking, in-flight requests and database compatibility all matter. maxSurge=1 needs room for the extra Pod. A one-node kind cluster cannot demonstrate real node-failure resilience.

## 13. Interview answers grounded in what you actually did

Do not memorize the sentences word for word. First point to your files and observed output, then answer in your own language.

**1. Explain your deployment workflow.**
I developed and tested a .NET API locally, pushed code to Git, and configured Jenkins Pipeline from SCM. Jenkins polled main, built in an SDK container, ran xUnit tests, created a non-root runtime image, loaded it into a kind cluster on Ubuntu, ran a schema-initialization Job, deployed two replicas, and checked health plus HTTP behavior. Each release carried the commit hash and build number. This was my independently executed practice project.

**2. Why Docker?**
It packages the application and runtime configuration consistently. I used a multi-stage build so the final image only needs ASP.NET runtime and published output. The database runs separately. Containers still depend on host architecture/kernel and runtime configuration; they do not eliminate every environment difference.

**3. Why Kubernetes when Docker can run the API?**
Docker runs containers; Kubernetes reconciles desired state across scheduled workloads. I used Deployments for replicas/rolling updates, Services for routing, probes for availability and a StatefulSet/PVC for practice database storage. Compose was sufficient for my local development environment.

**4. How did you reduce the image size?**
Separate SDK build/publish stages from the runtime stage, copy only publish output, exclude build context noise. I inspected image history. I would also optimize restore caching, assess base-image choices and scan dependencies; I would provide measured sizes rather than invented numbers.

**5. Explain EXPOSE versus publishing a port.**
EXPOSE documents the container port. Compose's 5000:8080 mapping actually forwards host traffic. In Kubernetes I use a Service and kind's node-to-host mapping; these do not depend on Dockerfile EXPOSE.

**6. ENTRYPOINT vs CMD?**
ENTRYPOINT selects the executable. CMD supplies defaults/arguments or a default command when no ENTRYPOINT exists. My image always starts dotnet PaymentRequest.Api.dll; the init container appends --init-db. In Kubernetes command overrides image entrypoint, while args overrides image default arguments.

**7. Why did your container fail to reach the database with localhost?**
Inside a container localhost refers to that container. Compose uses db:5432; Kubernetes uses postgres:5432 within the namespace. Native laptop code uses the published host port 5434.

**8. What is stored in a Docker volume?**
My PostgreSQL data, and separately Jenkins home. They survive container replacement. Volumes are not backups and do not automatically provide host failure protection.

**9. What is Jenkins agent versus controller?**
Controller schedules jobs, stores configuration and serves UI; agents execute work. My isolated lab runs one executor on the controller for simplicity. For production I would separate agents and controller and limit credentials/workload trust.

**10. How do you handle Jenkins credentials?**
I stored the kubeconfig as a Secret file and injected it with withCredentials. I kept it and DB secrets out of Git. This lab uses an administrator identity only in its dedicated cluster; production needs scoped deployment RBAC and managed secret handling.

**11. How are builds triggered?**
SCM polling every approximately two minutes after the first build. It detects new commits. I did not configure a webhook because Jenkins was only reachable through an SSH tunnel. I can explain how a webhook would differ without claiming I implemented it.

**12. What happens if tests fail?**
The test container exits nonzero; Jenkins marks the stage failed and stops before deploy. I intentionally pushed a failing assertion and verified the deployed version stayed unchanged. Runtime smoke failure happens after deployment and requires recovery; our pipeline does not automatically roll back.

**13. Deployment vs StatefulSet?**
Deployment suits interchangeable stateless API replicas. StatefulSet provides stable identities and associated claims for the practice DB. StatefulSet does not itself implement PostgreSQL replication or backup.

**14. Liveness vs readiness vs startup?**
Startup allows initialization before the other probes. Readiness decides whether the Pod should receive Service traffic. Liveness decides whether to restart the container. During my DB outage the API process stayed live but became unready. I separately broke a probe path and checked events/restarts.

**15. Service types?**
ClusterIP is cluster-internal. NodePort exposes a port on nodes. LoadBalancer requests external load-balancer integration where supported. I used NodePort 30080 mapped to Ubuntu loopback 18080 by kind, then accessed it through SSH. I did not create a cloud load balancer. Ingress is HTTP routing implemented by a controller, not merely a Service type.

**16. How does rolling deployment work?**
Deployment creates a new ReplicaSet and replaces old Pods gradually. My strategy allows one extra Pod and zero planned unavailability. Kubernetes waits for readiness. I observed an invalid new image stall the rollout while healthy old replicas remained. This depends on capacity and does not guarantee all requests are uninterrupted.

**17. How do you roll back?**
Inspect history, undo to a known good revision, wait for rollout and verify version/behavior. I then revert the bad Git change to keep source consistent. Application rollback does not reverse schema changes or external effects.

**18. How do you debug CrashLoopBackOff?**
Describe the Pod and inspect current/previous logs, termination state, exit code, restart events, command and configuration. Distinguish process crash, OOMKilled and probe-induced restart. ImagePullBackOff is an earlier image retrieval failure, not the same symptom.

**19. CPU/memory requests and limits?**
Requests influence scheduling; limits constrain consumption. In the manifest 100m is one-tenth CPU, 500m one-half CPU. CPU limiting throttles; exceeding the memory limit may kill the process. I would size from measurements rather than treat example values as production guidance.

**20. ConfigMap vs Secret?**
ConfigMap holds non-sensitive configuration; Secret holds sensitive values with access controls. Secret base64 is encoding, not encryption. My secret was injected as an environment variable, so changing the object does not update that variable in an already running process; a restart/redeployment and correct database rotation are needed.

**21. Why can both API replicas process the same idempotency key safely?**
They share a durable database uniqueness rule. A read-then-insert race is handled by catching the specific unique violation and returning the existing record. In-memory dictionaries cannot coordinate multiple replicas or survive restart. This guarantees one recorded row in the lab, not exactly-once external payment execution.

**22. Does two replicas mean high availability?**
It helps process availability, but both run on one node and share a single DB replica. Server loss or database outage still affects the whole application. Multi-node scheduling and proper DB resilience are additional requirements.

**23. How do you identify what was deployed?**
Commit/build image tag, Jenkins build record, manifest artifact and /version response. For stronger artifact identity I would record/deploy digests and promote the same immutable image across environments.

**24. Why use a registry?**
It distributes versioned artifacts to different nodes/environments with access controls. My first pipeline loads images into a single local kind cluster, which is a practice shortcut. If I complete the optional registry exercise, I can explain its tag/push/pull behavior separately.

**25. How did AI help you?**
AI helped scaffold the project and explain configurations. I personally ran the commands, inspected manifests and logs, tested duplicate behavior, fixed deliberate failures and verified deployment. I take responsibility for validation and can explain the files. I do not present AI-generated configuration as proof that I independently operated production infrastructure.

**26. What would you change for a real financial service?**
Authentication/authorization, auditable business operations, robust idempotency and reconciliation, safe transaction boundaries, encryption and secret lifecycle, least privilege, observability, tested backups, reviewed migrations and controlled promotion of immutable artifacts. Requirements vary; I would ask about consistency, availability, audit and recovery targets before choosing infrastructure.

**27. Tell me about a deployment incident.**
Use a drill you actually completed: “I changed the image reference to a missing tag. Rollout stalled, new Pod events showed ImagePullBackOff, and old replicas remained healthy. I checked the image/tag and kind runtime availability, rolled back, then verified version and smoke requests.” Label it a lab incident. Do not invent a production outage.

**28. How would you lead a team using AI for infrastructure?**
Require engineers to explain changes and review rendered manifests, secrets, privileges and failure behavior. Use tests, code review and deployment evidence. Keep credentials/customer data out of unauthorized tools. AI accelerates drafting; accountable people validate and operate the result.

## 14. Self-assessment and final rehearsal

Without opening the guide, draw your actual data/build flow and explain it in three minutes. Then demonstrate:

- One Git change reaches /version automatically.
- A failing test blocks deployment.
- A deleted API Pod is replaced.
- A saved record survives DB Pod replacement.
- A bad image is diagnosed from events and rolled back.
- A DB outage removes API readiness without liveness restart loops.
- You can find logs, endpoints, credentials usage, image tag and test artifacts.

For every file, answer: why does it exist, who reads it, when is it used, and what fails if it is wrong? If you cannot explain a line, look it up and test its behavior. That is the difference between running generated commands and owning the workflow.

A truthful closing statement after completing the lab:
“I previously used AI assistance for Docker/Jenkins/Kubernetes configurations. To deepen my understanding, I independently executed a full .NET deployment lab from local development and Git through Jenkins into Kubernetes on Ubuntu. I tested failures, recovery and rollback, and I can walk you through the configuration and evidence. My production responsibilities were [state only your actual responsibilities].”

Do not promise yourself a pass. This exercise materially improves your ability to explain and diagnose the technologies, while the interview will also assess .NET leadership, design, communication and domain knowledge.

## 15. Targeted cleanup

Keep the lab through the interview if it is affordable and permitted. When finished, these remove only the specifically named lab resources:
```bash
# SERVER: removes Jenkins container but preserves its home volume
docker rm -f ci-jenkins
# SERVER: destroys this practice Kubernetes cluster and its local data
kind delete cluster --name devops-lab
# LAPTOP: removes this Compose project, retaining its named database volume
docker compose down
```
Delete lab-jenkins-home or Compose database volumes only when you intentionally want their stored data removed. Revoke practice credentials and remove temporary kubeconfig copies. Do not run system-wide prune on a shared server.

## 16. Official references and validation status

Installation and conceptual references:
- Docker Desktop/WSL: https://docs.docker.com/desktop/features/wsl/
- Docker Ubuntu: https://docs.docker.com/engine/install/ubuntu/
- .NET Ubuntu: https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu
- .NET container images: https://learn.microsoft.com/en-us/dotnet/core/docker/container-images
- kind quick start and image loading: https://kind.sigs.k8s.io/docs/user/quick-start/
- kubectl Linux installation: https://kubernetes.io/docs/tasks/tools/install-kubectl-linux/
- Jenkins Pipeline: https://www.jenkins.io/doc/book/pipeline/jenkinsfile/
- Jenkins Docker installation: https://www.jenkins.io/doc/book/installing/docker/
- Kubernetes probes: https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/
- Kubernetes Deployments: https://kubernetes.io/docs/concepts/workloads/controllers/deployment/
- GitHub Git authentication: https://docs.github.com/en/authentication
- GitHub CLI login: https://cli.github.com/manual/gh_auth_login
- .NET developer installer: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script

Prepared 1 October 2026. The supplied kit has been statically inspected, its Python and shell syntax checked, XML/YAML parsed and template placeholders checked. The authoring environment has no Docker daemon or .NET SDK, so the end-to-end stack has NOT been executed here. Your checkpoint results are the runtime verification. Installation downloads, resource capacity, server policies and future image updates can affect execution. If an error occurs, capture the first failing command and redacted output, then diagnose that layer before advancing.
