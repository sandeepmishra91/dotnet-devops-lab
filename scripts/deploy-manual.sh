#!/usr/bin/env bash
set -euo pipefail
image=${1:?Usage: bash scripts/deploy-manual.sh IMAGE RELEASE}
release=${2:?Provide a unique lowercase release such as manual-v1}
[[ "$release" =~ ^[a-z0-9][a-z0-9-]{0,39}$ ]] || { echo 'Invalid release'; exit 1; }
[[ "$image" =~ ^[a-zA-Z0-9._:/-]+$ ]] || { echo 'Invalid image'; exit 1; }
[ "$(kubectl config current-context)" = "kind-devops-lab" ] || { echo 'Wrong context'; exit 1; }
kubectl -n devops-lab get secret lab-db >/dev/null
kubectl -n devops-lab apply -f k8s/postgres.yaml
kubectl -n devops-lab rollout status statefulset/postgres --timeout=180s
sed -e "s|__IMAGE__|$image|g" -e "s|__RELEASE__|$release|g" k8s/init-db.yaml > release-schema.yaml
kubectl -n devops-lab apply -f release-schema.yaml
kubectl -n devops-lab wait --for=condition=complete "job/schema-$release" --timeout=120s
sed -e "s|__IMAGE__|$image|g" -e "s|__RELEASE__|$release|g" k8s/api.yaml > release-api.yaml
kubectl -n devops-lab apply -f release-api.yaml
kubectl -n devops-lab rollout status deployment/payments-api --timeout=180s
