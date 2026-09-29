---
name: "RTS Network Authority"
description: "Use when changing network messages, multiplayer commands, session snapshots, or host/client gameplay behavior."
applyTo:
  - "src/Network/**/*.cs"
---

# Multiplayer Network Guidelines

- Keep gameplay authoritative on the host: validate requests and permissions before mutating world state, then publish the resulting command to peers. Clients must not independently decide gameplay outcomes.
- Background network tasks handle transport and enqueue messages only. Apply messages that touch the world through the existing game-thread update path.
- Preserve command ordering. When adding persistent state, include it in the session snapshot and apply it before the joining client resumes simulation.
- Use `NetworkHandler.EstimatedHostTime` for timestamps compared across peers; do not compare host timestamps with local `GameTime` values.
- Serialize through `NetworkJson.Options`. When changing the wire format, review its converters and `NetworkHandler.ProtocolVersion` for compatibility.
- Follow the existing request-to-host-to-command flow described in [the network architecture](../../AI/Netzwerk-Architektur.md).