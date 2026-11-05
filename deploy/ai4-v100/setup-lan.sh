#!/usr/bin/env bash
set -euo pipefail

umask 077
config_dir="$HOME/.config/gpu-dashboard"
install -d -m 700 "$config_dir"

if [[ ! -f "$config_dir/tls.crt" || ! -f "$config_dir/tls.key" ]]; then
    openssl req -x509 -newkey rsa:2048 -noenc -sha256 -days 365 \
        -subj /CN=ai4-v100 -addext subjectAltName=IP:10.0.0.44 \
        -addext basicConstraints=critical,CA:FALSE -addext extendedKeyUsage=serverAuth \
        -out "$config_dir/tls.crt" -keyout "$config_dir/tls.key"
fi

if [[ ! -f "$config_dir/lan.env" ]]; then
    env_file=$(mktemp "$config_dir/lan.env.XXXXXX")
    trap 'rm -f "$env_file"' EXIT
    {
        printf 'DashboardAccess__Password=%s\n' "$(openssl rand -hex 24)"
        printf 'Kestrel__Endpoints__Http__Certificate__Path="%s/tls.crt"\n' "$config_dir"
        printf 'Kestrel__Endpoints__Http__Certificate__KeyPath="%s/tls.key"\n' "$config_dir"
    } > "$env_file"
    mv "$env_file" "$config_dir/lan.env"
fi

install -Dm644 "$(dirname "$0")/lan.conf" \
    "$HOME/.config/systemd/user/gpu-dashboard.service.d/lan.conf"
