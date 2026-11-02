# Small router for a classroom / LAN session

One small router (or travel router) is enough for a class: the router hands out IPs, the
teacher's laptop is the host, students join the same SSID/Ethernet. **No internet, no campus
Wi-Fi, no port forwarding.**

```
                 [ small router ]   WAN: leave EMPTY (no campus wall port)
                   LAN / Wi-Fi
        ┌──────────────┼──────────────┬──────────────┐
   teacher (host)   student 1      student 2      student 3
   192.168.50.10    DHCP           DHCP           DHCP
```

## Router settings

| Setting | Value | Why |
|---|---|---|
| Mode | **Router mode** (not AP/bridge) | it must provide DHCP; there is no upstream DHCP |
| WAN port | **leave empty** (do not plug into a campus wall port) | avoids DHCP conflicts, captive portals and campus firewall policy |
| LAN subnet | e.g. `192.168.50.0/24` (avoid `192.168.1.x` / your home subnet / campus `10.x`) | no conflicts with other networks |
| DHCP | **on**, pool `192.168.50.100–200`, lease 1 day | every student gets an address automatically |
| Host address | DHCP reservation **or** static `192.168.50.10` | the host IP must be stable so students can type it |
| Wireless | SSID `CGDD-MP`, WPA2-PSK (WPA3 mixed only if all laptops support it) | simple, works with old Windows/macOS |
| **Client/AP isolation** | **OFF** | the whole point: laptops must reach each other |
| Guest network | **OFF** | guest networks isolate clients by design |
| Remote admin / WAN access | OFF | classroom only |
| Firewall on router | default is fine | LAN traffic is not filtered; no port forwarding needed |

> If the router's admin page has "Wireless Isolation", "AP Isolation" or "Client Isolation",
> turn it off. This is the single setting that silently breaks LAN multiplayer.

## Host machine

1. Connect by **Ethernet** if possible (cable to a LAN port); Wi-Fi also works.
2. **Turn off VPN / proxy** — a VPN as default route can make replies bypass the LAN.
3. Set the stable address: DHCP reservation on the router, or static `192.168.50.10/24`,
   gateway `192.168.50.1`, DNS empty/`192.168.50.1`.
4. Allow incoming **UDP 7777**:
   - Windows: first run → allow on **Private** networks; if the network is Public, switch it
     to Private (Settings → Network → Ethernet/Wi-Fi → Private network) or allow the app on
     Public too.
   - macOS: System Settings → Network → Firewall → allow `MPGame` incoming (or leave the
     firewall off for the class session).
5. Launch the game → **Host**. Press `F2`: write down the LAN IP, port (`7777`) and `gameId`.
6. Disable sleep; keep the charger connected.

## Students

1. Connect to the router (same SSID, or Ethernet).
2. **No VPN.** Allow the app through the firewall (Windows: Private network; macOS:
   right-click → Open the first time / allow incoming if prompted).
3. Launch the same build (same scene/config as the host), enter the host IP
   `192.168.50.10`, click **Join**. `F2` should show `role=Client`, the server address and
   `gameId`.
4. If they get **wrong game**, they are running a different build/scene/config; **server
   full** means `Max Players` is too small.

## Before class: 5-minute test

1. Router configured, host + one student connected.
2. Host starts **Host**; student joins `192.168.50.10`.
3. Host log shows `[MP] INFO SERVER_READY` and `CLIENT_JOINED`; both see each other.
4. Add a second student, then scale to the class.
5. Ping is optional: `ping 192.168.50.10` may be blocked by a firewall even when the game
   works; the game join is the real test.

## Troubleshooting

| Symptom | Check |
|---|---|
| Join times out | wrong IP; client isolation on; VPN on; Windows firewall |
| Host can't be reached from Wi-Fi but works from Ethernet | client isolation / AP isolation |
| IP changes after reconnect | DHCP reservation or static IP for the host |
| Only some students can join | DHCP pool too small; max players too low |
| Works for 2, fails for 4 | router client limit / Wi-Fi congestion; use 5 GHz or Ethernet |
| No internet but game works | expected — the game needs only LAN |

If the router is a travel router with an "AP mode" switch, use **Router mode** and leave WAN
empty; AP mode expects an upstream DHCP server which is not there.
