# Linux WireGuard 中转

适用于 Debian／Ubuntu + systemd。服务器需要可访问的公网 UDP 端口；此模板默认 `10099`，虚拟网段 `10.77.42.0/24`。它只转发玩家间数据，不是独立游戏服务端。

## 密钥与配置

在可信设备上为服务器和每个玩家分别生成一对 WireGuard 密钥，例如安装 `wireguard-tools` 后执行：

```bash
umask 077
wg genkey | tee server.key | wg pubkey > server.pub
wg genkey | tee player1.key | wg pubkey > player1.pub
```

每位玩家重复生成，不能共用私钥。私钥只放在该设备自己的配置中；服务器的 `[Peer]` 只需要玩家公钥。不要提交密钥、已填好的 `.conf` 或完整部署目录。

1. 将 `ersc-vps.conf.example` 复制为 `ersc-vps.conf`，填写服务器私钥，按玩家数量添加 `[Peer]`，使用玩家公钥及唯一的 `/32` 虚拟地址。
2. 将 `player.conf.example` 复制为每个人自己的 `.conf`，填写个人私钥、服务器公钥和公网 Endpoint。文件名使用 WireGuard 支持的字母、数字、下划线等，避免空格。
3. 在云安全组中开放对应 UDP 端口。
4. 将服务端配置和 `install-vps.sh` 放在服务器同一目录，检查内容后执行 `sudo bash install-vps.sh`。安装脚本拒绝覆盖已有 `/etc/wireguard/ersc-vps.conf`，安装 WireGuard／iptables、启用 IP 转发并启动服务。
5. Windows 玩家通过 WireGuard GUI 导入并激活自己的 `.conf`。

安装前先确认虚拟网段不与已有 VPN／局域网冲突。服务器需具备 iptables 支持，已有防火墙规则也需要允许该转发方式。

## 发现其他玩家

`custom_broadcasts.txt.example` 提供五个玩家地址示例。将实际参与者地址填写到游戏的 `steam_settings/custom_broadcasts.txt`，所有玩家保持相同发现列表。当前配套兼容接口使用游戏 TCP/UDP `47584`。

启动器的服务器地址与端口应与 WireGuard Endpoint 一致，但修改启动器字段不会自动编辑 WireGuard。

## 检查

```bash
systemctl status wg-quick@ersc-vps
wg show ersc-vps
```

每位玩家激活配置后，可检查 `10.77.42.1` 的可达性及 `wg show` 中的握手时间。发现房间还要求游戏、Mod、兼容层版本一致、联机密码相同、玩家 SteamID 各不相同。

模板阻止玩家通过此隧道访问服务器其他服务或将其当成互联网出口。它允许游戏网段内玩家互通；WireGuard 配置也不替代游戏侧的房间密码与身份检查。
