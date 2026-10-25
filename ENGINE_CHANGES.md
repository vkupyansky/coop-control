# Changes for RageCoop maintainers

## 🇬🇧 English · [🇷🇺 Читать по-русски →](ENGINE_CHANGES.ru.md)

These changes target the **tested RageCoop 1.5.12.15 binaries** listed in the release manifest. They are applied at runtime through Harmony patches that first check the exact method they change: if a method looks different, the patch is not applied. RageCoop's files on disk are not modified, and these changes are not claimed to be merged upstream. Newer nightly builds differ; whether they still have these issues has not been checked.

## Fixed

| Area | Problem in the tested build | What the toolkit does |
| --- | --- | --- |
| Vehicle creation | An unbounded sleep loop inside a game callback | Requests limited per frame, cancelled when the session changes |
| Projectile cleanup | The wrong new projectile could be picked; a missing shooter stopped the cleanup | Checks the projectile's identity; cleanup works without a shooter |
| Player lists | Readers raced with changes to the player roster and indexes | Short coordinated operations and snapshots |
| Resource transfer | Parallel transfers shared one stream position; a file of the same size was taken as up to date | Coordinated stream access; this resource is named by its content |
| Server requests | Races in the response table, timeouts and sender checks | Bounded waits, sender/type/state checks and cleanup |
| Object lifecycle | A delayed removal could hit a replaced connection or owner | Session and ownership are re-checked before removal |
| Packet reading | Lengths could allocate memory before the remaining data was checked; some codecs were asymmetric | Bounds are checked before allocation; affected values round-trip |
| Network fragments | Fragment groups and the large-buffer cache had no proper limits | Size and count limits, expiry, cleanup on disconnect and shutdown |
| Message delivery | Unrelated states shared one ordered channel | Separate delivery rules on the supported paths |
| Settings and log | An invalid settings file was overwritten; the logger locked on a replaceable string | Settings are validated without overwriting and created atomically; the logger uses a stable lock |
| Client loading | Resource loading lacked strict path and source checks | Early checks of message sources and download paths |
| Vehicle damage | A full snapshot of a repaired car could keep the previous damage | Damage is reset when a full snapshot carries none |
| Remote wrecks | A remote car's wreck exploded again on every update that revived its copy | One visible explosion per wreck; repeats are silent |
| Name tags | Tags were converted to pixels in another coordinate space and lifted 100 px, drifting sideways on non-16:9 screens | Drawn at a world point above the head |
| Client log | On disconnect the log stopped, expecting an SHVDN reload; without one it stayed stopped for the rest of the game | The log resumes in the same file when the resource starts again |
| Voice chat | Recording stopped only at the next key press, so a released push-to-talk key kept the microphone open; voice was off on the server and in the client by default | Recording stops when the key is released; voice is on by default and starts a few seconds after the map has loaded, with an off switch in Settings and `COOP_CONTROL_VOICE=0` on the server |
| Vehicle weapons | Shots of vehicles missing from RageCoop's gun table (the Lazer and others) were not replayed for other players, and every shot logged a warning | Gun positions are taken from the vehicle's own weapon bones; the warning is logged once per vehicle |

## Added

| Area | Before | Now |
| --- | --- | --- |
| Shared parked vehicles | Both games create the same parked cars, boats and planes in the same spots, and both copies were shown | One shared copy (the lower player number's); shop clerks and NPCs at fixed spots stay one per player |
| Removed traffic | The server does not relay removals: an NPC car or pedestrian removed by its owner's game stood still for others and vanished after 10 s | A silent copy near another player is taken over by that player's game and lives on as its traffic |
| Nearby copies | Copies per owner were capped by the soft limit divided by the player count, so nearby traffic appeared late or without a driver | Cars and NPC drivers within 150 m of the local player are always copied |

## Removed

- The server browser's assignment of a global callback that accepted any certificate; normal certificate checks apply.
- The search that removed vehicles within 2 m when a new one was created.
- The unbounded sleep loop in vehicle creation (replaced as described above).

## Known issues

- NPCs act in the game that controls them; for other players they are copies and may move less smoothly.
- NPC traffic created by another player's game far from you can still appear or disappear as it reaches you.
