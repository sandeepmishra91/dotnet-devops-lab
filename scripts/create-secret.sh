#!/usr/bin/env bash
set -euo pipefail
if [ "$(kubectl config current-context)" != "kind-devops-lab" ]; then
  echo 'Select the kind-devops-lab context first.' >&2; exit 1
fi
umask 077
secret_file=$(mktemp)
trap 'rm -f "$secret_file"' EXIT
lab_password=$(openssl rand -hex 24)
printf 'password=%s\nconnection=Host=postgres;Port=5432;Database=payments;Username=labuser;Password=%s\n' \
  "$lab_password" "$lab_password" > "$secret_file"
kubectl -n devops-lab create secret generic lab-db --from-env-file="$secret_file"
echo 'Created lab-db. Do not regenerate its password against an existing database volume.'
