# Coop Control

## 🇬🇧 English · [🇷🇺 Читать по-русски →](README.ru.md)

**A friendly menu, GTA Online interiors and stability fixes for playing GTA V Legacy together with RageCoop.**

By **Vladislav Kupyansky (vkupyansky)** · [GitHub](https://github.com/vkupyansky) · Telegram [@skibidessa](https://t.me/skibidessa)

Work in progress · MIT · Menu in English or Russian

**[Download v0.3.0-wip](https://github.com/vkupyansky/coop-control/releases/latest)** · [All releases](https://github.com/vkupyansky/coop-control/releases) · [Report a problem](https://github.com/vkupyansky/coop-control/issues/new/choose)

[RageCoop](https://ragecoop.com/) runs the multiplayer itself. Coop Control is an unofficial add-on for it: a menu with vehicles, a garage, teleports, GTA Online interiors and shared world settings, one installer that sets everything up, and fixes for a number of problems in the supported RageCoop build. RageCoop's own files are not modified.

### What Coop Control adds

- **Vehicles:** find and spawn any GTA vehicle, keep favourites and save tuned cars in a garage (up to 64). The garage and favourites are kept on the server under your RageCoop name, so they follow you to any PC.
- **Tuning:** use the normal Los Santos Customs; other players see your car exactly as you tuned it.
- **Driving:** repair a car as its driver or passenger, put it back on its wheels, hold a key for a boost.
- **Drive through traffic:** a shared switch that lets cars, motorcycles, quads and bicycles pass through NPC traffic. On a motorcycle the rider and passenger pass through too. Players, spawned cars, pedestrians and the world stay solid. The server remembers the switch after a restart.
- **GTA Online interiors:** 61 interiors behind their real street doors — the Diamond Casino and its penthouse, apartments and houses, CEO offices with their garages, biker clubhouses and businesses, a bunker, a hangar, a facility, a nightclub, an arcade, an auto shop, the LS Car Meet, Record A Studios, the Music Locker, warehouses, agencies, mansions and more. Each has its Online map icon; walk into the yellow circle at the door to go in.
- **Special places:** Cayo Perico (loads as you fly in), North Yankton, the aircraft carriers, yachts, a cargo ship and more, with map icons.
- **Shared world:** one map marker, weather and time of day for everyone; freeze time or weather; snow on the ground with tyre and foot tracks in snowy weather.
- **Clean free roam:** no mission markers, random events, side jobs or phone calls interrupting you while you play together. Shops and Los Santos Customs work as usual.
- **Teleports:** to your map marker or to a friend, including straight into a free passenger seat.
- **Voice chat:** hold N to talk; everyone on the server hears you. You respawn next to your partner instead of at a hospital.
- **Online radio and weapons:** GTA Online radio stations on the radio wheel; weapon attachments and colours, Mk II included.
- **Character:** weapons, ammo, health, armour and abilities.
- **Controls:** keyboard and gamepad; type Russian or English in search and garage names.
- **Language:** Settings → Язык / Language → English / Русский. Each player chooses their own.

### What we changed in RageCoop

The changes are applied while the game runs, through checked patches: if the RageCoop build differs from the supported one, a patch is simply not applied. In short:

- **Fixed:** hangs when creating vehicles, broken projectile cleanup, races in player lists, resource downloads and server requests, packet reading that allocated memory before checking sizes, a repaired car keeping its old damage, a wreck exploding again on every update, name tags drifting sideways on non-16:9 screens, the client log stopping after a disconnect, jet weapons whose shots other players did not see, a microphone that stayed on after the talk key was released.
- **Added:** cars and boats that both games park in the same spot become one shared copy; traffic removed by one player's game is taken over by the other game instead of freezing; cars and drivers within 150 m are always shown.
- **Removed:** the server browser setting that accepted any certificate; a search that replaced vehicles within 2 m when a new one was created.

The full list is in [engine changes](ENGINE_CHANGES.md).

### Screenshots

Real screenshots from a two-player session, with the menu in Russian.

![Shared free roam with a friend](screenshots/01-shared-play.png)

<details>
<summary>Menu, vehicle catalogue, garage and friends</summary>

**Vehicles, teleports and shared settings**

![Main menu](screenshots/02-main-menu.png)

**GTA vehicle catalogue**

![Standard GTA vehicles in the catalogue](screenshots/10-vehicle-catalog.png)

**Saved cars, colours and tuning**

![Personal garage with a saved car selected](screenshots/09-garage.png)

**Teleport to a friend**

![Friend selected in the partner menu](screenshots/08-partners.png)

</details>

### Install — players

Supported game: **GTA V Legacy 1.0.3889** on Windows (Enhanced is not supported). The download already includes the compatible RageCoop client and server.

**First time**

1. Start Story Mode once, then close GTA. Turn off BattlEye for Story Mode in the Rockstar Games Launcher; on Steam or Epic use the official `-nobattleye` launch option ([Rockstar's guide](https://support.rockstargames.com/articles/1nenwhZlVrJY6CTFeSS2Fx/grand-theft-auto-online-battleye-faq)).
2. Download `CoopControl-Addon.zip`, extract it (right-click → Extract All) and run **Install.exe**. If Windows shows "Windows protected your PC", choose More info → Run anyway.
3. The installer finds GTA V Legacy, checks its version, installs everything next to `GTA5.exe`, checks Visual C++ and .NET Framework 4.8, and tells you what to do next. If it cannot find the game, drag `GTA5.exe` into its window.
4. ScriptHookV may not be bundled: when asked, click Download on [its author's page](https://www.dev-c.com/gtav/scripthookv/) and leave the ZIP in Downloads — the installer takes the files from it.
5. Start GTA → Story Mode → press **F9** → enter the host's address. Press **J** to open the menu.

**Updating** — download the new `CoopControl-Addon.zip`, extract it and run **Install.exe** again. It replaces only what changed; your garage, favourites and menu settings stay. Then start GTA again.

On VK Play, do all of this inside the cloud PC, on its persistent game drive. If the installer cannot run, copy everything inside the `client` folder next to `GTA5.exe` by hand, together with `ScriptHookV.dll` and `dinput8.dll` from ScriptHookV.

The menu itself is downloaded from the server when you connect.

### Install — host (runs the server for friends)

1. Extract `CoopControl-Addon.zip` and put its `server` folder anywhere you can write to. GTA is not needed on the server machine.
2. Windows: double-click **Start server** (the .NET runtime is included). Linux or macOS: run `sh start-server.sh` with the x64 [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) installed.
3. The window shows the addresses to give friends — see [Connecting players](#connecting-players). The first run creates `Settings.xml` and listens on UDP **4499**. The server is not listed publicly; play with people you trust, since everyone connected can use the shared tools.
4. To stop: **Stop server** (Windows) or `sh stop-server.sh`, or Ctrl+C in the server window. The garage and favourites are saved on every change.

**Updating the server:** stop it, replace the files and keep `Settings.xml` and player data. `Resources/Server/CoopAdmin` must contain only `CoopAdmin.dll`. Use the RageCoop **1.5.12.15** files from this release: another nightly with the same number is not necessarily compatible.

### Staying up to date

New versions bring new places, fixes and features, and players get the new menu only after the host updates the server. So:

- The installer says when a newer version is out and offers to open its page.
- The server checks for a newer version when it starts: the host sees it in the server window, and every player who joins gets one chat line about it. To turn the check off, set the environment variable `COOP_CONTROL_UPDATE_CHECK=0`.
- On GitHub, click **Watch → Custom → Releases** to get an e-mail when a new version is published.

To update: the host updates the server first (see above), then each player runs the new **Install.exe**.

### Report a problem

[Open an issue](https://github.com/vkupyansky/coop-control/issues/new/choose) and choose **Problem / Ошибка**. Describe what you did and what happened, and attach the logs: `install-log.txt` next to Install.exe, `scripts/RageCoop/Data/RageCoop.Client.log` in the GTA folder, or the text from the server window. Questions and ideas: Telegram [@skibidessa](https://t.me/skibidessa).

### Connecting players

Pick the row that matches where your friend plays. The server listens on **UDP 4499**.

| Where the friend is | What the host does | Address the friend enters |
| --- | --- | --- |
| On the server PC itself | Nothing | `127.0.0.1:4499` |
| On the same home network | Nothing | The server PC's local address, e.g. `192.168.1.20:4499` |
| Elsewhere, and the host has a public IP | Forward UDP 4499 on the router | The host's public address, e.g. `203.0.113.5:4499` |
| Elsewhere, and the host has no public IP | A virtual network or a tunnel (below) | The address the app shows |

**Does the host have a public IP?** Open the router's status page and compare its "Internet / WAN" address with the one shown by any "what is my IP" site. If they match, the IP is public. If the router shows `100.64.x.x`–`100.127.x.x`, `10.x.x.x` or `192.168.x.x`, the provider shares one address between customers (CGNAT) and port forwarding will not work.

**Port forwarding (public IP).** In the router settings add a rule: UDP 4499 → the server PC's local address, port 4499. Give that PC a fixed local address (DHCP reservation) and allow UDP 4499 in its firewall. If a VPN on the host sends all traffic through itself, incoming players may not reach the server: exclude the server or switch the VPN off.

**Virtual network (no public IP).** Everyone installs the same app and joins one network; the friend then uses the server's address inside that network with `:4499`.

- [Tailscale](https://tailscale.com/) — Windows, macOS, Linux; the free plan covers a small group of friends.
- [ZeroTier](https://www.zerotier.com/) — Windows, macOS, Linux.
- [Radmin VPN](https://www.radmin-vpn.com/) — free and popular, but Windows only.

**Tunnel (no public IP, friends install nothing).** A service such as [playit.gg](https://playit.gg/) runs a small agent on the server PC and gives a public address for local UDP port 4499. Friends just enter that address. Handy when a friend plays from a cloud PC where apps cannot be installed; free-plan limits change, and the extra hop can add a little delay.

Which to choose: a public IP → port forwarding (fastest). No public IP → a virtual network; if friends cannot install anything → a tunnel.

### Controls

| Action | Keyboard | Xbox |
| --- | --- | --- |
| Coop Control menu | J | Hold LB + RB, press ↓ |
| Repair | K | Hold LB + RB, press ↑ |
| Select / back | Enter / Esc | A / B |
| Navigate | Arrow keys | D-pad |
| Boost | Hold O | Hold A while driving (duck still works) |
| Voice chat | Hold N | — |
| GTA pause / map | Esc | Menu / Start |

While you type in a text field, letter and editing keys belong to that field. Menu preferences are saved on your PC; the garage and favourites on the server. Esc pauses only your own game: while you are in the pause menu, your character and the traffic your game controls stand still for others.

### Optional: ReShade

[ReShade](https://reshade.me) makes the picture a little brighter and sharper. It changes only your own screen and is optional — skip it on a weak PC. Run its setup → choose `GTA5.exe` → DirectX 10/11/12 → when it asks for a preset, choose `CoopControl.ini` from the `reshade` folder of the download. In game, Home opens the ReShade menu, where effects can be turned off. In multiplayer ReShade turns off depth-based effects itself, so the preset uses only colour and sharpness.

### Limits

- The radio station and on/off state are shared; the exact song position and personal music files are not.
- Parked cars, boats and planes that both games place in the same spot are shared as one; shop clerks and other NPCs at fixed spots are one per player. See also the [known issues](ENGINE_CHANGES.md#known-issues).
- Interiors are places to visit together; GTA Online activities inside them (casino games, business production, property purchases) are not included.

### Additional cars

Add-on car models are optional and installed by each player separately. Every player needs the same files and a matching menu catalogue. Downloading the models and their authors' permissions are not covered by this project's MIT licence.

### Build from source (developers)

Unzip both the source and the add-on ZIPs. Install the .NET SDK pinned in `work/RageCoopAdmin/global.json` (**10.0.402**). In that folder copy `References.example.props` to `References.props` and point `RageCoopClientDir` to the add-on's `client/scripts/RageCoop`, `RageCoopServerDir` to `server`, and `LemonUiDll` to `lib/net48/LemonUI.SHVDN3.dll` from [LemonUI.SHVDN3 2.2.0](https://www.nuget.org/packages/LemonUI.SHVDN3/2.2.0). Then, from `work/RageCoopAdmin`:

```sh
dotnet restore Client/RageCoopAdmin.Client.csproj --locked-mode -p:PublicRelease=true -warnaserror
dotnet build Client/RageCoopAdmin.Client.csproj -c Release --no-restore -p:PublicRelease=true -warnaserror
```

Build Server, Bootstrap, EarlyClient and Installer the same way. Packaging the add-on also needs the official SHVDN 3.7.0-nightly.188 and .NET Runtime 10.0.13 (win-x64) ZIPs in `outputs/public-deps`; their URLs and pinned digests are in `tools/prepare_public_release.py`. From the source root, `bash tools/check.sh --public-release` runs the public builds and checks (Windows with Git Bash, or macOS with CrossOver and .NET Framework 4.8).

### License

Our code is [MIT](LICENSE), © 2026 Vladislav Kupyansky (vkupyansky). RageCoop and the other components keep their own licences and credits, listed in [THIRD_PARTY.txt](THIRD_PARTY.txt). ScriptHookV and ReShade are downloaded from their authors separately.
