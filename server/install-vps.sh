#!/usr/bin/env bash
set -euo pipefail
if [ "$(id -u)" != 0 ]; then echo 'Run as root.' >&2; exit 1; fi
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
test -f ersc-vps.conf
if [ -e /etc/wireguard/ersc-vps.conf ]; then echo 'Existing relay config found; refusing to overwrite keys.' >&2; exit 1; fi
if ip link show ersc-vps >/dev/null 2>&1; then echo 'Interface name already exists.' >&2; exit 1; fi
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends wireguard-tools iptables
install -d -m 700 /etc/wireguard /var/lib/ersc-vps
sysctl -n net.ipv4.ip_forward > /var/lib/ersc-vps/previous-ip-forward
install -m 600 ersc-vps.conf /etc/wireguard/ersc-vps.conf
printf 'net.ipv4.ip_forward=1\n' > /etc/sysctl.d/80-ersc-vps.conf
sysctl -p /etc/sysctl.d/80-ersc-vps.conf
systemctl enable --now wg-quick@ersc-vps
systemctl is-active wg-quick@ersc-vps
ip -4 addr show dev ersc-vps
echo 'Linux WireGuard relay installed. Verify the cloud security group permits the configured UDP port.'
